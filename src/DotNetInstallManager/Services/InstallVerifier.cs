namespace DotNetInstallManager.Services;

internal static class InstallVerifier
{
    public static void VerifyInstalled(InstallPlan plan, string installRoot)
    {
        if (IsInstalled(plan, installRoot, plan.ProductVersion))
        {
            return;
        }

        if (RequiresReleaseVersionFallback(plan.ProductVersion))
        {
            var releaseVersion = plan.ProductVersion.Split('-', 2, StringSplitOptions.TrimEntries)[0];
            if (IsInstalled(plan, installRoot, releaseVersion))
            {
                return;
            }
        }

        throw new InstallException(
            $"Failed to verify the installed {GetAssetDisplayName(plan.ProductKind)} version '{plan.ProductVersion}'. Installation location: {installRoot}.");
    }

    private static bool IsInstalled(InstallPlan plan, string installRoot, string version)
    {
        var packagePath = Path.Combine(installRoot, GetRelativePackagePath(plan.ProductKind), version);
        return Directory.Exists(packagePath);
    }

    private static string GetRelativePackagePath(InstallProductKind kind) =>
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

    private static string GetAssetDisplayName(InstallProductKind kind) =>
        kind switch
        {
            InstallProductKind.Sdk => ".NET SDK",
            InstallProductKind.Runtime => ".NET runtime",
            InstallProductKind.AspNetCoreRuntime => "ASP.NET Core runtime",
            InstallProductKind.WindowsDesktop => "Windows Desktop runtime",
            _ => ".NET payload"
        };
}
