namespace DotNetInstall.Services;

internal static class InstallVerifier
{
    public static void VerifyInstalled(InstallPlan plan, string installRoot)
    {
        if (GetInstalledLocations(plan, installRoot).Count > 0)
        {
            return;
        }

        throw new InstallException(
            $"Failed to verify the installed {GetAssetDisplayName(plan.ProductKind)} version '{plan.ProductVersion}'. Installation location: {installRoot}.");
    }

    internal static IReadOnlyList<string> GetInstalledLocations(InstallPlan plan, string installRoot) =>
        GetExpectedInstalledVersions(plan)
            .Select(version => Path.Combine(installRoot, GetRelativePackagePath(plan.ProductKind), version))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static IReadOnlyList<string> GetExpectedInstalledVersions(InstallPlan plan)
    {
        var versions = new List<string> { plan.ProductVersion };
        if (RequiresReleaseVersionFallback(plan.ProductVersion))
        {
            versions.Add(plan.ProductVersion.Split('-', 2, StringSplitOptions.TrimEntries)[0]);
        }

        return versions
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string GetRelativePackagePath(InstallProductKind kind) =>
        kind switch
        {
            InstallProductKind.Sdk => "sdk",
            InstallProductKind.Runtime => Path.Combine("shared", "Microsoft.NETCore.App"),
            InstallProductKind.AspNetCoreRuntime => Path.Combine("shared", "Microsoft.AspNetCore.App"),
            InstallProductKind.WindowsDesktop => Path.Combine("shared", "Microsoft.WindowsDesktop.App"),
            _ => throw new InstallException("Unsupported product kind.")
        };

    private static bool RequiresReleaseVersionFallback(string version) =>
        version.Contains("rtm", StringComparison.OrdinalIgnoreCase) ||
        version.Contains("servicing", StringComparison.OrdinalIgnoreCase);

    internal static string GetAssetDisplayName(InstallProductKind kind) =>
        kind switch
        {
            InstallProductKind.Sdk => ".NET SDK",
            InstallProductKind.Runtime => ".NET runtime",
            InstallProductKind.AspNetCoreRuntime => "ASP.NET Core runtime",
            InstallProductKind.WindowsDesktop => "Windows Desktop runtime",
            _ => ".NET payload"
        };

    internal static string? GetListRuntimeProductName(InstallProductKind kind) =>
        kind switch
        {
            InstallProductKind.Sdk => null,
            InstallProductKind.Runtime => "Microsoft.NETCore.App",
            InstallProductKind.AspNetCoreRuntime => "Microsoft.AspNetCore.App",
            InstallProductKind.WindowsDesktop => "Microsoft.WindowsDesktop.App",
            _ => null
        };
}
