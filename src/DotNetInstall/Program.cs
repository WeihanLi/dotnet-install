using DotNetInstall.Application;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};
return await DotNetInstallHost.RunAsync(args, cancellation.Token);
