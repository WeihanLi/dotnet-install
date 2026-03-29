using System.Diagnostics;
using System.Text;

namespace DotNetInstallManager.Services;

internal sealed record RemovalResult(
    string InstallRoot,
    string RequestedVersion,
    bool SdkOnly,
    bool DryRun,
    IReadOnlyList<string> MatchedPaths,
    IReadOnlyList<string> MissingPaths);

internal interface IPrivilegedRemovalExecutor
{
    bool TryDelete(string path, TextWriter stdout, bool verbose, out string? failureReason);
}

internal sealed class WindowsPrivilegedRemovalExecutor : IPrivilegedRemovalExecutor
{
    public bool TryDelete(string path, TextWriter stdout, bool verbose, out string? failureReason)
    {
        failureReason = null;

        if (!OperatingSystem.IsWindows())
        {
            failureReason = "Administrator retry is only supported on Windows.";
            return false;
        }

        if (verbose)
        {
            stdout.WriteLine($"Retrying removal with administrator privileges for \"{path}\"");
        }

        try
        {
            var script = BuildRemovalScript(path);
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {EncodePowerShellCommand(script)}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                failureReason = "Failed to launch elevated PowerShell process.";
                return false;
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                failureReason = $"Elevated removal process exited with code {process.ExitCode}.";
                return false;
            }

            if (Directory.Exists(path) || File.Exists(path))
            {
                failureReason = "Elevated removal completed but the path still exists.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    private static string BuildRemovalScript(string path)
    {
        var escapedPath = path.Replace("'", "''", StringComparison.Ordinal);
        return $$"""
        $path = '{{escapedPath}}'
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
        }
        """;
    }

    private static string EncodePowerShellCommand(string command) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
}

internal sealed class InstallRemover
{
    private readonly TextWriter _stdout;
    private readonly bool _verbose;
    private readonly IPrivilegedRemovalExecutor _privilegedRemovalExecutor;
    private readonly Action<string> _deletePath;

    public InstallRemover(TextWriter stdout, bool verbose)
        : this(stdout, verbose, new WindowsPrivilegedRemovalExecutor(), DeletePath)
    {
    }

    internal InstallRemover(
        TextWriter stdout,
        bool verbose,
        IPrivilegedRemovalExecutor privilegedRemovalExecutor,
        Action<string>? deletePath = null)
    {
        _stdout = stdout;
        _verbose = verbose;
        _privilegedRemovalExecutor = privilegedRemovalExecutor;
        _deletePath = deletePath ?? DeletePath;
    }

    public RemovalResult Remove(RemovalPlan plan, string installRoot, bool dryRun, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plan.RequestedVersion))
        {
            throw new InstallException("A version is required for removal.");
        }

        var normalizedRoot = Path.GetFullPath(installRoot);
        var matchedPaths = new List<string>();
        var missingPaths = new List<string>();

        foreach (var targetPath in ExpandTargetPaths(plan.Targets, normalizedRoot, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!PathExists(targetPath))
            {
                if (_verbose)
                {
                    _stdout.WriteLine($"Skipping missing path \"{targetPath}\"");
                }

                missingPaths.Add(targetPath);
                continue;
            }

            matchedPaths.Add(targetPath);

            if (dryRun)
            {
                _stdout.WriteLine($"Would remove \"{targetPath}\"");
                continue;
            }

            try
            {
                _deletePath(targetPath);
            }
            catch (Exception ex)
            {
                if (_privilegedRemovalExecutor.TryDelete(targetPath, _stdout, _verbose, out var retryFailureReason))
                {
                    _stdout.WriteLine($"Removed \"{targetPath}\" with administrator privileges");
                    TryDeleteEmptyParents(Path.GetDirectoryName(targetPath) ?? normalizedRoot, normalizedRoot);
                    continue;
                }

                var reason = string.IsNullOrWhiteSpace(retryFailureReason) ? ex.Message : $"{ex.Message} Elevated retry failed: {retryFailureReason}";
                if (OperatingSystem.IsLinux())
                {
                    reason = $"{reason} Try rerunning with sudo, for example: sudo dotnet-install remove {plan.RequestedVersion}";
                }

                throw new InstallException($"Failed to delete removal target '{targetPath}': {reason}", ex);
            }

            _stdout.WriteLine($"Removed \"{targetPath}\"");
            TryDeleteEmptyParents(Path.GetDirectoryName(targetPath) ?? normalizedRoot, normalizedRoot);
        }

        if (matchedPaths.Count == 0)
        {
            throw new InstallException(
                $"No SDK or runtime directories matching version '{plan.RequestedVersion}' were found under '{normalizedRoot}'.");
        }

        return new RemovalResult(normalizedRoot, plan.RequestedVersion, plan.SdkOnly, dryRun, matchedPaths, missingPaths);
    }

    private static IEnumerable<string> ExpandTargetPaths(
        IReadOnlyList<RemovalTarget> targets,
        string installRoot,
        CancellationToken cancellationToken)
    {
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (target.Kind)
            {
                case RemovalTargetKind.Directory:
                    yield return GetVersionPath(installRoot, target.RelativeRoot, target.MatchValue);
                    break;
                case RemovalTargetKind.DirectoryPattern:
                    foreach (var path in ExpandPatternDirectories(installRoot, target.RelativeRoot, target.MatchValue))
                    {
                        yield return path;
                    }

                    break;
                case RemovalTargetKind.FilePattern:
                    foreach (var path in ExpandFilePattern(installRoot, target.RelativeRoot, target.MatchValue))
                    {
                        yield return path;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<string> ExpandPatternDirectories(string installRoot, string relativeRootPattern, string version)
    {
        var separatorIndex = relativeRootPattern.LastIndexOf(Path.DirectorySeparatorChar);
        var altSeparatorIndex = relativeRootPattern.LastIndexOf(Path.AltDirectorySeparatorChar);
        var splitIndex = Math.Max(separatorIndex, altSeparatorIndex);
        var parentRelative = splitIndex >= 0 ? relativeRootPattern[..splitIndex] : string.Empty;
        var namePattern = splitIndex >= 0 ? relativeRootPattern[(splitIndex + 1)..] : relativeRootPattern;
        var parentPath = CombineRelative(installRoot, parentRelative);

        if (!Directory.Exists(parentPath))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(parentPath, namePattern, SearchOption.TopDirectoryOnly))
        {
            yield return CombineRelative(directory, version);
        }
    }

    private static IEnumerable<string> ExpandFilePattern(string installRoot, string relativeRoot, string filePattern)
    {
        var rootPath = CombineRelative(installRoot, relativeRoot);
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, filePattern, SearchOption.TopDirectoryOnly))
        {
            EnsureWithinInstallRoot(file, installRoot);
            yield return file;
        }
    }

    private static string GetVersionPath(string installRoot, string relativeRoot, string version) =>
        CombineRelative(installRoot, Path.Combine(relativeRoot, version));

    private static string CombineRelative(string installRoot, string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(installRoot, relativePath));
        EnsureWithinInstallRoot(path, installRoot);
        return path;
    }

    private static void EnsureWithinInstallRoot(string candidatePath, string installRoot)
    {
        var fullRoot = Path.GetFullPath(installRoot);
        var fullCandidate = Path.GetFullPath(candidatePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootedPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) || fullRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!string.Equals(fullCandidate, fullRoot, comparison) && !fullCandidate.StartsWith(rootedPrefix, comparison))
        {
            throw new InstallException($"Removal target '{candidatePath}' resolves outside the installation root '{installRoot}'.");
        }
    }

    private static void TryDeleteEmptyParents(string parentPath, string installRoot)
    {
        var fullRoot = Path.GetFullPath(installRoot);
        var current = new DirectoryInfo(parentPath);
        while (current is not null && !PathsEqual(current.FullName, fullRoot))
        {
            if (current.EnumerateFileSystemInfos().Any())
            {
                break;
            }

            var nextParentPath = current.Parent?.FullName;
            current.Delete();
            current = nextParentPath is null ? null : new DirectoryInfo(nextParentPath);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool PathExists(string path) => Directory.Exists(path) || File.Exists(path);

    private static void DeletePath(string path)
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
}
