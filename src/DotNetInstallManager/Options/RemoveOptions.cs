namespace DotNetInstallManager.Options;

internal sealed record RemoveOptions(
    string Version,
    string? InstallDir,
    bool SdkOnly,
    bool Verbose
);
