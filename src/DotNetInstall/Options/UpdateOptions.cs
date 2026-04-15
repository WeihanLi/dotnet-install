namespace DotNetInstall.Options;

internal sealed record UpdateOptions(
    string Version,
    bool Runtime,
    bool SdkOnly,
    bool DryRun,
    string InstallDir
);
