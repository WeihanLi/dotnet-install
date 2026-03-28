using System.Formats.Tar;
using System.IO.Compression;

namespace DotNetInstallManager.Services;

internal sealed class ArchiveExtractor
{
    private readonly TextWriter _stdout;
    private readonly bool _verbose;

    public ArchiveExtractor(TextWriter stdout, bool verbose)
    {
        _stdout = stdout;
        _verbose = verbose;
    }

    public void Extract(string archivePath, string destinationRoot, bool overrideNonVersionedFiles, CancellationToken cancellationToken)
    {
        var extension = GetArchiveExtension(archivePath);
        var entries = ReadEntries(archivePath, extension, cancellationToken);
        var tempRoot = Path.Combine(Path.GetTempPath(), "dotnet-install-extract", Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempRoot);
            ExtractToTemp(archivePath, extension, tempRoot);
            ApplyEntries(entries, tempRoot, destinationRoot, overrideNonVersionedFiles, cancellationToken);
        }
        catch (InstallException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InstallException($"Failed to extract archive '{archivePath}': {ex.Message}", ex);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private void ApplyEntries(
        IReadOnlyList<ArchiveEntryDescriptor> entries,
        string tempRoot,
        string destinationRoot,
        bool overrideNonVersionedFiles,
        CancellationToken cancellationToken)
    {
        var directoriesToUnpack = GetDirectoriesToUnpack(entries, destinationRoot);
        if (_verbose)
        {
            var values = directoriesToUnpack.Count == 0 ? "<none>" : string.Join(";", directoriesToUnpack);
            _stdout.WriteLine($"Directories to unpack: {values}");
        }

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.RelativePath))
            {
                continue;
            }

            var versionPrefix = GetPathPrefixWithVersion(entry.RelativePath);
            if (versionPrefix is not null && !directoriesToUnpack.Contains(versionPrefix))
            {
                continue;
            }

            switch (entry.Kind)
            {
                case ArchiveEntryKind.Directory:
                    CreateDestinationDirectory(destinationRoot, entry.RelativePath);
                    break;
                case ArchiveEntryKind.File:
                    CopyFile(
                        tempRoot,
                        destinationRoot,
                        entry.RelativePath,
                        versionPrefix is not null || overrideNonVersionedFiles);
                    break;
                case ArchiveEntryKind.SymbolicLink:
                    CopySymbolicLink(
                        tempRoot,
                        destinationRoot,
                        entry.RelativePath,
                        versionPrefix is not null || overrideNonVersionedFiles);
                    break;
            }
        }
    }

    private static void CopyFile(string tempRoot, string destinationRoot, string relativePath, bool overwrite)
    {
        var sourcePath = GetAbsolutePath(tempRoot, relativePath);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var destinationPath = GetAbsolutePath(destinationRoot, relativePath);
        if (!overwrite && File.Exists(destinationPath))
        {
            return;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);

        if (!OperatingSystem.IsWindows())
        {
            TryCopyUnixFileMode(sourcePath, destinationPath);
        }
    }

    private static void CopySymbolicLink(string tempRoot, string destinationRoot, string relativePath, bool overwrite)
    {
        var sourcePath = GetAbsolutePath(tempRoot, relativePath);
        if (!PathExists(sourcePath))
        {
            return;
        }

        var destinationPath = GetAbsolutePath(destinationRoot, relativePath);
        if (!overwrite && PathExists(destinationPath))
        {
            return;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        DeleteExistingPath(destinationPath);

        var sourceFileInfo = new FileInfo(sourcePath);
        var sourceDirectoryInfo = new DirectoryInfo(sourcePath);
        var linkTarget = sourceFileInfo.LinkTarget ?? sourceDirectoryInfo.LinkTarget;
        if (string.IsNullOrEmpty(linkTarget))
        {
            CopyFile(tempRoot, destinationRoot, relativePath, overwrite: true);
            return;
        }

        if (Directory.Exists(sourcePath))
        {
            Directory.CreateSymbolicLink(destinationPath, linkTarget);
            return;
        }

        File.CreateSymbolicLink(destinationPath, linkTarget);
    }

    private static void TryCopyUnixFileMode(string sourcePath, string destinationPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(sourcePath);
            File.SetUnixFileMode(destinationPath, mode);
        }
        catch
        {
            // Ignore permission propagation failures and rely on default extraction behavior.
        }
    }

    private static void CreateDestinationDirectory(string destinationRoot, string relativePath)
    {
        var destinationPath = GetAbsolutePath(destinationRoot, relativePath);
        Directory.CreateDirectory(destinationPath);
    }

    private static HashSet<string> GetDirectoriesToUnpack(IReadOnlyList<ArchiveEntryDescriptor> entries, string destinationRoot)
    {
        var directories = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.Kind == ArchiveEntryKind.Directory)
            {
                continue;
            }

            var prefix = GetPathPrefixWithVersion(entry.RelativePath);
            if (prefix is null)
            {
                continue;
            }

            var destinationPath = GetAbsolutePath(destinationRoot, prefix);
            if (!Directory.Exists(destinationPath))
            {
                directories.Add(prefix);
            }
        }

        return directories;
    }

    private static IReadOnlyList<ArchiveEntryDescriptor> ReadEntries(string archivePath, string extension, CancellationToken cancellationToken) =>
        extension switch
        {
            ".zip" => ReadZipEntries(archivePath, cancellationToken),
            ".tar.gz" or ".tar" => ReadTarEntries(archivePath, extension, cancellationToken),
            _ => throw new InstallException($"Archive format '{extension}' is not supported for extraction.")
        };

    private static IReadOnlyList<ArchiveEntryDescriptor> ReadZipEntries(string archivePath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var result = new List<ArchiveEntryDescriptor>(archive.Entries.Count);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = NormalizeRelativePath(entry.FullName);
            if (string.IsNullOrEmpty(relativePath))
            {
                continue;
            }

            var kind = entry.FullName.EndsWith("/", StringComparison.Ordinal)
                ? ArchiveEntryKind.Directory
                : ArchiveEntryKind.File;

            result.Add(new ArchiveEntryDescriptor(relativePath, kind));
        }

        return result;
    }

    private static IReadOnlyList<ArchiveEntryDescriptor> ReadTarEntries(string archivePath, string extension, CancellationToken cancellationToken)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var archiveStream = OpenTarReadStream(fileStream, extension);
        using var reader = new TarReader(archiveStream, leaveOpen: false);

        var result = new List<ArchiveEntryDescriptor>();
        TarEntry? entry;
        while ((entry = reader.GetNextEntry(copyData: false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = NormalizeRelativePath(entry.Name);
            if (string.IsNullOrEmpty(relativePath))
            {
                continue;
            }

            if (entry.EntryType is TarEntryType.Directory)
            {
                result.Add(new ArchiveEntryDescriptor(relativePath, ArchiveEntryKind.Directory));
                continue;
            }

            if (entry.EntryType is TarEntryType.SymbolicLink)
            {
                result.Add(new ArchiveEntryDescriptor(relativePath, ArchiveEntryKind.SymbolicLink));
            }
            else if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile or TarEntryType.HardLink)
            {
                result.Add(new ArchiveEntryDescriptor(relativePath, ArchiveEntryKind.File));
            }
        }

        return result;
    }

    private static void ExtractToTemp(string archivePath, string extension, string tempRoot)
    {
        switch (extension)
        {
            case ".zip":
                ZipFile.ExtractToDirectory(archivePath, tempRoot, overwriteFiles: true);
                break;
            case ".tar.gz":
            case ".tar":
                using (var fileStream = File.OpenRead(archivePath))
                using (var archiveStream = OpenTarReadStream(fileStream, extension))
                {
                    TarFile.ExtractToDirectory(archiveStream, tempRoot, overwriteFiles: true);
                }

                break;
            default:
                throw new InstallException($"Archive format '{extension}' is not supported for extraction.");
        }
    }

    private static Stream OpenTarReadStream(Stream fileStream, string extension) =>
        extension switch
        {
            ".tar.gz" => new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false),
            ".tar" => fileStream,
            _ => throw new InstallException($"Archive format '{extension}' is not supported for extraction.")
        };

    private static string GetArchiveExtension(string archivePath)
    {
        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            return ".tar.gz";
        }

        return Path.GetExtension(archivePath).ToLowerInvariant();
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimEnd('/');
    }

    private static string? GetPathPrefixWithVersion(string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            if (IsVersionSegment(segments[index]))
            {
                return string.Join("/", segments.Take(index + 1));
            }
        }

        return null;
    }

    private static bool IsVersionSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var firstDot = segment.IndexOf('.');
        if (firstDot <= 0 || firstDot == segment.Length - 1)
        {
            return false;
        }

        return char.IsDigit(segment[0]) && char.IsDigit(segment[firstDot + 1]);
    }

    private static string GetAbsolutePath(string rootPath, string relativePath)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var combined = segments.Aggregate(rootPath, Path.Combine);
        var fullRoot = Path.GetFullPath(rootPath);
        var fullPath = Path.GetFullPath(combined);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootedPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) || fullRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!string.Equals(fullPath, fullRoot, comparison) && !fullPath.StartsWith(rootedPrefix, comparison))
        {
            throw new InstallException($"Archive entry '{relativePath}' resolves outside the installation root.");
        }

        return fullPath;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore temporary directory cleanup failures.
        }
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void DeleteExistingPath(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
            return;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private readonly record struct ArchiveEntryDescriptor(string RelativePath, ArchiveEntryKind Kind);

    private enum ArchiveEntryKind
    {
        Directory,
        File,
        SymbolicLink
    }
}
