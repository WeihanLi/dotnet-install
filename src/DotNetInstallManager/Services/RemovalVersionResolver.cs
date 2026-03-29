using System.Net;
using System.Net.Http;

namespace DotNetInstallManager.Services;

internal enum RemovalTargetKind
{
    Directory,
    DirectoryPattern,
    FilePattern
}

internal sealed record RemovalTarget(RemovalTargetKind Kind, string RelativeRoot, string MatchValue);

internal sealed record RemovalPlan(
    string RequestedVersion,
    bool SdkOnly,
    string? RuntimeVersion,
    string? AspNetCoreRuntimeVersion,
    string? WindowsDesktopRuntimeVersion,
    string? WorkloadFeatureBand,
    string? SdkManifestBand,
    IReadOnlyList<RemovalTarget> Targets);

internal sealed class RemovalVersionResolver
{
    public async Task<RemovalPlan> ResolveAsync(
        string requestedVersion,
        bool sdkOnly,
        IReleaseMetadataClient metadataClient,
        CancellationToken cancellationToken)
    {
        var normalized = requestedVersion.Trim();
        var targets = new List<RemovalTarget>
        {
            new(RemovalTargetKind.Directory, "sdk", normalized),
            new(RemovalTargetKind.FilePattern, "swidtag", $"*{normalized}*.swidtag")
        };

        if (sdkOnly)
        {
            return new RemovalPlan(normalized, sdkOnly, null, null, null, null, null, targets);
        }

        var resolved = await TryResolveSdkRuntimeTargetsAsync(normalized, metadataClient, cancellationToken)
            ?? BuildFallbackResolvedTargets(normalized);

        AddRuntimeTargets(targets, resolved.RuntimeVersion, resolved.AspNetCoreRuntimeVersion, resolved.WindowsDesktopRuntimeVersion);
        AddSdkCompanionTargets(targets, resolved);

        return new RemovalPlan(
            normalized,
            sdkOnly,
            resolved.RuntimeVersion,
            resolved.AspNetCoreRuntimeVersion,
            resolved.WindowsDesktopRuntimeVersion,
            resolved.WorkloadFeatureBand,
            resolved.SdkManifestBand,
            targets
                .GroupBy(target => $"{target.Kind}\0{target.RelativeRoot}\0{target.MatchValue}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList());
    }

    internal static HttpClient CreateMetadataHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-install");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, br, deflate");
        return client;
    }

    private static async Task<ResolvedSdkTargets?> TryResolveSdkRuntimeTargetsAsync(
        string requestedVersion,
        IReleaseMetadataClient metadataClient,
        CancellationToken cancellationToken)
    {
        var channelVersion = InferChannelVersion(requestedVersion);
        if (channelVersion is null)
        {
            return null;
        }

        try
        {
            var index = await metadataClient.GetReleaseIndexAsync(cancellationToken);
            var channel = index.Entries.FirstOrDefault(entry =>
                string.Equals(entry.ChannelVersion, channelVersion, StringComparison.OrdinalIgnoreCase));

            if (channel is null)
            {
                return null;
            }

            var document = await metadataClient.GetChannelReleaseDocumentAsync(channel.ReleasesJsonUrl, cancellationToken);
            var release = document.Releases.FirstOrDefault(entry =>
                MatchesProductVersion(entry.Sdk, requestedVersion) ||
                (entry.Sdks?.Any(sdk => MatchesProductVersion(sdk, requestedVersion)) ?? false));

            if (release is null)
            {
                return null;
            }

            return new ResolvedSdkTargets(
                RuntimeVersion: release.Runtime?.VersionDisplay ?? release.Runtime?.Version,
                AspNetCoreRuntimeVersion: release.AspNetCoreRuntime?.VersionDisplay ?? release.AspNetCoreRuntime?.Version,
                WindowsDesktopRuntimeVersion: release.WindowsDesktopRuntime?.VersionDisplay ?? release.WindowsDesktopRuntime?.Version,
                WorkloadFeatureBand: ComputeSdkFeatureBand(requestedVersion),
                SdkManifestBand: ComputeSdkManifestBand(requestedVersion));
        }
        catch
        {
            return null;
        }
    }

    private static void AddRuntimeTargets(
        List<RemovalTarget> targets,
        string? runtimeVersion,
        string? aspNetCoreRuntimeVersion,
        string? windowsDesktopRuntimeVersion)
    {
        AddVersionedDirectory(targets, Path.Combine("host", "fxr"), runtimeVersion);
        AddVersionedDirectory(targets, Path.Combine("shared", "Microsoft.NETCore.App"), runtimeVersion);
        AddVersionedDirectory(targets, Path.Combine("shared", "Microsoft.AspNetCore.App"), aspNetCoreRuntimeVersion);
        AddVersionedDirectory(targets, Path.Combine("shared", "Microsoft.WindowsDesktop.App"), windowsDesktopRuntimeVersion);
    }

    private static void AddSdkCompanionTargets(List<RemovalTarget> targets, ResolvedSdkTargets resolved)
    {
        AddVersionedDirectory(targets, "templates", resolved.RuntimeVersion);
        AddVersionedDirectory(targets, Path.Combine("packs", "Microsoft.NETCore.App.Ref"), resolved.RuntimeVersion);
        AddVersionedDirectory(targets, Path.Combine("packs", "Microsoft.AspNetCore.App.Ref"), resolved.AspNetCoreRuntimeVersion);
        AddVersionedDirectory(targets, Path.Combine("packs", "Microsoft.WindowsDesktop.App.Ref"), resolved.WindowsDesktopRuntimeVersion);

        if (!string.IsNullOrWhiteSpace(resolved.RuntimeVersion))
        {
            targets.Add(new RemovalTarget(RemovalTargetKind.DirectoryPattern, Path.Combine("packs", "Microsoft.NETCore.App.Host.*"), resolved.RuntimeVersion));
            targets.Add(new RemovalTarget(RemovalTargetKind.FilePattern, "swidtag", $"*{resolved.RuntimeVersion}*.swidtag"));
        }

        if (!string.IsNullOrWhiteSpace(resolved.AspNetCoreRuntimeVersion))
        {
            targets.Add(new RemovalTarget(RemovalTargetKind.FilePattern, "swidtag", $"*{resolved.AspNetCoreRuntimeVersion}*.swidtag"));
        }

        if (!string.IsNullOrWhiteSpace(resolved.WindowsDesktopRuntimeVersion))
        {
            targets.Add(new RemovalTarget(RemovalTargetKind.FilePattern, "swidtag", $"*{resolved.WindowsDesktopRuntimeVersion}*.swidtag"));
        }

        AddVersionedDirectory(targets, Path.Combine("metadata", "workloads"), resolved.WorkloadFeatureBand);
        AddVersionedDirectory(targets, "sdk-manifests", resolved.SdkManifestBand);
    }

    private static void AddVersionedDirectory(List<RemovalTarget> targets, string relativeRoot, string? version)
    {
        if (!string.IsNullOrWhiteSpace(version))
        {
            targets.Add(new RemovalTarget(RemovalTargetKind.Directory, relativeRoot, version));
        }
    }

    private static ResolvedSdkTargets BuildFallbackResolvedTargets(string requestedVersion) =>
        new(
            RuntimeVersion: requestedVersion,
            AspNetCoreRuntimeVersion: requestedVersion,
            WindowsDesktopRuntimeVersion: requestedVersion,
            WorkloadFeatureBand: ComputeSdkFeatureBand(requestedVersion),
            SdkManifestBand: ComputeSdkManifestBand(requestedVersion));

    private static bool MatchesProductVersion(ReleaseProduct? product, string version)
    {
        if (product is null)
        {
            return false;
        }

        return (!string.IsNullOrEmpty(product.Version) && product.Version.Equals(version, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrEmpty(product.VersionDisplay) && product.VersionDisplay.Equals(version, StringComparison.OrdinalIgnoreCase));
    }

    private static string? InferChannelVersion(string version)
    {
        var trimmed = version.Split('-', 2, StringSplitOptions.TrimEntries)[0];
        var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : null;
    }

    private static string? ComputeSdkFeatureBand(string sdkVersion)
    {
        if (!TryParseSdkCoreVersion(sdkVersion, out var major, out var minor, out var patch))
        {
            return null;
        }

        var featureBand = patch - (patch % 100);
        return $"{major}.{minor}.{featureBand}";
    }

    private static string? ComputeSdkManifestBand(string sdkVersion)
    {
        if (!TryParseSdkCoreVersion(sdkVersion, out var major, out var minor, out _))
        {
            return null;
        }

        var prerelease = sdkVersion.Split('-', 2, StringSplitOptions.TrimEntries);
        if (prerelease.Length == 1)
        {
            return $"{major}.{minor}.100";
        }

        var previewParts = prerelease[1].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (previewParts.Length >= 2)
        {
            return $"{major}.{minor}.100-{previewParts[0]}.{previewParts[1]}";
        }

        return $"{major}.{minor}.100-{prerelease[1]}";
    }

    private static bool TryParseSdkCoreVersion(string sdkVersion, out int major, out int minor, out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;

        var stablePart = sdkVersion.Split('-', 2, StringSplitOptions.TrimEntries)[0];
        var parts = stablePart.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 3 &&
               int.TryParse(parts[0], out major) &&
               int.TryParse(parts[1], out minor) &&
               int.TryParse(parts[2], out patch);
    }

    private sealed record ResolvedSdkTargets(
        string? RuntimeVersion,
        string? AspNetCoreRuntimeVersion,
        string? WindowsDesktopRuntimeVersion,
        string? WorkloadFeatureBand,
        string? SdkManifestBand);
}
