using System.Text;
using DotNetInstallManager.Options;

namespace DotNetInstallManager.Services;

internal sealed class InstallOrchestrator : IInstallOrchestrator
{
    public Task<int> ExecuteAsync(
        InstallOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("dotnet-install tool");
        builder.AppendLine($"Channel: {options.Channel} | Quality: {options.Quality ?? "<auto>"} | Version: {options.Version}");
        builder.AppendLine($"Architecture: {options.Architecture} | OS Override: {options.UserProvidedOs ?? "autodetect"}");
        builder.AppendLine($"InstallDir: {options.InstallDir} | Runtime: {options.EffectiveRuntime} | RuntimeOnly: {options.RequestsRuntimeOnly}");
        builder.AppendLine($"DryRun: {options.DryRun} | Internal: {options.Internal} | Verbose: {options.Verbose}");
        builder.AppendLine($"Feeds => Azure: {options.AzureFeed ?? "default"}, Uncached: {options.UncachedFeed ?? "none"}");
        builder.AppendLine($"Proxy: {options.ProxyAddress ?? "none"} | KeepZip: {options.KeepZip} | ZipPath: {options.ZipPath?.FullName ?? "temp"}");
        builder.AppendLine("Next step: implement network + extraction pipeline.");

        standardOut.Write(builder.ToString());
        standardError.WriteLine("Download pipeline not wired yet. This is a planning stub.");

        return Task.FromResult(0);
    }

    public Task<int> ExecuteRemovalAsync(
        RemoveOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("dotnet-install removal");
        builder.AppendLine($"InstallDir: {options.InstallDir}");
        builder.AppendLine($"Version: {options.Version} | SdkOnly: {options.SdkOnly}");
        builder.AppendLine($"Verbose: {options.Verbose}");
        builder.AppendLine("Next step: implement deletion of target SDK/runtime folders.");

        standardOut.Write(builder.ToString());
        standardError.WriteLine("Removal pipeline not wired yet. This is a planning stub.");

        return Task.FromResult(0);
    }
}
