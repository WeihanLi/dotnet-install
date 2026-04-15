using DotNetInstall.Options;

namespace DotNetInstall.Services;

internal sealed record UpdatePlan(
    string RequestedVersion,
    string ResolvedVersion,
    InstallProductKind ProductKind,
    string ChannelVersion,
    string InstallRoot,
    bool InstallRequired,
    IReadOnlyList<string> InstalledVersions,
    IReadOnlyList<string> ObsoleteVersions,
    InstallPlan InstallPlan);

internal static class UpdatePlanner
{
    public static async Task<UpdatePlan> BuildAsync(
        UpdateOptions options,
        IReleaseMetadataClient metadataClient,
        CancellationToken cancellationToken)
    {
        var installOptions = CreateInstallOptions(options);
        var installRoot = InstallEnvironment.ResolveInstallRoot(options.InstallDir);
        var installPlan = await InstallPlanBuilder.BuildAsync(installOptions, metadataClient, cancellationToken);
        var installedVersions = GetInstalledVersions(installRoot, installPlan.ProductKind)
            .Where(version => IsInChannel(version, installPlan.ChannelVersion))
            .OrderBy(ParseVersion)
            .ThenBy(version => version, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var installRequired = !InstallVerifier.GetInstalledLocations(installPlan, installRoot).Any();
        var obsoleteVersions = installedVersions
            .Where(version => !string.Equals(version, installPlan.ProductVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return new UpdatePlan(
            options.Versions[0],
            installPlan.ProductVersion,
            installPlan.ProductKind,
            installPlan.ChannelVersion,
            installRoot,
            installRequired,
            installedVersions,
            obsoleteVersions,
            installPlan);
    }

    internal static IReadOnlyList<string> GetInstalledVersions(string installRoot, InstallProductKind productKind)
    {
        var versions = productKind switch
        {
            InstallProductKind.Sdk => EnumerateVersionDirectories(Path.Combine(installRoot, "sdk")),
            InstallProductKind.Runtime => EnumerateVersionDirectories(Path.Combine(installRoot, "shared", "Microsoft.NETCore.App")),
            InstallProductKind.AspNetCoreRuntime => EnumerateVersionDirectories(Path.Combine(installRoot, "shared", "Microsoft.AspNetCore.App")),
            InstallProductKind.WindowsDesktop => EnumerateVersionDirectories(Path.Combine(installRoot, "shared", "Microsoft.WindowsDesktop.App")),
            _ => []
        };

        return versions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateVersionDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>();
    }

    private static bool IsInChannel(string version, string channelVersion)
    {
        if (!TryParseChannelVersion(channelVersion, out var channelMajor, out var channelMinor) ||
            !TryParseProductVersion(version, out var major, out var minor))
        {
            return false;
        }

        return major == channelMajor && minor == channelMinor;
    }

    private static bool TryParseChannelVersion(string channelVersion, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        var parts = channelVersion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 &&
               int.TryParse(parts[0], out major) &&
               int.TryParse(parts[1], out minor);
    }

    private static bool TryParseProductVersion(string version, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        var stablePart = version.Split('-', 2, StringSplitOptions.TrimEntries)[0];
        var parts = stablePart.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 &&
               int.TryParse(parts[0], out major) &&
               int.TryParse(parts[1], out minor);
    }

    private static Version ParseVersion(string version)
    {
        var stablePart = version.Split('-', 2, StringSplitOptions.TrimEntries)[0];
        return Version.TryParse(stablePart, out var parsed)
            ? parsed
            : new Version(0, 0);
    }

    internal static InstallOptions CreateInstallOptions(UpdateOptions options) =>
        new(
            Channel: "LTS",
            Quality: null,
            Version: options.Versions[0],
            Internal: false,
            GlobalJsonFile: null,
            InstallDir: options.InstallDir,
            Architecture: "<auto>",
            Runtime: options.Runtime ? "dotnet" : null,
            SharedRuntime: false,
            DryRun: false,
            Yes: true,
            NoPath: false,
            PersistPath: false,
            AzureFeed: null,
            UncachedFeed: null,
            FeedCredential: null,
            ProxyAddress: null,
            ProxyUseDefaultCredentials: false,
            ProxyBypassList: [],
            OverrideNonVersionedFiles: true,
            KeepZip: false,
            ZipPath: null,
            Verbose: false,
            RuntimeId: null,
            UserProvidedOs: null,
            DownloadTimeoutSeconds: 1200);
}
