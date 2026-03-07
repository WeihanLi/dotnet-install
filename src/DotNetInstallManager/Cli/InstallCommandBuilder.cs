using System.CommandLine;
using DotNetInstallManager.Options;
using DotNetInstallManager.Services;

namespace DotNetInstallManager.Cli;

internal static class InstallCommandBuilder
{
    internal static RootCommand Build(IInstallOrchestrator orchestrator, CancellationToken externalToken)
    {
        var channelOption = CreateStringOption("--channel", "Download channel (LTS, STS, or specific version train)", "LTS", "-c", "-Channel");
        var qualityOption = CreateNullableStringOption("--quality", "Optional build quality (daily, preview, GA)", "-q", "-Quality");
        var versionOption = CreateStringOption("--version", "Specific version to install", "Latest", "-v", "-Version");
        var internalOption = CreateBoolOption("--internal", "Download internal builds", "-Internal");
        var jsonOption = CreateFileOption("--jsonfile", "Path to global.json that pins the SDK version", "-JsonFile");
        var installDirOption = CreateStringOption("--install-dir", "Installation root", "<auto>", "-i", "-InstallDir");
        var architectureOption = CreateStringOption("--architecture", "Target architecture (x64, arm64, etc.)", "<auto>", "-Architecture", "-arch");
        var runtimeOption = CreateNullableStringOption("--runtime", "Install only a runtime (dotnet, aspnetcore, windowsdesktop)", "-Runtime");
        var sharedRuntimeOption = CreateBoolOption("--shared-runtime", "Obsolete switch that maps to --runtime dotnet", "-SharedRuntime");
        var dryRunOption = CreateBoolOption("--dry-run", "Emit install plan without downloading", "-DryRun");
        var noPathOption = CreateBoolOption("--no-path", "Skip PATH mutation for current process", "-NoPath");
        var azureFeedOption = CreateNullableStringOption("--azure-feed", "Override the default Azure feed", "-AzureFeed");
        var uncachedFeedOption = CreateNullableStringOption("--uncached-feed", "Use an uncached feed", "-UncachedFeed");
        var feedCredentialOption = CreateNullableStringOption("--feed-credential", "Token appended to feed URLs", "-FeedCredential");
        var proxyAddressOption = CreateNullableStringOption("--proxy-address", "Proxy to use for HTTP requests", "-ProxyAddress");
        var proxyDefaultCredsOption = CreateBoolOption("--proxy-use-default-credentials", "Use default credentials for proxy", "-ProxyUseDefaultCredentials");
        var proxyBypassOption = CreateStringArrayOption("--proxy-bypass-list", "Comma separated hosts that bypass the proxy", "-ProxyBypassList");
        var skipNonVersionedOption = CreateBoolOption("--skip-non-versioned-files", "Skip overwriting non-versioned files", "-SkipNonVersionedFiles");
        var keepZipOption = CreateBoolOption("--keep-zip", "Do not delete downloaded archives", "-KeepZip");
        var zipPathOption = CreateFileOption("--zip-path", "Custom path for the downloaded archive", "-ZipPath");
        var verboseOption = CreateBoolOption("--verbose", "Emit verbose diagnostics", "-Verbose");
        var runtimeIdOption = CreateNullableStringOption("--runtime-id", "Legacy runtime identifier override", "-RuntimeId");
        var osOption = CreateNullableStringOption("--os", "Override OS detection (linux, linux-musl, osx, freebsd, rhel.6)", "-OS");
        var downloadTimeoutOption = CreateIntOption("--download-timeout", "HTTP download timeout in seconds", 1200, "-DownloadTimeout");

        var root = new RootCommand("NativeAOT-powered dotnet-install manager that mirrors the shell scripts");
        root.Add(channelOption);
        root.Add(qualityOption);
        root.Add(versionOption);
        root.Add(internalOption);
        root.Add(jsonOption);
        root.Add(installDirOption);
        root.Add(architectureOption);
        root.Add(runtimeOption);
        root.Add(sharedRuntimeOption);
        root.Add(dryRunOption);
        root.Add(noPathOption);
        root.Add(azureFeedOption);
        root.Add(uncachedFeedOption);
        root.Add(feedCredentialOption);
        root.Add(proxyAddressOption);
        root.Add(proxyDefaultCredsOption);
        root.Add(proxyBypassOption);
        root.Add(skipNonVersionedOption);
        root.Add(keepZipOption);
        root.Add(zipPathOption);
        root.Add(verboseOption);
        root.Add(runtimeIdOption);
        root.Add(osOption);
        root.Add(downloadTimeoutOption);
        root.Add(CreateRemoveCommand());

        root.Validators.Add(result =>
        {
            var version = result.GetValue(versionOption);
            var quality = result.GetValue(qualityOption);
            if (!string.IsNullOrWhiteSpace(quality) && !string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Quality and Version options cannot be combined. Use --version latest to pair with --quality.");
            }
        });

        root.SetAction(async (parseResult, invocationToken) =>
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(invocationToken, externalToken);
            var options = BindOptions(parseResult);
            var output = parseResult.InvocationConfiguration.Output ?? Console.Out;
            var error = parseResult.InvocationConfiguration.Error ?? Console.Error;
            var exitCode = await orchestrator.ExecuteAsync(options, output, error, linkedCts.Token);
            return exitCode;
        });

        return root;

        InstallOptions BindOptions(ParseResult parseResult) => new(
            parseResult.GetValue(channelOption)!,
            parseResult.GetValue(qualityOption),
            parseResult.GetValue(versionOption)!,
            parseResult.GetValue(internalOption),
            parseResult.GetValue(jsonOption),
            parseResult.GetValue(installDirOption)!,
            parseResult.GetValue(architectureOption)!,
            parseResult.GetValue(runtimeOption),
            parseResult.GetValue(sharedRuntimeOption),
            parseResult.GetValue(dryRunOption),
            parseResult.GetValue(noPathOption),
            parseResult.GetValue(azureFeedOption),
            parseResult.GetValue(uncachedFeedOption),
            parseResult.GetValue(feedCredentialOption),
            parseResult.GetValue(proxyAddressOption),
            parseResult.GetValue(proxyDefaultCredsOption),
            parseResult.GetValue(proxyBypassOption)!,
            !parseResult.GetValue(skipNonVersionedOption),
            parseResult.GetValue(keepZipOption),
            parseResult.GetValue(zipPathOption),
            parseResult.GetValue(verboseOption),
            parseResult.GetValue(runtimeIdOption),
            parseResult.GetValue(osOption),
            parseResult.GetValue(downloadTimeoutOption));

        Command CreateRemoveCommand()
        {
            var removeCommand = new Command("remove", "Remove an installed SDK or runtime");

            var versionArgument = new Argument<string>("version")
            {
                Description = "Sdk Version to remove",
                Arity = ArgumentArity.ExactlyOne
            };

            var removeInstallDirOption = CreateStringOption("--install-dir", "Installation root", "<auto>", "--dir", "--folder");
            var sdkOnlyOption = CreateBoolOption("--sdk-only", "Remove only the SDK");
            var removeVerboseOption = CreateBoolOption("--verbose", "Emit verbose diagnostics");

            removeCommand.Add(versionArgument);
            removeCommand.Add(removeInstallDirOption);
            removeCommand.Add(sdkOnlyOption);
            removeCommand.Add(removeVerboseOption);

            removeCommand.Validators.Add(result =>
            {
                var versionValue = result.GetValue(versionArgument);
                if (string.IsNullOrWhiteSpace(versionValue))
                {
                    result.AddError("The --version option is required when removing an SDK/runtime.");
                }
            });

            removeCommand.SetAction(async (parseResult, invocationToken) =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(invocationToken, externalToken);
                var removeOptions = BindRemoveOptions(parseResult);
                var output = parseResult.InvocationConfiguration.Output ?? Console.Out;
                var error = parseResult.InvocationConfiguration.Error ?? Console.Error;
                var exitCode = await orchestrator.ExecuteRemovalAsync(removeOptions, output, error, linkedCts.Token);
                return exitCode;
            });

            return removeCommand;

            RemoveOptions BindRemoveOptions(ParseResult parseResult) => new(
                parseResult.GetValue(versionArgument)!,
                parseResult.GetValue(removeInstallDirOption),
                parseResult.GetValue(sdkOnlyOption),
                parseResult.GetValue(removeVerboseOption));
        }
    }

    private static Option<string> CreateStringOption(string name, string description, string defaultValue, params string[] aliases)
    {
        var option = new Option<string>(name, aliases ?? Array.Empty<string>())
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => defaultValue
        };
        return option;
    }

    private static Option<string?> CreateNullableStringOption(string name, string description, params string[] aliases)
    {
        var option = new Option<string?>(name, aliases ?? Array.Empty<string>())
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne
        };
        return option;
    }

    private static Option<FileInfo?> CreateFileOption(string name, string description, params string[] aliases)
    {
        var option = new Option<FileInfo?>(name, aliases ?? Array.Empty<string>())
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne
        };
        return option;
    }

    private static Option<bool> CreateBoolOption(string name, string description, params string[] aliases)
    {
        var option = new Option<bool>(name, aliases ?? Array.Empty<string>())
        {
            Description = description
        };
        return option;
    }

    private static Option<string[]> CreateStringArrayOption(string name, string description, params string[] aliases)
    {
        var option = new Option<string[]>(name, aliases ?? Array.Empty<string>())
        {
            Description = description,
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => Array.Empty<string>()
        };
        return option;
    }

    private static Option<int> CreateIntOption(string name, string description, int defaultValue, params string[] aliases)
    {
        var option = new Option<int>(name, aliases ?? Array.Empty<string>())
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => defaultValue
        };
        return option;
    }
}
