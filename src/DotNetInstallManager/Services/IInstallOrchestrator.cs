using DotNetInstallManager.Options;

namespace DotNetInstallManager.Services;

internal interface IInstallOrchestrator
{
    Task<int> ExecuteAsync(
        InstallOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken);

    Task<int> ExecuteRemovalAsync(
        RemoveOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken);
}
