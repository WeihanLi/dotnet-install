using DotNetInstall.Options;

namespace DotNetInstall.Services;

internal interface IInstallOrchestrator
{
    Task<int> ExecuteAsync(
        InstallOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken);

    Task<int> ExecuteUpdateAsync(
        UpdateOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken);

    Task<int> ExecuteSelfUpdateAsync(
        SelfUpdateOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken);

    Task<int> ExecuteRemovalAsync(
        RemoveOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken);
}
