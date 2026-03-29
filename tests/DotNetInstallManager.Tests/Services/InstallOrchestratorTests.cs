using DotNetInstallManager.Services;

namespace DotNetInstallManager.Tests.Services;

public sealed class InstallOrchestratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnet-install-tests", Guid.NewGuid().ToString("N"));

    public InstallOrchestratorTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task FilterSharedTargetsAsync_DoesNotFilterRuntimeRemovalRequests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sdk", "10.0.300"));
        Directory.CreateDirectory(Path.Combine(_root, "shared", "Microsoft.NETCore.App", "10.0.4"));

        var requestedPlan = new RemovalPlan(
            RequestedVersion: "10.0.4",
            RequestedKind: RemovalRequestKind.Runtime,
            SdkOnly: false,
            RuntimeVersion: "10.0.4",
            AspNetCoreRuntimeVersion: null,
            WindowsDesktopRuntimeVersion: null,
            WorkloadFeatureBand: null,
            SdkManifestBand: null,
            WarningMessage: null,
            Targets:
            [
                new RemovalTarget(RemovalTargetKind.Directory, Path.Combine("shared", "Microsoft.NETCore.App"), "10.0.4")
            ]);

        var resolver = new RemovalVersionResolver();
        var client = FakeReleaseMetadataClient.CreateSimple(
            "10.0",
            "sts",
            FakeReleaseMetadataClient.CreateSdkReleaseDocument("10.0", "10.0.4", "10.0.300", "10.0.4"));

        var filteredPlan = await InstallOrchestrator.FilterSharedTargetsAsync(
            requestedPlan,
            _root,
            resolver,
            client,
            CancellationToken.None);

        Assert.Single(filteredPlan.Targets);
        Assert.Equal(requestedPlan.Targets[0], filteredPlan.Targets[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
