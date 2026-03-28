using System.Net;
using System.Net.Http;
using System.Text;
using DotNetInstallManager.Options;

namespace DotNetInstallManager.Services;

internal sealed class InstallOrchestrator : IInstallOrchestrator
{
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

    public Task<int> ExecuteRemovalAsync(
        RemoveOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("dotnet-install removal");
        builder.AppendLine($"InstallDir: {options.InstallDir}");
        builder.AppendLine($"Version: {options.Version} | SdkOnly: {options.SdkOnly}");
        builder.AppendLine($"Verbose: {options.Verbose}");
        builder.AppendLine("Next step: implement deletion of target SDK/runtime folders.");

        standardOut.Write(builder.ToString());
        standardError.WriteLine("Removal pipeline not wired yet. This is a planning stub.");

        return Task.FromResult(0);
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
}
