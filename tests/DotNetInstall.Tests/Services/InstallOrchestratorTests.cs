using DotNetInstall.Options;
using DotNetInstall.Services;

namespace DotNetInstall.Tests.Services;

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
                _ => throw new UnauthorizedAccessException("Access denied.")),
            static () => new FakeSelfUpdater(),
            new StringReader("yes"),
            static () => false);

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
                _ => throw new UnauthorizedAccessException("Access denied.")),
            static () => new FakeSelfUpdater(),
            new StringReader("yes"),
            static () => false);

        var exitCode = await orchestrator.ExecuteRemovalAsync(
            new RemoveOptions("8.0.206", _root, SdkOnly: true, DryRun: false, Verbose: false),
            TextWriter.Null,
            output,
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.True(elevationManager.TryRunCalled);
        Assert.Contains("Failed to relaunch removal with administrator privileges", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmExistingInstallAsync_ReturnsFalse_WhenInputIsRedirected()
    {
        var plan = new InstallPlan(
            ChannelVersion: "10.0",
            ReleaseVersion: "10.0.5",
            ProductVersion: "10.0.300",
            ProductKind: InstallProductKind.Sdk,
            TargetRid: "win-x64",
            AssetName: "dotnet-sdk-10.0.300-win-x64.zip",
            SourceUrl: "https://example.test/sdk.zip",
            CandidateUrls: ["https://example.test/sdk.zip"],
            ExpectedHash: null,
            IsPreview: false);

        var result = await InstallOrchestrator.ConfirmExistingInstallAsync(
            plan,
            new ExistingInstallDetectionResult([new ExistingInstallMatch("install root", @"C:\Program Files\dotnet\sdk\10.0.300")]),
            skipConfirmation: false,
            new StringWriter(),
            new StringWriter(),
            new StringReader(string.Empty),
            inputRedirected: true,
            CancellationToken.None);

        Assert.False(result);
    }

    [Theory]
    [InlineData("y")]
    [InlineData("yes")]
    public async Task ConfirmExistingInstallAsync_ReturnsTrue_WhenUserConfirms(string response)
    {
        var plan = new InstallPlan(
            ChannelVersion: "10.0",
            ReleaseVersion: "10.0.5",
            ProductVersion: "10.0.300",
            ProductKind: InstallProductKind.Sdk,
            TargetRid: "win-x64",
            AssetName: "dotnet-sdk-10.0.300-win-x64.zip",
            SourceUrl: "https://example.test/sdk.zip",
            CandidateUrls: ["https://example.test/sdk.zip"],
            ExpectedHash: null,
            IsPreview: false);

        var result = await InstallOrchestrator.ConfirmExistingInstallAsync(
            plan,
            new ExistingInstallDetectionResult([new ExistingInstallMatch("dotnet --list-sdks", @"C:\Program Files\dotnet\sdk")]),
            skipConfirmation: false,
            new StringWriter(),
            new StringWriter(),
            new StringReader(response),
            inputRedirected: false,
            CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ConfirmExistingInstallAsync_ReturnsTrue_WhenYesOptionSpecifiedWithRedirectedInput()
    {
        var plan = new InstallPlan(
            ChannelVersion: "10.0",
            ReleaseVersion: "10.0.5",
            ProductVersion: "10.0.300",
            ProductKind: InstallProductKind.Sdk,
            TargetRid: "win-x64",
            AssetName: "dotnet-sdk-10.0.300-win-x64.zip",
            SourceUrl: "https://example.test/sdk.zip",
            CandidateUrls: ["https://example.test/sdk.zip"],
            ExpectedHash: null,
            IsPreview: false);

        var output = new StringWriter();
        var error = new StringWriter();

        var result = await InstallOrchestrator.ConfirmExistingInstallAsync(
            plan,
            new ExistingInstallDetectionResult([new ExistingInstallMatch("install root", @"C:\Program Files\dotnet\sdk\10.0.300")]),
            skipConfirmation: true,
            output,
            error,
            new StringReader(string.Empty),
            inputRedirected: true,
            CancellationToken.None);

        Assert.True(result);
        Assert.Contains("Continuing installation because --yes was specified.", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void BuildUpdateInstallStatusMessage_ReturnsAlreadyInstalledMessage_WhenInstallNotRequired()
    {
        var plan = new UpdatePlan(
            RequestedVersion: "10.0.x",
            ResolvedVersion: "10.0.201",
            ProductKind: InstallProductKind.Sdk,
            ChannelVersion: "10.0",
            InstallRoot: _root,
            InstallRequired: false,
            InstalledVersions: ["10.0.201"],
            ObsoleteVersions: [],
            InstallPlan: new InstallPlan(
                ChannelVersion: "10.0",
                ReleaseVersion: "10.0.5",
                ProductVersion: "10.0.201",
                ProductKind: InstallProductKind.Sdk,
                TargetRid: "win-x64",
                AssetName: "dotnet-sdk-10.0.201-win-x64.zip",
                SourceUrl: "https://example.test/sdk.zip",
                CandidateUrls: ["https://example.test/sdk.zip"],
                ExpectedHash: null,
                IsPreview: false));

        var message = InstallOrchestrator.BuildUpdateInstallStatusMessage(plan);

        Assert.Equal("Latest requested .NET SDK version '10.0.201' is already installed.", message);
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

    private sealed class FakeSelfUpdater : ISelfUpdater
    {
        public void Dispose()
        {
        }

        public Task<int> ExecuteAsync(
            SelfUpdateOptions options,
            TextWriter standardOut,
            TextWriter standardError,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
