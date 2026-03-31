using System.Net;
using System.Net.Http;
using DotNetInstallManager.Options;

namespace DotNetInstallManager.Services;

internal sealed class InstallOrchestrator : IInstallOrchestrator
{
    private readonly IRemovalElevationManager _removalElevationManager;
    private readonly Func<TextWriter, bool, InstallRemover> _removerFactory;
    private readonly TextReader _input;
    private readonly Func<bool> _isInputRedirected;

    public InstallOrchestrator()
        : this(
            new WindowsRemovalElevationManager(),
            (stdout, verbose) => new InstallRemover(stdout, verbose),
            Console.In,
            static () => Console.IsInputRedirected)
    {
    }

    internal InstallOrchestrator(
        IRemovalElevationManager removalElevationManager,
        Func<TextWriter, bool, InstallRemover> removerFactory,
        TextReader input,
        Func<bool> isInputRedirected)
    {
        _removalElevationManager = removalElevationManager;
        _removerFactory = removerFactory;
        _input = input;
        _isInputRedirected = isInputRedirected;
    }

    public async Task<int> ExecuteAsync(
        InstallOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = CreateHttpClient(options);
            var metadataClient = new ReleaseMetadataClient(httpClient);
            var plan = await InstallPlanBuilder.BuildAsync(options, metadataClient, cancellationToken);
            var installRoot = InstallEnvironment.ResolveInstallRoot(options.InstallDir);

            WritePlan(plan, options, installRoot, standardOut);

            if (options.DryRun)
            {
                return 0;
            }

            var existingInstall = await ExistingInstallDetector.DetectAsync(plan, installRoot, cancellationToken);
            if (!await ConfirmExistingInstallAsync(plan, existingInstall, options.Yes, standardOut, standardError, _input, _isInputRedirected(), cancellationToken))
            {
                return 1;
            }

            var downloader = new ArtifactDownloader(httpClient, standardOut, standardError, options.Verbose);
            var downloadResult = await downloader.DownloadAsync(plan, options, cancellationToken);
            if (!downloadResult.Success)
            {
                standardError.WriteLine("Failed to download the requested artifact from any candidate URL.");
                return 1;
            }

            standardOut.WriteLine($"Extracting archive to {installRoot}");
            var extractor = new ArchiveExtractor(standardOut, options.Verbose);

            try
            {
                extractor.Extract(downloadResult.DownloadPath!, installRoot, options.OverrideNonVersionedFiles, cancellationToken);
                InstallVerifier.VerifyInstalled(plan, installRoot);
            }
            finally
            {
                if (!options.KeepZip)
                {
                    TryDeleteArchive(downloadResult.DownloadPath, standardOut, standardError, options.Verbose);
                }
            }

            InstallEnvironment.ConfigurePath(installRoot, options.NoPath, options.Verbose, standardOut);
            standardOut.WriteLine($"Installation finished successfully: {plan.ProductKind} {plan.ProductVersion}");
            return 0;
        }
        catch (InstallException ex)
        {
            standardError.WriteLine(ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            standardError.WriteLine("Operation cancelled.");
            return 1;
        }
    }

    public async Task<int> ExecuteRemovalAsync(
        RemoveOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        try
        {
            var installRoot = InstallEnvironment.ResolveInstallRoot(options.InstallDir ?? "<auto>");
            var resolver = new RemovalVersionResolver();
            using var metadataHttpClient = RemovalVersionResolver.CreateMetadataHttpClient();
            var metadataClient = new ReleaseMetadataClient(metadataHttpClient);
            var plan = await resolver.ResolveAsync(options.Version, options.SdkOnly, installRoot, metadataClient, cancellationToken);
            plan = await FilterSharedTargetsAsync(plan, installRoot, resolver, metadataClient, cancellationToken);
            if (!string.IsNullOrWhiteSpace(plan.WarningMessage))
            {
                standardOut.WriteLine(plan.WarningMessage);
            }

            var remover = _removerFactory(standardOut, options.Verbose);
            RemovalResult result;

            try
            {
                result = remover.Remove(plan, installRoot, options.DryRun, cancellationToken);
            }
            catch (RemovalRequiresElevationException) when (!options.DryRun && _removalElevationManager.CanRetryAsAdministrator)
            {
                if (!_removalElevationManager.TryRunElevatedRemove(standardOut, options.Verbose, out var exitCode, out var failureReason))
                {
                    standardError.WriteLine($"Failed to relaunch removal with administrator privileges: {failureReason}");
                    return 1;
                }

                return exitCode;
            }

            standardOut.WriteLine(result.DryRun
                ? $"Removal dry-run complete: {result.MatchedPaths.Count} path(s) would be removed for version {result.RequestedVersion} under {result.InstallRoot}"
                : $"Removal finished successfully: removed {result.MatchedPaths.Count} path(s) for version {result.RequestedVersion} under {result.InstallRoot}");
            return 0;
        }
        catch (InstallException ex)
        {
            standardError.WriteLine(ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            standardError.WriteLine("Operation cancelled.");
            return 1;
        }
    }

    private static HttpClient CreateHttpClient(InstallOptions options)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };

        if (!string.IsNullOrWhiteSpace(options.ProxyAddress))
        {
            handler.UseProxy = true;
            handler.Proxy = BuildProxy(options);
            handler.DefaultProxyCredentials = options.ProxyUseDefaultCredentials ? CredentialCache.DefaultNetworkCredentials : null;
        }
        else
        {
            handler.UseProxy = false;
        }

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = options.DownloadTimeout
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-install-nativeaot");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, br, deflate");
        return client;
    }

    private static IWebProxy BuildProxy(InstallOptions options)
    {
        var proxy = new WebProxy(options.ProxyAddress!)
        {
            BypassProxyOnLocal = false
        };

        if (options.ProxyUseDefaultCredentials)
        {
            proxy.Credentials = CredentialCache.DefaultCredentials;
        }

        if (options.ProxyBypassList.Count > 0)
        {
            proxy.BypassList = options.ProxyBypassList.ToArray();
        }

        return proxy;
    }

    private static void WritePlan(InstallPlan plan, InstallOptions options, string installRoot, TextWriter standardOut)
    {
        standardOut.WriteLine($"dotnet-install plan for channel {plan.ChannelVersion} ({plan.ReleaseVersion})");
        standardOut.WriteLine($"Product: {plan.ProductKind} {plan.ProductVersion} | RID: {plan.TargetRid} | Preview: {plan.IsPreview}");
        standardOut.WriteLine($"InstallRoot: {installRoot}");
        standardOut.WriteLine($"Primary URL: {plan.SourceUrl}");
        standardOut.WriteLine("Candidate URLs:");
        for (var i = 0; i < plan.CandidateUrls.Count; i++)
        {
            standardOut.WriteLine($"  [{i}] {plan.CandidateUrls[i]}");
        }
        standardOut.WriteLine($"DryRun: {options.DryRun} | KeepZip: {options.KeepZip} | ZipPath: {options.ZipPath?.FullName ?? "<temp>"}");
    }

    private static void TryDeleteArchive(string? archivePath, TextWriter standardOut, TextWriter standardError, bool verbose)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            return;
        }

        try
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
                if (verbose)
                {
                    standardOut.WriteLine($"The temporary archive file \"{archivePath}\" was removed.");
                }
            }
            else if (verbose)
            {
                standardOut.WriteLine($"The temporary archive file \"{archivePath}\" does not exist, therefore is not removed.");
            }
        }
        catch (Exception ex)
        {
            standardError.WriteLine($"Failed to remove the temporary archive file \"{archivePath}\": {ex.Message}");
        }
    }

    internal static async Task<RemovalPlan> FilterSharedTargetsAsync(
        RemovalPlan requestedPlan,
        string installRoot,
        RemovalVersionResolver resolver,
        IReleaseMetadataClient metadataClient,
        CancellationToken cancellationToken)
    {
        if (requestedPlan.SdkOnly || requestedPlan.RequestedKind == RemovalRequestKind.Runtime)
        {
            return requestedPlan;
        }

        var sdkRoot = Path.Combine(installRoot, "sdk");
        if (!Directory.Exists(sdkRoot))
        {
            return requestedPlan;
        }

        var installedSdkVersions = Directory.EnumerateDirectories(sdkRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) &&
                           !string.Equals(name, requestedPlan.RequestedVersion, StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .ToList();

        if (installedSdkVersions.Count == 0)
        {
            return requestedPlan;
        }

        var sharedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sdkVersion in installedSdkVersions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var otherPlan = await resolver.ResolveAsync(sdkVersion, sdkOnly: false, installRoot, metadataClient, cancellationToken);
            foreach (var target in otherPlan.Targets)
            {
                sharedKeys.Add(GetTargetKey(target));
            }
        }

        var filteredTargets = requestedPlan.Targets
            .Where(target => IsSdkTargetForRequestedVersion(target, requestedPlan.RequestedVersion) || !sharedKeys.Contains(GetTargetKey(target)))
            .ToList();

        return requestedPlan with { Targets = filteredTargets };
    }

    private static bool IsSdkTargetForRequestedVersion(RemovalTarget target, string requestedVersion) =>
        target.Kind == RemovalTargetKind.Directory &&
        string.Equals(target.RelativeRoot, "sdk", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(target.MatchValue, requestedVersion, StringComparison.OrdinalIgnoreCase);

    private static string GetTargetKey(RemovalTarget target) =>
        $"{target.Kind}\0{target.RelativeRoot}\0{target.MatchValue}";

    internal static async Task<bool> ConfirmExistingInstallAsync(
        InstallPlan plan,
        ExistingInstallDetectionResult detection,
        bool skipConfirmation,
        TextWriter standardOut,
        TextWriter standardError,
        TextReader input,
        bool inputRedirected,
        CancellationToken cancellationToken)
    {
        if (!detection.IsInstalled)
        {
            return true;
        }

        standardOut.WriteLine($"Warning: {InstallVerifier.GetAssetDisplayName(plan.ProductKind)} version '{plan.ProductVersion}' is already installed.");
        foreach (var match in detection.Matches)
        {
            standardOut.WriteLine($"  Existing installation detected via {match.Source}: {match.Location}");
        }

        if (skipConfirmation)
        {
            standardOut.WriteLine("Continuing installation because --yes was specified.");
            return true;
        }

        if (inputRedirected)
        {
            standardError.WriteLine("Installation canceled because confirmation is required and standard input is redirected.");
            return false;
        }

        standardOut.Write("Continue installation? [y/N]: ");
        var response = await input.ReadLineAsync(cancellationToken);
        if (response is null ||
            (!response.Equals("y", StringComparison.OrdinalIgnoreCase) &&
             !response.Equals("yes", StringComparison.OrdinalIgnoreCase)))
        {
            standardError.WriteLine("Installation canceled.");
            return false;
        }

        return true;
    }
}
