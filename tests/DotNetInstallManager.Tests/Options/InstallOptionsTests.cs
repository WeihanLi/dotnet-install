using DotNetInstallManager.Options;

namespace DotNetInstallManager.Tests.Options;

public sealed class InstallOptionsTests
{
    private static InstallOptions CreateOptions(
        string channel = "LTS",
        string? quality = null,
        string version = "Latest",
        bool @internal = false,
        FileInfo? globalJsonFile = null,
        string installDir = "<auto>",
        string architecture = "<auto>",
        string? runtime = null,
        bool sharedRuntime = false,
        bool dryRun = false,
        bool yes = false,
        bool noPath = false,
        bool persistPath = false,
        string? azureFeed = null,
        string? uncachedFeed = null,
        string? feedCredential = null,
        string? proxyAddress = null,
        bool proxyUseDefaultCredentials = false,
        IReadOnlyList<string>? proxyBypassList = null,
        bool overrideNonVersionedFiles = true,
        bool keepZip = false,
        FileInfo? zipPath = null,
        bool verbose = false,
        string? runtimeId = null,
        string? userProvidedOs = null,
        int downloadTimeoutSeconds = 1200) =>
        new(channel, quality, version, @internal, globalJsonFile, installDir,
            architecture, runtime, sharedRuntime, dryRun, yes, noPath, persistPath, azureFeed,
            uncachedFeed, feedCredential, proxyAddress, proxyUseDefaultCredentials,
            proxyBypassList ?? Array.Empty<string>(), overrideNonVersionedFiles,
            keepZip, zipPath, verbose, runtimeId, userProvidedOs, downloadTimeoutSeconds);

    [Fact]
    public void RequestsRuntimeOnly_ReturnsFalse_WhenNoRuntimeFlagsSet()
    {
        var options = CreateOptions(runtime: null, sharedRuntime: false);
        Assert.False(options.RequestsRuntimeOnly);
    }

    [Fact]
    public void RequestsRuntimeOnly_ReturnsTrue_WhenSharedRuntimeIsSet()
    {
        var options = CreateOptions(sharedRuntime: true);
        Assert.True(options.RequestsRuntimeOnly);
    }

    [Fact]
    public void RequestsRuntimeOnly_ReturnsTrue_WhenRuntimeIsSpecified()
    {
        var options = CreateOptions(runtime: "dotnet");
        Assert.True(options.RequestsRuntimeOnly);
    }

    [Fact]
    public void RequestsRuntimeOnly_ReturnsTrue_WhenRuntimeIsAspNetCore()
    {
        var options = CreateOptions(runtime: "aspnetcore");
        Assert.True(options.RequestsRuntimeOnly);
    }

    [Fact]
    public void EffectiveRuntime_ReturnsDotnet_WhenSharedRuntimeTrueAndRuntimeNull()
    {
        var options = CreateOptions(sharedRuntime: true, runtime: null);
        Assert.Equal("dotnet", options.EffectiveRuntime);
    }

    [Fact]
    public void EffectiveRuntime_ReturnsRuntime_WhenRuntimeExplicitlySet()
    {
        var options = CreateOptions(runtime: "aspnetcore");
        Assert.Equal("aspnetcore", options.EffectiveRuntime);
    }

    [Fact]
    public void EffectiveRuntime_ReturnsEmpty_WhenNoRuntimeRequested()
    {
        var options = CreateOptions(runtime: null, sharedRuntime: false);
        Assert.Equal(string.Empty, options.EffectiveRuntime);
    }

    [Fact]
    public void EffectiveRuntime_PrefersExplicitRuntime_OverSharedRuntimeFlag()
    {
        var options = CreateOptions(sharedRuntime: true, runtime: "aspnetcore");
        Assert.Equal("aspnetcore", options.EffectiveRuntime);
    }

    [Theory]
    [InlineData(1200, 1200)]
    [InlineData(30, 30)]
    [InlineData(3600, 3600)]
    public void DownloadTimeout_ConvertsSecondsToTimeSpan(int seconds, int expectedSeconds)
    {
        var options = CreateOptions(downloadTimeoutSeconds: seconds);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.DownloadTimeout);
    }
}
