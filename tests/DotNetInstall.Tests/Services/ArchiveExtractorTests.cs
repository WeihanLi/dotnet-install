using System.Formats.Tar;
using System.IO.Compression;
using DotNetInstall.Services;

namespace DotNetInstall.Tests.Services;

public sealed class ArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnet-install-tests", Guid.NewGuid().ToString("N"));

    public ArchiveExtractorTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Extract_Zip_SkipsExistingVersionedDirectoriesAndNonVersionedFiles_WhenOverwriteDisabled()
    {
        var archivePath = Path.Combine(_root, "sdk.zip");
        CreateZipArchive(
            archivePath,
            ("dotnet.exe", "new-dotnet"),
            ("sdk/8.0.100/new.txt", "skip-me"),
            ("sdk/8.0.200/new.txt", "install-me"));

        var installRoot = Path.Combine(_root, "install");
        Directory.CreateDirectory(Path.Combine(installRoot, "sdk", "8.0.100"));
        File.WriteAllText(Path.Combine(installRoot, "dotnet.exe"), "existing-dotnet");
        File.WriteAllText(Path.Combine(installRoot, "sdk", "8.0.100", "existing.txt"), "existing-sdk");

        var extractor = new ArchiveExtractor(TextWriter.Null, verbose: false);
        extractor.Extract(archivePath, installRoot, overrideNonVersionedFiles: false, CancellationToken.None);

        Assert.Equal("existing-dotnet", File.ReadAllText(Path.Combine(installRoot, "dotnet.exe")));
        Assert.False(File.Exists(Path.Combine(installRoot, "sdk", "8.0.100", "new.txt")));
        Assert.Equal("install-me", File.ReadAllText(Path.Combine(installRoot, "sdk", "8.0.200", "new.txt")));
    }

    [Fact]
    public void Extract_Zip_OverwritesNonVersionedFiles_WhenOverwriteEnabled()
    {
        var archivePath = Path.Combine(_root, "sdk.zip");
        CreateZipArchive(
            archivePath,
            ("dotnet.exe", "new-dotnet"),
            ("host/fxr.txt", "host-data"));

        var installRoot = Path.Combine(_root, "install");
        Directory.CreateDirectory(installRoot);
        File.WriteAllText(Path.Combine(installRoot, "dotnet.exe"), "existing-dotnet");

        var extractor = new ArchiveExtractor(TextWriter.Null, verbose: false);
        extractor.Extract(archivePath, installRoot, overrideNonVersionedFiles: true, CancellationToken.None);

        Assert.Equal("new-dotnet", File.ReadAllText(Path.Combine(installRoot, "dotnet.exe")));
        Assert.Equal("host-data", File.ReadAllText(Path.Combine(installRoot, "host", "fxr.txt")));
    }

    [Fact]
    public void Extract_TarGz_InstallsVersionedAndNonVersionedFiles()
    {
        var archivePath = Path.Combine(_root, "runtime.tar.gz");
        CreateTarGzArchive(
            archivePath,
            ("dotnet", "launcher"),
            ("shared/Microsoft.NETCore.App/8.0.5/runtime.txt", "runtime-data"));

        var installRoot = Path.Combine(_root, "install");
        var extractor = new ArchiveExtractor(TextWriter.Null, verbose: false);

        extractor.Extract(archivePath, installRoot, overrideNonVersionedFiles: true, CancellationToken.None);

        Assert.Equal("launcher", File.ReadAllText(Path.Combine(installRoot, "dotnet")));
        Assert.Equal("runtime-data", File.ReadAllText(Path.Combine(installRoot, "shared", "Microsoft.NETCore.App", "8.0.5", "runtime.txt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void CreateZipArchive(string archivePath, params (string Path, string Content)[] files)
    {
        using var stream = File.Create(archivePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            var entry = archive.CreateEntry(file.Path);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(file.Content);
        }
    }

    private static void CreateTarGzArchive(string archivePath, params (string Path, string Content)[] files)
    {
        var sourceRoot = Path.Combine(Path.GetDirectoryName(archivePath)!, "tar-source");
        Directory.CreateDirectory(sourceRoot);

        foreach (var file in files)
        {
            var fullPath = file.Path
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Aggregate(sourceRoot, Path.Combine);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Content);
        }

        using var fileStream = File.Create(archivePath);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.SmallestSize);
        TarFile.CreateFromDirectory(sourceRoot, gzipStream, includeBaseDirectory: false);

        Directory.Delete(sourceRoot, recursive: true);
    }
}
