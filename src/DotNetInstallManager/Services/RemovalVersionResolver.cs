using System.Net;
using System.Net.Http;

namespace DotNetInstallManager.Services;

internal enum RemovalTargetKind
{
    Directory,
    DirectoryPattern,
    FilePattern
}

internal enum RemovalRequestKind
{
    Sdk,
    Runtime
}

internal sealed record RemovalTarget(RemovalTargetKind Kind, string RelativeRoot, string MatchValue);

internal sealed record RemovalPlan(
    string RequestedVersion,
    RemovalRequestKind RequestedKind,
    bool SdkOnly,
    string? RuntimeVersion,
    string? AspNetCoreRuntimeVersion,
    string? WindowsDesktopRuntimeVersion,
    string? WorkloadFeatureBand,
    string? SdkManifestBand,
    string? WarningMessage,
    IReadOnlyList<RemovalTarget> Targets);

internal sealed class RemovalVersionResolver
{
    public async Task<RemovalPlan> ResolveAsync(
        string requestedVersion,
        bool sdkOnly,
        string installRoot,
        IReleaseMetadataClient metadataClient,
        CancellationToken cancellationToken)
    {
        var normalized = requestedVersion.Trim();
        var installedResolution = TryResolveInstalledVersion(normalized, installRoot);
        if (installedResolution?.RequestedKind == RemovalRequestKind.Runtime)
        {
            return BuildRuntimePlan(normalized, sdkOnly, installedResolution);
        }

        var metadataResolution = await TryResolveRequestedVersionAsync(normalized, metadataClient, cancellationToken);
        if (metadataResolution?.RequestedKind == RemovalRequestKind.Runtime)
        {
            return BuildRuntimePlan(normalized, sdkOnly, metadataResolution);
        }

        if (sdkOnly)
        {
            return BuildSdkPlan(normalized, sdkOnly, resolved: null, warningMessage: null);
        }

        var resolvedSdkTargets = installedResolution?.RequestedKind == RemovalRequestKind.Sdk
            ? await TryResolveSdkRuntimeTargetsAsync(normalized, metadataClient, cancellationToken)
            : metadataResolution;

        var warningMessage = resolvedSdkTargets is null
            ? $"Failed to resolve runtime metadata for SDK version '{normalized}'. Only SDK-specific paths will be removed. Remove the matching runtime version separately."
            : null;

        return BuildSdkPlan(normalized, sdkOnly, resolvedSdkTargets, warningMessage);
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

    private static async Task<ResolvedRemovalRequest?> TryResolveSdkRuntimeTargetsAsync(
        string requestedVersion,
        IReleaseMetadataClient metadataClient,
        CancellationToken cancellationToken)
    {
        var resolution = await TryResolveRequestedVersionAsync(requestedVersion, metadataClient, cancellationToken);
        return resolution?.RequestedKind == RemovalRequestKind.Sdk ? resolution : null;
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

    private static void AddRuntimeCompanionTargets(List<RemovalTarget> targets, ResolvedRemovalRequest resolved)
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
    }

    private static void AddSdkBandTargets(List<RemovalTarget> targets, ResolvedRemovalRequest resolved)
    {
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

    private static RemovalPlan BuildSdkPlan(
        string requestedVersion,
        bool sdkOnly,
        ResolvedRemovalRequest? resolved,
        string? warningMessage)
    {
        var targets = new List<RemovalTarget>
        {
            new(RemovalTargetKind.Directory, "sdk", requestedVersion),
            new(RemovalTargetKind.FilePattern, "swidtag", $"*{requestedVersion}*.swidtag")
        };

        if (resolved is not null)
        {
            AddRuntimeTargets(targets, resolved.RuntimeVersion, resolved.AspNetCoreRuntimeVersion, resolved.WindowsDesktopRuntimeVersion);
            AddRuntimeCompanionTargets(targets, resolved);
            AddSdkBandTargets(targets, resolved);
        }

        return FinalizePlan(
            requestedVersion,
            RemovalRequestKind.Sdk,
            sdkOnly,
            resolved?.RuntimeVersion,
            resolved?.AspNetCoreRuntimeVersion,
            resolved?.WindowsDesktopRuntimeVersion,
            resolved?.WorkloadFeatureBand,
            resolved?.SdkManifestBand,
            warningMessage,
            targets);
    }

    private static RemovalPlan BuildRuntimePlan(
        string requestedVersion,
        bool sdkOnly,
        ResolvedRemovalRequest resolved)
    {
        var targets = new List<RemovalTarget>();
        AddRuntimeTargets(targets, resolved.RuntimeVersion, resolved.AspNetCoreRuntimeVersion, resolved.WindowsDesktopRuntimeVersion);
        AddRuntimeCompanionTargets(targets, resolved);

        return FinalizePlan(
            requestedVersion,
            RemovalRequestKind.Runtime,
            sdkOnly,
            resolved.RuntimeVersion,
            resolved.AspNetCoreRuntimeVersion,
            resolved.WindowsDesktopRuntimeVersion,
            null,
            null,
            null,
            targets);
    }

    private static RemovalPlan FinalizePlan(
        string requestedVersion,
        RemovalRequestKind requestedKind,
        bool sdkOnly,
        string? runtimeVersion,
        string? aspNetCoreRuntimeVersion,
        string? windowsDesktopRuntimeVersion,
        string? workloadFeatureBand,
        string? sdkManifestBand,
        string? warningMessage,
        List<RemovalTarget> targets) =>
        new(
            requestedVersion,
            requestedKind,
            sdkOnly,
            runtimeVersion,
            aspNetCoreRuntimeVersion,
            windowsDesktopRuntimeVersion,
            workloadFeatureBand,
            sdkManifestBand,
            warningMessage,
            targets
                .GroupBy(target => $"{target.Kind}\0{target.RelativeRoot}\0{target.MatchValue}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList());

    private static ResolvedRemovalRequest? TryResolveInstalledVersion(string requestedVersion, string installRoot)
    {
        if (IsInstalledSdkVersion(installRoot, requestedVersion))
        {
            return new ResolvedRemovalRequest(
                RemovalRequestKind.Sdk,
                RuntimeVersion: null,
                AspNetCoreRuntimeVersion: null,
                WindowsDesktopRuntimeVersion: null,
                WorkloadFeatureBand: null,
                SdkManifestBand: null);
        }

        var runtimeVersion = IsInstalledCoreRuntimeVersion(installRoot, requestedVersion) ? requestedVersion : null;
        var aspNetCoreRuntimeVersion = IsInstalledAspNetCoreRuntimeVersion(installRoot, requestedVersion) ? requestedVersion : null;
        var windowsDesktopRuntimeVersion = IsInstalledWindowsDesktopRuntimeVersion(installRoot, requestedVersion) ? requestedVersion : null;

        if (runtimeVersion is null && aspNetCoreRuntimeVersion is null && windowsDesktopRuntimeVersion is null)
        {
            return null;
        }

        return new ResolvedRemovalRequest(
            RemovalRequestKind.Runtime,
            runtimeVersion,
            aspNetCoreRuntimeVersion,
            windowsDesktopRuntimeVersion,
            WorkloadFeatureBand: null,
            SdkManifestBand: null);
    }

    private static bool IsInstalledSdkVersion(string installRoot, string version) =>
        Directory.Exists(Path.Combine(installRoot, "sdk", version));

    private static bool IsInstalledCoreRuntimeVersion(string installRoot, string version) =>
        Directory.Exists(Path.Combine(installRoot, "host", "fxr", version)) ||
        Directory.Exists(Path.Combine(installRoot, "shared", "Microsoft.NETCore.App", version)) ||
        Directory.Exists(Path.Combine(installRoot, "templates", version)) ||
        Directory.Exists(Path.Combine(installRoot, "packs", "Microsoft.NETCore.App.Ref", version)) ||
        HasAppHostPackVersion(installRoot, version);

    private static bool IsInstalledAspNetCoreRuntimeVersion(string installRoot, string version) =>
        Directory.Exists(Path.Combine(installRoot, "shared", "Microsoft.AspNetCore.App", version)) ||
        Directory.Exists(Path.Combine(installRoot, "packs", "Microsoft.AspNetCore.App.Ref", version));

    private static bool IsInstalledWindowsDesktopRuntimeVersion(string installRoot, string version) =>
        Directory.Exists(Path.Combine(installRoot, "shared", "Microsoft.WindowsDesktop.App", version)) ||
        Directory.Exists(Path.Combine(installRoot, "packs", "Microsoft.WindowsDesktop.App.Ref", version));

    private static bool HasAppHostPackVersion(string installRoot, string version)
    {
        var packsRoot = Path.Combine(installRoot, "packs");
        if (!Directory.Exists(packsRoot))
        {
            return false;
        }

        return Directory.EnumerateDirectories(packsRoot, "Microsoft.NETCore.App.Host.*", SearchOption.TopDirectoryOnly)
            .Any(directory => Directory.Exists(Path.Combine(directory, version)));
    }

    private static async Task<ResolvedRemovalRequest?> TryResolveRequestedVersionAsync(
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
            foreach (var release in document.Releases)
            {
                var sdkMatch =
                    MatchesProductVersion(release.Sdk, requestedVersion) ||
                    (release.Sdks?.Any(sdk => MatchesProductVersion(sdk, requestedVersion)) ?? false);
                if (sdkMatch)
                {
                    return new ResolvedRemovalRequest(
                        RemovalRequestKind.Sdk,
                        RuntimeVersion: release.Runtime?.VersionDisplay ?? release.Runtime?.Version,
                        AspNetCoreRuntimeVersion: release.AspNetCoreRuntime?.VersionDisplay ?? release.AspNetCoreRuntime?.Version,
                        WindowsDesktopRuntimeVersion: release.WindowsDesktopRuntime?.VersionDisplay ?? release.WindowsDesktopRuntime?.Version,
                        WorkloadFeatureBand: ComputeSdkFeatureBand(requestedVersion),
                        SdkManifestBand: ComputeSdkManifestBand(requestedVersion));
                }

                var runtimeVersion = MatchesProductVersion(release.Runtime, requestedVersion)
                    ? release.Runtime?.VersionDisplay ?? release.Runtime?.Version
                    : null;
                var aspNetCoreRuntimeVersion = MatchesProductVersion(release.AspNetCoreRuntime, requestedVersion)
                    ? release.AspNetCoreRuntime?.VersionDisplay ?? release.AspNetCoreRuntime?.Version
                    : null;
                var windowsDesktopRuntimeVersion = MatchesProductVersion(release.WindowsDesktopRuntime, requestedVersion)
                    ? release.WindowsDesktopRuntime?.VersionDisplay ?? release.WindowsDesktopRuntime?.Version
                    : null;

                if (runtimeVersion is not null || aspNetCoreRuntimeVersion is not null || windowsDesktopRuntimeVersion is not null)
                {
                    return new ResolvedRemovalRequest(
                        RemovalRequestKind.Runtime,
                        runtimeVersion,
                        aspNetCoreRuntimeVersion,
                        windowsDesktopRuntimeVersion,
                        WorkloadFeatureBand: null,
                        SdkManifestBand: null);
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

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

    private sealed record ResolvedRemovalRequest(
        RemovalRequestKind RequestedKind,
        string? RuntimeVersion,
        string? AspNetCoreRuntimeVersion,
        string? WindowsDesktopRuntimeVersion,
        string? WorkloadFeatureBand,
        string? SdkManifestBand);
}
