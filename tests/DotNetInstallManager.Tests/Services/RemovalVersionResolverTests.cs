using DotNetInstallManager.Services;

namespace DotNetInstallManager.Tests.Services;

public sealed class RemovalVersionResolverTests
{
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

        var plan = await resolver.ResolveAsync(sdkVersion, sdkOnly: false, client, CancellationToken.None);

        Assert.Equal(runtimeVersion, plan.RuntimeVersion);
        Assert.Equal("8.0.200", plan.WorkloadFeatureBand);
        Assert.Equal("8.0.100", plan.SdkManifestBand);
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
    public async Task ResolveAsync_FallsBackToExactVersion_WhenSdkMappingIsUnavailable()
    {
        var client = FakeReleaseMetadataClient.CreateSimple(
            "8.0",
            "lts",
            FakeReleaseMetadataClient.CreateSdkReleaseDocument("8.0", "8.0.5", "8.0.205", "8.0.5"));
        var resolver = new RemovalVersionResolver();

        var plan = await resolver.ResolveAsync("9.0.100", sdkOnly: false, client, CancellationToken.None);

        Assert.Equal("9.0.100", plan.RuntimeVersion);
        Assert.Equal("9.0.100", plan.WorkloadFeatureBand);
        Assert.Equal("9.0.100", plan.SdkManifestBand);
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == "sdk" && target.MatchValue == "9.0.100");
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == Path.Combine("host", "fxr") && target.MatchValue == "9.0.100");
        Assert.Contains(plan.Targets, target => target.Kind == RemovalTargetKind.Directory && target.RelativeRoot == Path.Combine("shared", "Microsoft.NETCore.App") && target.MatchValue == "9.0.100");
    }
}
