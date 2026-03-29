using DotNetInstallManager.Options;
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

    [Fact]
    public async Task ExecuteRemovalAsync_RetriesWholeCommandWithElevation_OnWindowsAccessFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(_root, "sdk", "8.0.205"));

        var elevationManager = new FakeRemovalElevationManager(canRetryAsAdministrator: true, elevatedExitCode: 0);
        var orchestrator = new InstallOrchestrator(
            elevationManager,
            (stdout, verbose) => new InstallRemover(
                stdout,
                verbose,
                _ => throw new UnauthorizedAccessException("Access denied.")));

        var exitCode = await orchestrator.ExecuteRemovalAsync(
            new RemoveOptions("8.0.205", _root, SdkOnly: true, DryRun: false, Verbose: false),
            TextWriter.Null,
            TextWriter.Null,
            CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(elevationManager.TryRunCalled);
    }

    [Fact]
    public async Task ExecuteRemovalAsync_ReturnsError_WhenElevationRelaunchFails()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Directory.CreateDirectory(Path.Combine(_root, "sdk", "8.0.206"));
        var output = new StringWriter();

        var elevationManager = new FakeRemovalElevationManager(canRetryAsAdministrator: true, elevatedExitCode: 1, failureReason: "Elevation prompt canceled.", canLaunch: false);
        var orchestrator = new InstallOrchestrator(
            elevationManager,
            (stdout, verbose) => new InstallRemover(
                stdout,
                verbose,
                _ => throw new UnauthorizedAccessException("Access denied.")));

        var exitCode = await orchestrator.ExecuteRemovalAsync(
            new RemoveOptions("8.0.206", _root, SdkOnly: true, DryRun: false, Verbose: false),
            TextWriter.Null,
            output,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.True(elevationManager.TryRunCalled);
        Assert.Contains("Failed to relaunch removal with administrator privileges", output.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeRemovalElevationManager : IRemovalElevationManager
    {
        private readonly int _elevatedExitCode;
        private readonly string? _failureReason;
        private readonly bool _canLaunch;

        public FakeRemovalElevationManager(bool canRetryAsAdministrator, int elevatedExitCode, string? failureReason = null, bool canLaunch = true)
        {
            CanRetryAsAdministrator = canRetryAsAdministrator;
            _elevatedExitCode = elevatedExitCode;
            _failureReason = failureReason;
            _canLaunch = canLaunch;
        }

        public bool CanRetryAsAdministrator { get; }

        public bool TryRunCalled { get; private set; }

        public bool TryRunElevatedRemove(TextWriter standardOut, bool verbose, out int exitCode, out string? failureReason)
        {
            TryRunCalled = true;
            exitCode = _elevatedExitCode;
            failureReason = _failureReason;
            return _canLaunch;
        }
    }
}
