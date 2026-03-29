using DotNetInstallManager.Services;

namespace DotNetInstallManager.Tests.Services;

public sealed class InstallRemoverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnet-install-tests", Guid.NewGuid().ToString("N"));

    public InstallRemoverTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void RemoveVersion_RemovesSdkAndRuntimeDirectories_ForExactVersion()
    {
        CreateVersionDirectory("sdk", "8.0.205");
        CreateVersionDirectory(Path.Combine("host", "fxr"), "8.0.205");
        CreateVersionDirectory(Path.Combine("shared", "Microsoft.NETCore.App"), "8.0.205");
        CreateVersionDirectory(Path.Combine("shared", "Microsoft.AspNetCore.App"), "8.0.205");
        CreateVersionDirectory(Path.Combine("shared", "Microsoft.WindowsDesktop.App"), "8.0.205");
        CreateVersionDirectory("sdk", "9.0.100");

        var remover = new InstallRemover(TextWriter.Null, verbose: false);

        var result = remover.RemoveVersion(_root, "8.0.205", sdkOnly: false, dryRun: false, CancellationToken.None);

        Assert.Equal(5, result.MatchedPaths.Count);
        Assert.False(Directory.Exists(Path.Combine(_root, "sdk", "8.0.205")));
        Assert.False(Directory.Exists(Path.Combine(_root, "host", "fxr", "8.0.205")));
        Assert.False(Directory.Exists(Path.Combine(_root, "shared", "Microsoft.NETCore.App", "8.0.205")));
        Assert.True(Directory.Exists(Path.Combine(_root, "sdk", "9.0.100")));
    }

    [Fact]
    public void RemoveVersion_WithSdkOnly_RemovesOnlySdkDirectory()
    {
        CreateVersionDirectory("sdk", "8.0.205");
        CreateVersionDirectory(Path.Combine("shared", "Microsoft.NETCore.App"), "8.0.205");

        var remover = new InstallRemover(TextWriter.Null, verbose: false);

        var result = remover.RemoveVersion(_root, "8.0.205", sdkOnly: true, dryRun: false, CancellationToken.None);

        Assert.Single(result.MatchedPaths);
        Assert.False(Directory.Exists(Path.Combine(_root, "sdk", "8.0.205")));
        Assert.True(Directory.Exists(Path.Combine(_root, "shared", "Microsoft.NETCore.App", "8.0.205")));
    }

    [Fact]
    public void RemoveVersion_WithDryRun_ListsMatchesWithoutDeleting()
    {
        CreateVersionDirectory("sdk", "8.0.205");
        CreateVersionDirectory(Path.Combine("shared", "Microsoft.NETCore.App"), "8.0.205");
        var output = new StringWriter();
        var remover = new InstallRemover(output, verbose: false);

        var result = remover.RemoveVersion(_root, "8.0.205", sdkOnly: false, dryRun: true, CancellationToken.None);

        Assert.True(result.DryRun);
        Assert.Equal(2, result.MatchedPaths.Count);
        Assert.True(Directory.Exists(Path.Combine(_root, "sdk", "8.0.205")));
        Assert.True(Directory.Exists(Path.Combine(_root, "shared", "Microsoft.NETCore.App", "8.0.205")));
        Assert.Contains("Would remove", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveVersion_Throws_WhenVersionDoesNotExist()
    {
        CreateVersionDirectory("sdk", "9.0.100");
        var remover = new InstallRemover(TextWriter.Null, verbose: false);

        var exception = Assert.Throws<InstallException>(() =>
            remover.RemoveVersion(_root, "8.0.205", sdkOnly: false, dryRun: false, CancellationToken.None));

        Assert.Contains("No SDK or runtime directories matching version '8.0.205' were found", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void CreateVersionDirectory(string relativeRoot, string version)
    {
        var path = Path.Combine(_root, relativeRoot, version);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "marker.txt"), $"{relativeRoot}:{version}");
    }
}
