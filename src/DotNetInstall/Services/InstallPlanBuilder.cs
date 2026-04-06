using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using DotNetInstall.Options;

namespace DotNetInstall.Services;

internal enum InstallProductKind
{
    Sdk,
    Runtime,
    AspNetCoreRuntime,
    WindowsDesktop
}

internal sealed record InstallPlan(
    string ChannelVersion,
    string ReleaseVersion,
    string ProductVersion,
    InstallProductKind ProductKind,
    string TargetRid,
    string AssetName,
    string SourceUrl,
    IReadOnlyList<string> CandidateUrls,
    string? ExpectedHash,
    bool IsPreview);

internal static class InstallPlanBuilder
{
    private const string AutoToken = "<auto>";
    private static readonly string[] KnownFeedRoots =
    {
        "https://builds.dotnet.microsoft.com/dotnet",
        "https://dotnetcli.blob.core.windows.net/dotnet"
    };

    public static async Task<InstallPlan> BuildAsync(
        InstallOptions options,
        IReleaseMetadataClient metadataClient,
        CancellationToken cancellationToken)
    {
        var index = await metadataClient.GetReleaseIndexAsync(cancellationToken);
        var channel = ResolveChannelEntry(index, options.Channel, options.Version);
        var releaseDocument = await metadataClient.GetChannelReleaseDocumentAsync(channel.ReleasesJsonUrl, cancellationToken);
        var release = ResolveRelease(releaseDocument, options);
        var productKind = DetermineProductKind(options);
        var product = ResolveProduct(release, productKind, options);
        var rid = ResolveRuntimeIdentifier(options);
        var asset = ResolveAsset(product, rid);
        var candidateUrls = BuildCandidateUrls(asset.Url, options);

        return new InstallPlan(
            releaseDocument.ChannelVersion,
            release.ReleaseVersion,
            product.Version ?? product.VersionDisplay ?? release.ReleaseVersion,
            productKind,
            rid,
            asset.Name,
            asset.Url,
            candidateUrls,
            asset.Hash,
            IsPreviewRelease(release.ReleaseVersion));
    }

    private static ReleaseIndexEntry ResolveChannelEntry(ReleaseIndexDocument index, string channelOption, string versionOption)
    {
        if (TryInferChannelFromVersion(versionOption, out var versionChannel))
        {
            return index.Entries.FirstOrDefault(e =>
                string.Equals(e.ChannelVersion, versionChannel, StringComparison.OrdinalIgnoreCase))
                ?? throw new InstallException($"Channel '{versionChannel}' inferred from version '{versionOption.Trim()}' was not found in release metadata.");
        }

        if (string.IsNullOrWhiteSpace(channelOption))
        {
            throw new InstallException("Channel option cannot be empty.");
        }

        var channel = channelOption.Trim();
        if (channel.Equals("lts", StringComparison.OrdinalIgnoreCase))
        {
            return index.Entries
                .Where(e => string.Equals(e.ReleaseType, "lts", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseChannelVersion)
                .FirstOrDefault() ?? throw new InstallException("No LTS channel found in release metadata.");
        }

        if (channel.Equals("sts", StringComparison.OrdinalIgnoreCase))
        {
            return index.Entries
                .Where(e => string.Equals(e.ReleaseType, "sts", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseChannelVersion)
                .FirstOrDefault() ?? throw new InstallException("No STS channel found in release metadata.");
        }

        if (channel.Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            return index.Entries
                .Where(e => string.Equals(e.SupportPhase, "preview", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(ParseChannelVersion)
                .FirstOrDefault() ?? throw new InstallException("No preview channel found in release metadata.");
        }

        var direct = index.Entries.FirstOrDefault(e =>
            string.Equals(e.ChannelVersion, channel, StringComparison.OrdinalIgnoreCase));

        return direct ?? throw new InstallException($"Channel '{channel}' was not found in release metadata.");
    }

    private static ReleaseEntry ResolveRelease(ReleaseDocument document, InstallOptions options)
    {
        var ordered = document.Releases
            .OrderByDescending(r => TryParseReleaseDate(r.ReleaseDate))
            .ThenByDescending(r => ParseSemanticVersion(r.ReleaseVersion))
            .ToList();

        if (!IsLatest(options.Version))
        {
            var version = options.Version.Trim();
            if (TryParseVersionSelector(version, out var selector))
            {
                var wildcardRelease = ordered.FirstOrDefault(r => ReleaseMatchesVersionSelector(r, selector));
                if (wildcardRelease is null)
                {
                    throw new InstallException($"Version '{version}' was not found in channel {document.ChannelVersion}.");
                }

                return wildcardRelease;
            }

            var explicitRelease = ordered.FirstOrDefault(r => ReleaseMatchesVersion(r, version));
            if (explicitRelease is null)
            {
                throw new InstallException($"Version '{version}' was not found in channel {document.ChannelVersion}.");
            }

            return explicitRelease;
        }

        var candidates = FilterByQuality(ordered, options.Quality);
        var release = candidates.FirstOrDefault();
        if (release is null)
        {
            throw new InstallException($"No releases matched quality '{options.Quality ?? "ga"}' within channel {document.ChannelVersion}.");
        }

        return release;
    }

    private static IEnumerable<ReleaseEntry> FilterByQuality(IEnumerable<ReleaseEntry> releases, string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality) || quality.Equals("ga", StringComparison.OrdinalIgnoreCase))
        {
            return releases.Where(r => !IsPreviewRelease(r.ReleaseVersion));
        }

        if (quality.Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            return releases.Where(r => IsPreviewRelease(r.ReleaseVersion));
        }

        if (quality.Equals("daily", StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallException("Daily quality builds require explicit URLs. Use --uncached-feed or --azure-feed with a known build path.");
        }

        throw new InstallException($"Quality '{quality}' is not supported.");
    }

    private static bool ReleaseMatchesVersion(ReleaseEntry entry, string version) =>
        string.Equals(entry.ReleaseVersion, version, StringComparison.OrdinalIgnoreCase) ||
        MatchesProductVersion(entry.Sdk, version) ||
        MatchesProductVersion(entry.Runtime, version) ||
        MatchesProductVersion(entry.AspNetCoreRuntime, version) ||
        MatchesProductVersion(entry.WindowsDesktopRuntime, version) ||
        (entry.Sdks?.Any(s => MatchesProductVersion(s, version)) ?? false);

    private static bool ReleaseMatchesVersionSelector(ReleaseEntry entry, VersionSelector selector) =>
        MatchesVersionSelector(entry.ReleaseVersion, selector) ||
        MatchesVersionSelector(entry.Sdk, selector) ||
        MatchesVersionSelector(entry.Runtime, selector) ||
        MatchesVersionSelector(entry.AspNetCoreRuntime, selector) ||
        MatchesVersionSelector(entry.WindowsDesktopRuntime, selector) ||
        (entry.Sdks?.Any(s => MatchesVersionSelector(s, selector)) ?? false);

    private static bool MatchesProductVersion(ReleaseProduct? product, string version)
    {
        if (product is null)
        {
            return false;
        }

        return (!string.IsNullOrEmpty(product.Version) && product.Version.Equals(version, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrEmpty(product.VersionDisplay) && product.VersionDisplay.Equals(version, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesVersionSelector(ReleaseProduct? product, VersionSelector selector)
    {
        if (product is null)
        {
            return false;
        }

        return MatchesVersionSelector(product.Version, selector) ||
               MatchesVersionSelector(product.VersionDisplay, selector);
    }

    private static bool MatchesVersionSelector(string? version, VersionSelector selector) =>
        TryParseVersionComponents(version, out var major, out var minor) &&
        major == selector.Major &&
        (!selector.Minor.HasValue || minor == selector.Minor.Value);

    private static bool IsLatest(string version) =>
        string.IsNullOrWhiteSpace(version) || version.Equals("latest", StringComparison.OrdinalIgnoreCase);

    private static bool IsPreviewRelease(string? releaseVersion)
    {
        if (string.IsNullOrWhiteSpace(releaseVersion))
        {
            return false;
        }

        return releaseVersion.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
               releaseVersion.Contains("rc", StringComparison.OrdinalIgnoreCase) ||
               releaseVersion.Contains("alpha", StringComparison.OrdinalIgnoreCase) ||
               releaseVersion.Contains("beta", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime TryParseReleaseDate(string? value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result))
        {
            return result;
        }

        return DateTime.MinValue;
    }

    private static Version ParseSemanticVersion(string? version)
    {
        if (Version.TryParse(version?.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0], out var parsed))
        {
            return parsed;
        }

        return new Version(0, 0);
    }

    private static Version ParseChannelVersion(ReleaseIndexEntry entry)
    {
        if (Version.TryParse(entry.ChannelVersion, out var value))
        {
            return value;
        }

        return new Version(0, 0);
    }

    private static InstallProductKind DetermineProductKind(InstallOptions options)
    {
        if (!options.RequestsRuntimeOnly)
        {
            return InstallProductKind.Sdk;
        }

        var runtime = options.EffectiveRuntime;
        if (string.IsNullOrWhiteSpace(runtime))
        {
            return InstallProductKind.Runtime;
        }

        return runtime.ToLowerInvariant() switch
        {
            "dotnet" => InstallProductKind.Runtime,
            "aspnetcore" => InstallProductKind.AspNetCoreRuntime,
            "windowsdesktop" => InstallProductKind.WindowsDesktop,
            _ => throw new InstallException($"Runtime '{runtime}' is not supported.")
        };
    }

    private static ReleaseProduct ResolveProduct(ReleaseEntry release, InstallProductKind kind, InstallOptions options) =>
        kind switch
        {
            InstallProductKind.Sdk => ResolveSdkProduct(release, options),
            InstallProductKind.Runtime => release.Runtime ?? throw new InstallException($"Release {release.ReleaseVersion} does not contain a dotnet runtime payload."),
            InstallProductKind.AspNetCoreRuntime => release.AspNetCoreRuntime ?? throw new InstallException($"Release {release.ReleaseVersion} does not contain an ASP.NET Core runtime payload."),
            InstallProductKind.WindowsDesktop => release.WindowsDesktopRuntime ?? throw new InstallException($"Release {release.ReleaseVersion} does not contain a WindowsDesktop runtime payload."),
            _ => throw new InstallException("Unsupported product kind.")
        };

    private static ReleaseProduct ResolveSdkProduct(ReleaseEntry release, InstallOptions options)
    {
        if (!IsLatest(options.Version))
        {
            var version = options.Version.Trim();
            ReleaseProduct? match;
            if (TryParseVersionSelector(version, out var selector))
            {
                match = release.Sdks?.FirstOrDefault(s => MatchesVersionSelector(s, selector)) ??
                        (MatchesVersionSelector(release.Sdk, selector) ? release.Sdk : null);
            }
            else
            {
                match = release.Sdks?.FirstOrDefault(s => MatchesProductVersion(s, version)) ??
                        (MatchesProductVersion(release.Sdk, version) ? release.Sdk : null);
            }

            if (match is null)
            {
                throw new InstallException($"SDK version '{options.Version}' cannot be located within release {release.ReleaseVersion}.");
            }

            return match;
        }

        return release.Sdks?.FirstOrDefault() ?? release.Sdk ?? throw new InstallException($"Release {release.ReleaseVersion} does not expose SDK artifacts.");
    }

    private static ReleaseFile ResolveAsset(ReleaseProduct product, string targetRid)
    {
        if (product.Files is null || product.Files.Count == 0)
        {
            throw new InstallException($"No downloadable files were found for version '{product.Version ?? product.VersionDisplay}'.");
        }

        var matches = product.Files
            .Where(f => string.Equals(f.Rid, targetRid, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => RankByExtension(f.Name, targetRid.StartsWith("win", StringComparison.OrdinalIgnoreCase)))
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0)
        {
            throw new InstallException($"Runtime identifier '{targetRid}' is not available for '{product.Version ?? product.VersionDisplay}'.");
        }

        return matches[0];
    }

    private static int RankByExtension(string fileName, bool preferWindowsZip)
    {
        var extension = GetArchiveExtension(fileName);

        var preferences = preferWindowsZip
            ? new[] { ".zip", ".exe", ".msi" }
            : new[] { ".tar.gz", ".tar", ".zip", ".pkg" };

        var index = Array.IndexOf(preferences, extension);
        return index >= 0 ? index : preferences.Length;
    }

    private static string GetArchiveExtension(string fileName)
    {
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            return ".tar.gz";
        }

        if (fileName.EndsWith(".tar.bz2", StringComparison.OrdinalIgnoreCase))
        {
            return ".tar.bz2";
        }

        return Path.GetExtension(fileName) ?? string.Empty;
    }

    private static string ResolveRuntimeIdentifier(InstallOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.RuntimeId))
        {
            return options.RuntimeId;
        }

        var architecture = ResolveArchitecture(options.Architecture);
        var os = ResolveOperatingSystem(options.UserProvidedOs);

        return $"{os}-{architecture}";
    }

    private static string ResolveArchitecture(string? architecture)
    {
        if (string.IsNullOrWhiteSpace(architecture) || architecture.Equals(AutoToken, StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                Architecture.X86 => "x86",
                Architecture.S390x => "s390x",
                Architecture.Wasm => "wasm",
                Architecture.Ppc64le => "ppc64le",
                _ => throw new InstallException($"Unsupported architecture '{RuntimeInformation.ProcessArchitecture}'.")
            };
        }

        return architecture.ToLowerInvariant() switch
        {
            "amd64" => "x64",
            "x64" => "x64",
            "x86" => "x86",
            "arm64" => "arm64",
            "arm" => "arm",
            "armhf" => "arm",
            "ppc64le" => "ppc64le",
            "s390x" => "s390x",
            "wasm" => "wasm",
            _ => throw new InstallException($"Architecture '{architecture}' is not supported.")
        };
    }

    private static string ResolveOperatingSystem(string? userProvidedOs)
    {
        if (!string.IsNullOrWhiteSpace(userProvidedOs) && !userProvidedOs.Equals(AutoToken, StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeOs(userProvidedOs);
        }

        if (OperatingSystem.IsWindows())
        {
            return "win";
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx" : "osx";
        }

        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        throw new InstallException("Unable to determine the host operating system. Use --os to specify it explicitly.");
    }

    private static string NormalizeOs(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "win" or "windows" => "win",
            "osx" or "macos" => "osx",
            "linux" => "linux",
            "linux-musl" or "alpine" => "linux-musl",
            "linux-musleabihf" => "linux-musl",
            "rhel6" or "rhel.6" => "rhel.6",
            "freebsd" => "freebsd",
            _ => value.ToLowerInvariant()
        };
    }

    private static IReadOnlyList<string> BuildCandidateUrls(string primaryUrl, InstallOptions options)
    {
        var urls = new List<string>();

        if (TryRewriteFeed(primaryUrl, options.AzureFeed, out var azureUrl))
        {
            urls.Add(ApplyFeedCredential(azureUrl, options.FeedCredential));
        }

        urls.Add(ApplyFeedCredential(primaryUrl, options.FeedCredential));

        if (TryRewriteFeed(primaryUrl, options.UncachedFeed, out var uncachedUrl))
        {
            urls.Add(ApplyFeedCredential(uncachedUrl, options.FeedCredential));
        }

        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool TryRewriteFeed(string originalUrl, string? overrideRoot, out string rewrittenUrl)
    {
        rewrittenUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(overrideRoot))
        {
            return false;
        }

        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        if (!Uri.TryCreate(overrideRoot, UriKind.Absolute, out var overrideUri))
        {
            return false;
        }

        var baseUri = EnsureTrailingSlash(overrideUri);
        var relative = ExtractDotnetRelativePath(originUri);
        rewrittenUrl = new Uri(baseUri, relative).ToString();
        return true;
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);

    private static string ExtractDotnetRelativePath(Uri originUri)
    {
        var path = originUri.AbsolutePath.TrimStart('/');
        var dotnetIndex = path.IndexOf("dotnet/", StringComparison.OrdinalIgnoreCase);
        if (dotnetIndex >= 0)
        {
            return path[(dotnetIndex + "dotnet/".Length)..];
        }

        foreach (var known in KnownFeedRoots)
        {
            if (originUri.AbsoluteUri.StartsWith(known, StringComparison.OrdinalIgnoreCase))
            {
                return originUri.AbsoluteUri.Substring(known.Length).TrimStart('/');
            }
        }

        return path;
    }

    private static string ApplyFeedCredential(string url, string? credential)
    {
        if (string.IsNullOrWhiteSpace(credential))
        {
            return url;
        }

        var trimmed = credential.Trim();
        var token = trimmed.TrimStart('?', '&');
        var separator = url.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{url}{separator}{token}";
    }

    private static bool TryInferChannelFromVersion(string version, out string channelVersion)
    {
        channelVersion = string.Empty;

        if (IsLatest(version))
        {
            return false;
        }

        if (TryParseVersionSelector(version, out var selector))
        {
            channelVersion = selector.Minor.HasValue
                ? $"{selector.Major}.{selector.Minor.Value}"
                : $"{selector.Major}.0";
            return true;
        }

        var stablePart = version.Trim().Split('-', 2, StringSplitOptions.TrimEntries)[0];
        var parts = stablePart.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var major) &&
            int.TryParse(parts[1], out var minor))
        {
            channelVersion = $"{major}.{minor}";
            return true;
        }

        return false;
    }

    private static bool TryParseVersionSelector(string version, out VersionSelector selector)
    {
        selector = default;

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var parts = version.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 &&
            parts[1].Equals("x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[0], out var major))
        {
            selector = new VersionSelector(major, null);
            return true;
        }

        if (parts.Length == 3 &&
            parts[2].Equals("x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[0], out major) &&
            int.TryParse(parts[1], out var minor))
        {
            selector = new VersionSelector(major, minor);
            return true;
        }

        return false;
    }

    private static bool TryParseVersionComponents(string? version, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var stablePart = version.Split('-', 2, StringSplitOptions.TrimEntries)[0];
        var parts = stablePart.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 &&
               int.TryParse(parts[0], out major) &&
               int.TryParse(parts[1], out minor);
    }

    private readonly record struct VersionSelector(int Major, int? Minor);
}
