using DotNetInstallManager.Services;

namespace DotNetInstallManager.Tests.Services;

public sealed class InstallLifecycleTests : IDisposable
{
    private readonly string? _originalInstallDir = Environment.GetEnvironmentVariable("DOTNET_INSTALL_DIR");
    private readonly string? _originalDotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
    private readonly string? _originalPath = Environment.GetEnvironmentVariable("PATH");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnet-install-tests", Guid.NewGuid().ToString("N"));

    public InstallLifecycleTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ResolveInstallRoot_UsesDotNetInstallDirEnvironmentVariable_WhenAuto()
    {
        var configured = Path.Combine(_root, "configured");
        Environment.SetEnvironmentVariable("DOTNET_INSTALL_DIR", configured, EnvironmentVariableTarget.Process);

        var resolved = InstallEnvironment.ResolveInstallRoot("<auto>");

        Assert.Equal(Path.GetFullPath(configured), resolved);
    }

    [Fact]
    public void ResolveInstallRoot_UsesDotNetLocationFromPath_WhenAuto()
    {
        Environment.SetEnvironmentVariable("DOTNET_INSTALL_DIR", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("DOTNET_ROOT", null, EnvironmentVariableTarget.Process);

        var dotnetRoot = Path.Combine(_root, "dotnet-root");
        Directory.CreateDirectory(dotnetRoot);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(dotnetRoot, "dotnet.cmd"), "@echo off");
        }
        else
        {
            var dotnetPath = Path.Combine(dotnetRoot, "dotnet");
            File.WriteAllText(dotnetPath, "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(
                dotnetPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", string.IsNullOrWhiteSpace(originalPath)
            ? dotnetRoot
            : string.Concat(dotnetRoot, Path.PathSeparator, originalPath), EnvironmentVariableTarget.Process);

        var resolved = InstallEnvironment.ResolveInstallRoot("<auto>");

        Assert.Equal(Path.GetFullPath(dotnetRoot), resolved);
    }

    [Fact]
    public void ConfigurePath_PrependsInstallRoot_OnlyOnce()
    {
        var installRoot = Path.Combine(_root, "dotnet");
        Environment.SetEnvironmentVariable("PATH", "C:\\existing", EnvironmentVariableTarget.Process);
        var output = new StringWriter();

        InstallEnvironment.ConfigurePath(installRoot, noPath: false, verbose: false, output);
        InstallEnvironment.ConfigurePath(installRoot, noPath: false, verbose: true, output);

        var path = Environment.GetEnvironmentVariable("PATH")!;
        var segments = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(Path.GetFullPath(installRoot), segments[0]);
        Assert.Equal(1, segments.Count(segment =>
            string.Equals(segment, Path.GetFullPath(installRoot), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)));
        Assert.Contains("already contains", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurePath_WithNoPath_PrintsLocationWithoutMutatingPath()
    {
        var installRoot = Path.Combine(_root, "dotnet");
        Environment.SetEnvironmentVariable("PATH", "C:\\existing", EnvironmentVariableTarget.Process);
        var output = new StringWriter();

        InstallEnvironment.ConfigurePath(installRoot, noPath: true, verbose: false, output);

        Assert.Equal("C:\\existing", Environment.GetEnvironmentVariable("PATH"));
        Assert.Contains(Path.GetFullPath(installRoot), output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyInstalled_AcceptsSdkFolder()
    {
        var installRoot = Path.Combine(_root, "install");
        Directory.CreateDirectory(Path.Combine(installRoot, "sdk", "8.0.205"));
        var plan = new InstallPlan(
            ChannelVersion: "8.0",
            ReleaseVersion: "8.0.5",
            ProductVersion: "8.0.205",
            ProductKind: InstallProductKind.Sdk,
            TargetRid: "win-x64",
            AssetName: "dotnet-sdk-8.0.205-win-x64.zip",
            SourceUrl: "https://example.invalid/dotnet-sdk-8.0.205-win-x64.zip",
            CandidateUrls: ["https://example.invalid/dotnet-sdk-8.0.205-win-x64.zip"],
            ExpectedHash: null,
            IsPreview: false);

        InstallVerifier.VerifyInstalled(plan, installRoot);
    }

    [Fact]
    public void VerifyInstalled_AcceptsReleaseVersionFallback_ForRtmBuilds()
    {
        var installRoot = Path.Combine(_root, "install");
        Directory.CreateDirectory(Path.Combine(installRoot, "shared", "Microsoft.NETCore.App", "9.0.0"));
        var plan = new InstallPlan(
            ChannelVersion: "9.0",
            ReleaseVersion: "9.0.0",
            ProductVersion: "9.0.0-rtm.12345",
            ProductKind: InstallProductKind.Runtime,
            TargetRid: "linux-x64",
            AssetName: "dotnet-runtime-9.0.0-linux-x64.tar.gz",
            SourceUrl: "https://example.invalid/dotnet-runtime-9.0.0-linux-x64.tar.gz",
            CandidateUrls: ["https://example.invalid/dotnet-runtime-9.0.0-linux-x64.tar.gz"],
            ExpectedHash: null,
            IsPreview: false);

        InstallVerifier.VerifyInstalled(plan, installRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DOTNET_INSTALL_DIR", _originalInstallDir, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("DOTNET_ROOT", _originalDotnetRoot, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("PATH", _originalPath, EnvironmentVariableTarget.Process);

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
