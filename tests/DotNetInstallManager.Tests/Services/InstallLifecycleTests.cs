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
        if (OperatingSystem.IsMacOS())
        {
            // Skip on macOS where the system dotnet shim is always present on PATH and would interfere with the test
            // build error example: https://github.com/WeihanLi/dotnet-install/actions/runs/23827004845/job/69452175751
            return;
        }

        Environment.SetEnvironmentVariable("DOTNET_INSTALL_DIR", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("DOTNET_ROOT", null, EnvironmentVariableTarget.Process);

        var dotnetRoot = Path.Combine(_root, "dotnet-root");
        Directory.CreateDirectory(dotnetRoot);

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(dotnetRoot, "dotnet.exe"), string.Empty);
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
    public void TryResolveDotnetPath_UsesQuotedPathEntry_WhenExecutableExists()
    {
        var dotnetRoot = Path.Combine(_root, "quoted-dotnet");
        var executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var expected = Path.Combine(dotnetRoot, executableName);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var resolved = InstallEnvironment.TryResolveDotnetPath(
            name => name == "PATH" ? $"\"{dotnetRoot}\"" : null,
            OperatingSystem.IsWindows(),
            path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(dotnetRoot), comparison),
            path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected), comparison),
            path => path);

        Assert.Equal(expected, resolved, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    [Fact]
    public void TryResolveDotnetPath_SkipsInvalidAndMissingEntries()
    {
        var dotnetRoot = Path.Combine(_root, "valid-dotnet");
        var missingRoot = Path.Combine(_root, "missing-dotnet");
        var invalidEntry = $"bad{Path.GetInvalidPathChars()[0]}path";
        var executableName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var expected = Path.Combine(dotnetRoot, executableName);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var resolved = InstallEnvironment.TryResolveDotnetPath(
            name => name == "PATH"
                ? string.Join(Path.PathSeparator, [invalidEntry, missingRoot, dotnetRoot])
                : null,
            OperatingSystem.IsWindows(),
            path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(dotnetRoot), comparison),
            path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected), comparison),
            path => path);

        Assert.Equal(expected, resolved, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    [Fact]
    public void TryResolveDotnetPath_ReturnsNull_WhenPathDoesNotContainDotnet()
    {
        var missingRoot = Path.Combine(_root, "missing-dotnet");

        var resolved = InstallEnvironment.TryResolveDotnetPath(
            name => name == "PATH" ? missingRoot : null,
            OperatingSystem.IsWindows(),
            _ => false,
            _ => false,
            path => path);

        Assert.Null(resolved);
    }

    [Fact]
    public void ConfigurePath_PrependsInstallRoot_OnlyOnce()
    {
        var installRoot = Path.Combine(_root, "dotnet");
        var existingPath = Path.Combine(_root, "existing");
        Environment.SetEnvironmentVariable("PATH", existingPath, EnvironmentVariableTarget.Process);
        var output = new StringWriter();

        InstallEnvironment.ConfigurePath(installRoot, noPath: false, persistPath: false, verbose: false, shouldUpdatePath: true, output);
        InstallEnvironment.ConfigurePath(installRoot, noPath: false, persistPath: false, verbose: true, shouldUpdatePath: true, output);

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
        var existingPath = Path.Combine(_root, "existing");
        Environment.SetEnvironmentVariable("PATH", existingPath, EnvironmentVariableTarget.Process);
        var output = new StringWriter();

        InstallEnvironment.ConfigurePath(installRoot, noPath: true, persistPath: false, verbose: false, shouldUpdatePath: true, output);

        Assert.Equal(existingPath, Environment.GetEnvironmentVariable("PATH"));
        Assert.Contains(Path.GetFullPath(installRoot), output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurePath_WithPersistPath_PrependsProcessAndUserPaths_OnlyOnce()
    {
        var installRoot = Path.Combine(_root, "dotnet");
        var store = new Dictionary<EnvironmentVariableTarget, string?>();
        var output = new StringWriter();

        static string? GetEnvironmentVariable(string name, EnvironmentVariableTarget target, Dictionary<EnvironmentVariableTarget, string?> store) =>
            name == "PATH" && store.TryGetValue(target, out var value) ? value : null;

        static void SetEnvironmentVariable(string name, string? value, EnvironmentVariableTarget target, Dictionary<EnvironmentVariableTarget, string?> store)
        {
            if (name == "PATH")
            {
                store[target] = value;
            }
        }

        var processPath = Path.Combine(_root, "existing-process");
        var userPath = Path.Combine(_root, "existing-user");
        store[EnvironmentVariableTarget.Process] = processPath;
        store[EnvironmentVariableTarget.User] = userPath;

        InstallEnvironment.ConfigurePath(
            installRoot,
            noPath: false,
            persistPath: true,
            verbose: false,
            shouldUpdatePath: true,
            output,
            (name, target) => GetEnvironmentVariable(name, target, store),
            (name, value, target) => SetEnvironmentVariable(name, value, target, store),
            isWindows: true);

        InstallEnvironment.ConfigurePath(
            installRoot,
            noPath: false,
            persistPath: true,
            verbose: true,
            shouldUpdatePath: true,
            output,
            (name, target) => GetEnvironmentVariable(name, target, store),
            (name, value, target) => SetEnvironmentVariable(name, value, target, store),
            isWindows: true);

        var expectedPath = Path.GetFullPath(installRoot);
        Assert.StartsWith(expectedPath + Path.PathSeparator, store[EnvironmentVariableTarget.Process], StringComparison.Ordinal);
        Assert.StartsWith(expectedPath + Path.PathSeparator, store[EnvironmentVariableTarget.User], StringComparison.Ordinal);
        Assert.Equal(1, store[EnvironmentVariableTarget.Process]!.Split(Path.PathSeparator).Count(segment =>
            string.Equals(segment, expectedPath, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, store[EnvironmentVariableTarget.User]!.Split(Path.PathSeparator).Count(segment =>
            string.Equals(segment, expectedPath, StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("Current process PATH already contains", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("User PATH already contains", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurePath_WithPersistPath_ThrowsOnNonWindows()
    {
        var installRoot = Path.Combine(_root, "dotnet");

        var exception = Assert.Throws<InstallException>(() => InstallEnvironment.ConfigurePath(
            installRoot,
            noPath: false,
            persistPath: true,
            verbose: false,
            shouldUpdatePath: true,
            TextWriter.Null,
            (_, _) => null,
            (_, _, _) => { },
            isWindows: false));

        Assert.Contains("--persist-path is only supported on Windows", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurePath_SkipsPathMutation_WhenExistingDotNetInstallationWasDetected()
    {
        var installRoot = Path.Combine(_root, "dotnet");
        var store = new Dictionary<EnvironmentVariableTarget, string?>
        {
            [EnvironmentVariableTarget.Process] = "C:\\existing",
            [EnvironmentVariableTarget.User] = "C:\\user-existing"
        };
        var output = new StringWriter();

        InstallEnvironment.ConfigurePath(
            installRoot,
            noPath: false,
            persistPath: true,
            verbose: false,
            shouldUpdatePath: false,
            output,
            (name, target) => name == "PATH" && store.TryGetValue(target, out var value) ? value : null,
            (name, value, target) =>
            {
                if (name == "PATH")
                {
                    store[target] = value;
                }
            },
            isWindows: true);

        Assert.Equal("C:\\existing", store[EnvironmentVariableTarget.Process]);
        Assert.Equal("C:\\user-existing", store[EnvironmentVariableTarget.User]);
        Assert.Contains("Skipping PATH update because an existing .NET installation was detected.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldUpdatePathForInstall_ReturnsFalse_WhenDotNetAlreadyExistsOnPath()
    {
        var installRoot = Path.Combine(_root, "new-install");
        var existingRoot = Path.Combine(_root, "existing-dotnet");

        var result = InstallEnvironment.ShouldUpdatePathForInstall(
            installRoot,
            () => existingRoot,
            _ => null,
            isWindows: true,
            directoryExists: path => string.Equals(path, Path.Combine(existingRoot, "sdk"), StringComparison.OrdinalIgnoreCase),
            fileExists: path => string.Equals(path, Path.Combine(existingRoot, "dotnet.exe"), StringComparison.OrdinalIgnoreCase));

        Assert.False(result);
    }

    [Fact]
    public void ShouldUpdatePathForInstall_ReturnsTrue_WhenNoExistingDotNetInstallationIsFound()
    {
        var installRoot = Path.Combine(_root, "first-install");

        var result = InstallEnvironment.ShouldUpdatePathForInstall(
            installRoot,
            () => null,
            _ => null,
            isWindows: true,
            directoryExists: _ => false,
            fileExists: _ => false);

        Assert.True(result);
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
