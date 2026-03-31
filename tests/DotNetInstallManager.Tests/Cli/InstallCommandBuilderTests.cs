using System.CommandLine;
using DotNetInstallManager.Cli;
using DotNetInstallManager.Options;
using DotNetInstallManager.Services;

namespace DotNetInstallManager.Tests.Cli;

public sealed class InstallCommandBuilderTests
{
    private readonly FakeInstallOrchestrator _orchestrator = new();

    private RootCommand BuildRoot(Func<string, string?>? getEnvironmentVariable = null) =>
        InstallCommandBuilder.Build(
            _orchestrator,
            CancellationToken.None,
            getEnvironmentVariable ?? (_ => null));

    private static Task<int> InvokeAsync(RootCommand root, string[] args, StringWriter? outputWriter = null)
    {
        var parseResult = root.Parse(args);
        var config = new InvocationConfiguration
        {
            Output = outputWriter ?? TextWriter.Null,
            Error = TextWriter.Null
        };
        return parseResult.InvokeAsync(config, CancellationToken.None);
    }

    [Fact]
    public async Task DefaultInvocation_UsesLtsChannelAndLatestVersion()
    {
        var root = BuildRoot();
        await InvokeAsync(root, []);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal("LTS", opts.Channel);
        Assert.Equal("Latest", opts.Version);
        Assert.False(opts.Yes);
        Assert.False(opts.PersistPath);
    }

    [Fact]
    public async Task DefaultInvocation_SetsYesTrue_WhenCiEnvironmentVariableIsPresent()
    {
        var root = BuildRoot(name => name == "CI" ? "true" : null);
        await InvokeAsync(root, []);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.True(opts.Yes);
    }

    [Fact]
    public async Task VersionOption_SetsInstallVersion()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--version", "8.0.205"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal("8.0.205", opts.Version);
    }

    [Fact]
    public async Task VersionOption_ShortAlias_SetsInstallVersion()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["-v", "9.0.100"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal("9.0.100", opts.Version);
    }

    [Fact]
    public async Task ChannelOption_OverridesDefault()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--channel", "STS"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal("STS", opts.Channel);
    }

    [Fact]
    public async Task ChannelOption_ShortAlias_OverridesDefault()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["-c", "8.0"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal("8.0", opts.Channel);
    }

    [Fact]
    public async Task DryRunFlag_SetsDryRunTrue()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--dry-run"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.True(opts.DryRun);
    }

    [Fact]
    public async Task YesOption_SetsYesTrue()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--yes"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.True(opts.Yes);
    }

    [Fact]
    public async Task YesOption_ShortAlias_SetsYesTrue()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["-y"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.True(opts.Yes);
    }

    [Fact]
    public async Task PersistPathOption_SetsPersistPathTrue()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--persist-path"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.True(opts.PersistPath);
    }

    [Fact]
    public async Task PersistPathAndNoPathCombination_ReturnsParseError()
    {
        var root = BuildRoot();
        var error = new StringWriter();
        var parseResult = root.Parse(["--persist-path", "--no-path"]);
        var exitCode = await parseResult.InvokeAsync(
            new InvocationConfiguration { Output = TextWriter.Null, Error = error },
            CancellationToken.None);

        Assert.NotEqual(0, exitCode);
        Assert.Empty(_orchestrator.InstallCalls);
    }

    [Fact]
    public async Task RuntimeOption_SetsRuntime()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--runtime", "aspnetcore"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal("aspnetcore", opts.Runtime);
        Assert.True(opts.RequestsRuntimeOnly);
    }

    [Fact]
    public async Task SharedRuntimeFlag_SetsSharedRuntimeTrue()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--shared-runtime"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.True(opts.SharedRuntime);
        Assert.True(opts.RequestsRuntimeOnly);
    }

    [Fact]
    public async Task QualityAndVersionCombination_ReturnsParseError()
    {
        var root = BuildRoot();
        var error = new StringWriter();
        var parseResult = root.Parse(["--quality", "preview", "--version", "8.0.205"]);
        var exitCode = await parseResult.InvokeAsync(
            new InvocationConfiguration { Output = TextWriter.Null, Error = error },
            CancellationToken.None);

        Assert.NotEqual(0, exitCode);
        Assert.Empty(_orchestrator.InstallCalls);
    }

    [Fact]
    public async Task QualityWithLatestVersion_Succeeds()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--quality", "preview", "--version", "Latest"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal("preview", opts.Quality);
        Assert.Equal("Latest", opts.Version);
    }

    [Fact]
    public async Task ArchitectureOption_SetsArchitecture()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--architecture", "arm64"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal("arm64", opts.Architecture);
    }

    [Fact]
    public async Task OsOption_SetsUserProvidedOs()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--os", "linux"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal("linux", opts.UserProvidedOs);
    }

    [Fact]
    public async Task RemoveSubcommand_InvokesRemoval()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["remove", "8.0.205"]);

        Assert.Empty(_orchestrator.InstallCalls);
        var removeOpts = Assert.Single(_orchestrator.RemoveCalls);
        Assert.Equal("8.0.205", removeOpts.Version);
    }

    [Fact]
    public async Task RemoveSubcommand_WithInstallDir_SetsInstallDir()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["remove", "8.0.205", "--install-dir", "/opt/dotnet"]);

        var removeOpts = Assert.Single(_orchestrator.RemoveCalls);
        Assert.Equal("/opt/dotnet", removeOpts.InstallDir);
    }

    [Fact]
    public async Task RemoveSubcommand_WithSdkOnlyFlag_SetsSdkOnly()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["remove", "8.0.205", "--sdk-only"]);

        var removeOpts = Assert.Single(_orchestrator.RemoveCalls);
        Assert.True(removeOpts.SdkOnly);
    }

    [Fact]
    public async Task RemoveSubcommand_WithDryRunFlag_SetsDryRun()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["remove", "8.0.205", "--dry-run"]);

        var removeOpts = Assert.Single(_orchestrator.RemoveCalls);
        Assert.True(removeOpts.DryRun);
    }

    [Fact]
    public async Task VersionSubcommand_PrintsToolVersion()
    {
        var root = BuildRoot();
        var output = new StringWriter();
        await InvokeAsync(root, ["version"], output);

        var result = output.ToString().Trim();
        Assert.False(string.IsNullOrWhiteSpace(result), "Version subcommand should print a non-empty version string");
        Assert.Empty(_orchestrator.InstallCalls);
    }

    [Fact]
    public async Task VersionSubcommand_ReturnsExitCodeZero()
    {
        var root = BuildRoot();
        var exitCode = await InvokeAsync(root, ["version"]);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task DownloadTimeoutOption_SetsTimeout()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--download-timeout", "300"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal(300, opts.DownloadTimeoutSeconds);
    }

    [Fact]
    public async Task DefaultDownloadTimeout_Is1200Seconds()
    {
        var root = BuildRoot();
        await InvokeAsync(root, []);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal(1200, opts.DownloadTimeoutSeconds);
    }

    [Fact]
    public async Task VerboseFlag_SetsVerboseTrue()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--verbose"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.True(opts.Verbose);
    }

    [Fact]
    public async Task ProxyAddressOption_SetsProxyAddress()
    {
        var root = BuildRoot();
        await InvokeAsync(root, ["--proxy-address", "http://proxy.example.com:8080"]);

        var opts = Assert.Single(_orchestrator.InstallCalls);
        Assert.Equal("http://proxy.example.com:8080", opts.ProxyAddress);
    }
}
