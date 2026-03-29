using DotNetInstallManager.Services;

namespace DotNetInstallManager.Tests.Services;

public sealed class RemovalVersionResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnet-install-tests", Guid.NewGuid().ToString("N"));

    public RemovalVersionResolverTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ResolveAsync_ForSdkVersion_IncludesMappedRuntimeVersions()
    {
        const string sdkVersion = "8.0.205";
        const string runtimeVersion = "8.0.5";
        const string aspNetVersion = "8.0.6";
        const string windowsDesktopVersion = "8.0.7";

        var sdkFile = new ReleaseFile("dotnet-sdk-8.0.205-win-x64.zip", "win-x64", "https://example.invalid/sdk.zip", null);
        var runtimeFile = new ReleaseFile("dotnet-runtime-8.0.5-win-x64.zip", "win-x64", "https://example.invalid/runtime.zip", null);
        var aspNetFile = new ReleaseFile("aspnetcore-runtime-8.0.6-win-x64.zip", "win-x64", "https://example.invalid/aspnet.zip", null);
        var windowsDesktopFile = new ReleaseFile("windowsdesktop-runtime-8.0.7-win-x64.zip", "win-x64", "https://example.invalid/windowsdesktop.zip", null);

        var document = new ReleaseDocument(
            ChannelVersion: "8.0",
            ReleaseType: "lts",
            SupportPhase: "active",
            Releases:
            [
                new ReleaseEntry(
                    ReleaseVersion: runtimeVersion,
                    ReleaseDate: "2024-01-01",
                    Sdk: new ReleaseProduct(sdkVersion, sdkVersion, [sdkFile]),
                    Sdks: [new ReleaseProduct(sdkVersion, sdkVersion, [sdkFile])],
                    Runtime: new ReleaseProduct(runtimeVersion, runtimeVersion, [runtimeFile]),
                    AspNetCoreRuntime: new ReleaseProduct(aspNetVersion, aspNetVersion, [aspNetFile]),
                    WindowsDesktopRuntime: new ReleaseProduct(windowsDesktopVersion, windowsDesktopVersion, [windowsDesktopFile]))
            ]);

        var client = FakeReleaseMetadataClient.CreateSimple("8.0", "lts", document);
        var resolver = new RemovalVersionResolver();

        var plan = await resolver.ResolveAsync(sdkVersion, sdkOnly: false, _root, client, CancellationToken.None);

        Assert.Equal(RemovalRequestKind.Sdk, plan.RequestedKind);
        Assert.Equal(runtimeVersion, plan.RuntimeVersion);
        Assert.Equal("8.0.200", plan.WorkloadFeatureBand);
        Assert.Equal("8.0.100", plan.SdkManifestBand);
        Assert.Null(plan.WarningMessage);
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == "sdk" && target.MatchValue == sdkVersion);
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == Path.Combine("host", "fxr") && target.MatchValue == runtimeVersion);
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == "templates" && target.MatchValue == runtimeVersion);
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == Path.Combine("packs", "Microsoft.NETCore.App.Ref") && target.MatchValue == runtimeVersion);
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == Path.Combine("metadata", "workloads") && target.MatchValue == "8.0.200");
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == "sdk-manifests" && target.MatchValue == "8.0.100");
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.DirectoryPattern && target.RelativeRoot == Path.Combine("packs", "Microsoft.NETCore.App.Host.*") && target.MatchValue == runtimeVersion);
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.FilePattern && target.RelativeRoot == "swidtag" && target.MatchValue == $"*{sdkVersion}*.swidtag");
    }

    [Fact]
    public async Task ResolveAsync_ForRuntimeVersion_PlansRuntimeTargetsOnly()
    {
        var client = FakeReleaseMetadataClient.CreateSimple(
            "8.0",
            "lts",
            FakeReleaseMetadataClient.CreateSdkReleaseDocument("8.0", "8.0.5", "8.0.205", "8.0.5"));
        var resolver = new RemovalVersionResolver();

        var plan = await resolver.ResolveAsync("8.0.5", sdkOnly: false, _root, client, CancellationToken.None);

        Assert.Equal(RemovalRequestKind.Runtime, plan.RequestedKind);
        Assert.Equal("8.0.5", plan.RuntimeVersion);
        Assert.Null(plan.WorkloadFeatureBand);
        Assert.Null(plan.SdkManifestBand);
        Assert.Null(plan.WarningMessage);
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == "sdk");
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == Path.Combine("metadata", "workloads"));
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == "sdk-manifests");
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == Path.Combine("host", "fxr") && target.MatchValue == "8.0.5");
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == Path.Combine("shared", "Microsoft.NETCore.App") && target.MatchValue == "8.0.5");
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == "templates" && target.MatchValue == "8.0.5");
    }

    [Fact]
    public async Task ResolveAsync_ForSdkVersion_WithoutRuntimeMapping_ReturnsSdkTargetsAndWarning()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sdk", "9.0.100"));
        var client = FakeReleaseMetadataClient.CreateSimple(
            "8.0",
            "lts",
            FakeReleaseMetadataClient.CreateSdkReleaseDocument("8.0", "8.0.5", "8.0.205", "8.0.5"));
        var resolver = new RemovalVersionResolver();

        var plan = await resolver.ResolveAsync("9.0.100", sdkOnly: false, _root, client, CancellationToken.None);

        Assert.Equal(RemovalRequestKind.Sdk, plan.RequestedKind);
        Assert.Null(plan.RuntimeVersion);
        Assert.Null(plan.AspNetCoreRuntimeVersion);
        Assert.Null(plan.WindowsDesktopRuntimeVersion);
        Assert.Null(plan.WorkloadFeatureBand);
        Assert.Null(plan.SdkManifestBand);
        Assert.NotNull(plan.WarningMessage);
        Assert.Contains("Remove the matching runtime version separately", plan.WarningMessage, StringComparison.Ordinal);
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == "sdk" && target.MatchValue == "9.0.100");
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.FilePattern && target.RelativeRoot == "swidtag" && target.MatchValue == "*9.0.100*.swidtag");
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == Path.Combine("host", "fxr"));
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == Path.Combine("shared", "Microsoft.NETCore.App"));
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == Path.Combine("shared", "Microsoft.AspNetCore.App"));
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == Path.Combine("shared", "Microsoft.WindowsDesktop.App"));
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == "templates");
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == Path.Combine("packs", "Microsoft.NETCore.App.Ref"));
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == Path.Combine("packs", "Microsoft.AspNetCore.App.Ref"));
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == Path.Combine("packs", "Microsoft.WindowsDesktop.App.Ref"));
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == Path.Combine("packs", "Microsoft.NETCore.App.Host.*"));
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == Path.Combine("metadata", "workloads"));
        Assert.DoesNotContain(plan.Targets, target => target.RelativeRoot == "sdk-manifests");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
