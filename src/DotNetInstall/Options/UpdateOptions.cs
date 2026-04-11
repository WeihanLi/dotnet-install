namespace DotNetInstall.Options;

internal sealed record UpdateOptions(
    string Version,
    bool Runtime,
    bool DryRun,
    string InstallDir
);
