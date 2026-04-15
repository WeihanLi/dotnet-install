namespace DotNetInstall.Options;

internal sealed record UpdateOptions(
    IReadOnlyList<string> Versions,
    bool Runtime,
    bool SdkOnly,
    bool DryRun,
    string InstallDir
)
{
    // public string Version => Versions[0];
}
