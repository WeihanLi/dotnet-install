namespace DotNetInstallManager.Options;

internal sealed record InstallOptions(
    string Channel,
    string? Quality,
    string Version,
    bool Internal,
    FileInfo? GlobalJsonFile,
    string InstallDir,
    string Architecture,
    string? Runtime,
    bool SharedRuntime,
    bool DryRun,
    bool Yes,
    bool NoPath,
    bool PersistPath,
    string? AzureFeed,
    string? UncachedFeed,
    string? FeedCredential,
    string? ProxyAddress,
    bool ProxyUseDefaultCredentials,
    IReadOnlyList<string> ProxyBypassList,
    bool OverrideNonVersionedFiles,
    bool KeepZip,
    FileInfo? ZipPath,
    bool Verbose,
    string? RuntimeId,
    string? UserProvidedOs,
    int DownloadTimeoutSeconds)
{
    public bool RequestsRuntimeOnly => SharedRuntime || !string.IsNullOrWhiteSpace(Runtime);

    public TimeSpan DownloadTimeout => TimeSpan.FromSeconds(DownloadTimeoutSeconds);

    public string EffectiveRuntime => SharedRuntime && string.IsNullOrWhiteSpace(Runtime) ? "dotnet" : Runtime ?? string.Empty;
}
