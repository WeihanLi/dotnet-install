using System.CommandLine;
using DotNetInstallManager.Cli;
using DotNetInstallManager.Services;

namespace DotNetInstallManager.Application;

internal static class DotNetInstallHost
{
    public static Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var orchestrator = new InstallOrchestrator();
        var rootCommand = InstallCommandBuilder.Build(orchestrator, cancellationToken);
        var parseResult = rootCommand.Parse(args);
        var invocationConfiguration = new InvocationConfiguration
        {
            Output = Console.Out,
            Error = Console.Error
        };

        return parseResult.InvokeAsync(invocationConfiguration, cancellationToken);
    }
}
