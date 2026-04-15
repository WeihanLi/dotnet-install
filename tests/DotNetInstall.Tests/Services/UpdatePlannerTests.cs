using System.Runtime.InteropServices;
using DotNetInstall.Options;
using DotNetInstall.Services;

namespace DotNetInstall.Tests.Services;

public sealed class UpdatePlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnet-install-update-tests", Guid.NewGuid().ToString("N"));

    public UpdatePlannerTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task BuildAsync_UsesLatestSdkInChannel_AndMarksOlderVersionsObsolete()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sdk", "10.0.201"));

        var client = CreateSdkClient(
            CreateSdkRelease("10.0.5", "2026-03-10", "10.0.300"),
            CreateSdkRelease("10.0.4", "2026-02-10", "10.0.201"));

        var plan = await UpdatePlanner.BuildAsync(
            new UpdateOptions(["10.0.x"], Runtime: false, SdkOnly: false, DryRun: false, InstallDir: _root),
            client,
            CancellationToken.None);

        Assert.Equal(InstallProductKind.Sdk, plan.ProductKind);
        Assert.Equal("10.0.300", plan.ResolvedVersion);
        Assert.True(plan.InstallRequired);
        Assert.Equal(["10.0.201"], plan.InstalledVersions);
        Assert.Equal(["10.0.201"], plan.ObsoleteVersions);
    }

    [Fact]
    public async Task BuildAsync_SkipsInstall_WhenResolvedSdkIsAlreadyInstalled()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sdk", "10.0.201"));
        Directory.CreateDirectory(Path.Combine(_root, "sdk", "10.0.300"));

        var client = CreateSdkClient(
            CreateSdkRelease("10.0.5", "2026-03-10", "10.0.300"),
            CreateSdkRelease("10.0.4", "2026-02-10", "10.0.201"));

        var plan = await UpdatePlanner.BuildAsync(
            new UpdateOptions(["10.0.x"], Runtime: false, SdkOnly: false, DryRun: false, InstallDir: _root),
            client,
            CancellationToken.None);

        Assert.False(plan.InstallRequired);
        Assert.Equal(["10.0.201", "10.0.300"], plan.InstalledVersions);
        Assert.Equal(["10.0.201"], plan.ObsoleteVersions);
    }

    [Fact]
    public async Task BuildAsync_UsesExactSdkVersion_AsResolvedTarget()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sdk", "10.0.300"));

        var client = CreateSdkClient(
            CreateSdkRelease("10.0.5", "2026-03-10", "10.0.300"),
            CreateSdkRelease("10.0.4", "2026-02-10", "10.0.201"));

        var plan = await UpdatePlanner.BuildAsync(
            new UpdateOptions(["10.0.201"], Runtime: false, SdkOnly: false, DryRun: false, InstallDir: _root),
            client,
            CancellationToken.None);

        Assert.Equal("10.0.201", plan.ResolvedVersion);
        Assert.True(plan.InstallRequired);
        Assert.Equal(["10.0.300"], plan.ObsoleteVersions);
    }

    [Fact]
    public async Task BuildAsync_UsesRuntimeVersions_WhenRuntimeOptionIsSet()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared", "Microsoft.NETCore.App", "10.0.4"));

        var client = CreateSdkClient(
            CreateSdkRelease("10.0.5", "2026-03-10", "10.0.300"),
            CreateSdkRelease("10.0.4", "2026-02-10", "10.0.201"));

        var plan = await UpdatePlanner.BuildAsync(
            new UpdateOptions(["10.0.x"], Runtime: true, SdkOnly: false, DryRun: false, InstallDir: _root),
            client,
            CancellationToken.None);

        Assert.Equal(InstallProductKind.Runtime, plan.ProductKind);
        Assert.Equal("10.0.5", plan.ResolvedVersion);
        Assert.True(plan.InstallRequired);
        Assert.Equal(["10.0.4"], plan.InstalledVersions);
        Assert.Equal(["10.0.4"], plan.ObsoleteVersions);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static FakeReleaseMetadataClient CreateSdkClient(params ReleaseEntry[] releases) =>
        new(
            new ReleaseIndexDocument(
            [
                new ReleaseIndexEntry(
                    ChannelVersion: "10.0",
                    Product: ".NET",
                    ReleaseType: "lts",
                    SupportPhase: "active",
                    ReleasesJsonUrl: "https://fake.test/10.0/releases.json",
                    LatestRelease: releases[0].ReleaseVersion,
                    LatestSdk: releases[0].Sdk?.Version ?? string.Empty,
                    LatestRuntime: releases[0].Runtime?.Version ?? string.Empty)
            ]),
            new Dictionary<string, ReleaseDocument>
            {
                ["https://fake.test/10.0/releases.json"] = new ReleaseDocument(
                    ChannelVersion: "10.0",
                    ReleaseType: "lts",
                    SupportPhase: "active",
                    Releases: releases)
            });

    private static ReleaseEntry CreateSdkRelease(string releaseVersion, string releaseDate, string sdkVersion)
    {
        var rid = RuntimeInformation.RuntimeIdentifier;

        var sdkFile = new ReleaseFile(
            Name: $"dotnet-sdk-{sdkVersion}-{rid}.zip",
            Rid: rid,
            Url: $"https://builds.dotnet.microsoft.com/dotnet/Sdk/{sdkVersion}/dotnet-sdk-{sdkVersion}-{rid}.zip",
            Hash: null);
        var runtimeFile = new ReleaseFile(
            Name: $"dotnet-runtime-{releaseVersion}-{rid}.zip",
            Rid: rid,
            Url: $"https://builds.dotnet.microsoft.com/dotnet/Runtime/{releaseVersion}/dotnet-runtime-{releaseVersion}-{rid}.zip",
            Hash: null);

        var sdk = new ReleaseProduct(Version: sdkVersion, VersionDisplay: sdkVersion, Files: [sdkFile]);
        var runtime = new ReleaseProduct(Version: releaseVersion, VersionDisplay: releaseVersion, Files: [runtimeFile]);

        return new ReleaseEntry(
            ReleaseVersion: releaseVersion,
            ReleaseDate: releaseDate,
            Sdk: sdk,
            Sdks: [sdk],
            Runtime: runtime,
            AspNetCoreRuntime: null,
            WindowsDesktopRuntime: null);
    }

    private static string GetCurrentRid()
    {
        if (OperatingSystem.IsWindows())
        {
            return "win-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "osx-x64";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux-x64";
        }

        throw new InvalidOperationException("Unsupported test host operating system.");
    }
}
