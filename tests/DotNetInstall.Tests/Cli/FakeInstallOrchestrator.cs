using DotNetInstall.Options;
using DotNetInstall.Services;

namespace DotNetInstall.Tests.Cli;

internal sealed class FakeInstallOrchestrator : IInstallOrchestrator
{
    public List<InstallOptions> InstallCalls { get; } = [];
    public List<RemoveOptions> RemoveCalls { get; } = [];

    public Task<int> ExecuteAsync(
        InstallOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        InstallCalls.Add(options);
        return Task.FromResult(0);
    }

    public Task<int> ExecuteRemovalAsync(
        RemoveOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        RemoveCalls.Add(options);
        return Task.FromResult(0);
    }
}
