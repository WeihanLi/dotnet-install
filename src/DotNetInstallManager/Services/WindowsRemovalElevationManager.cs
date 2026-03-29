using System.ComponentModel;
using System.Diagnostics;

namespace DotNetInstallManager.Services;

internal interface IRemovalElevationManager
{
    bool CanRetryAsAdministrator { get; }

    bool TryRunElevatedRemove(TextWriter standardOut, bool verbose, out int exitCode, out string? failureReason);
}

internal sealed class WindowsRemovalElevationManager : IRemovalElevationManager
{
    internal const string ElevatedRetryFlag = "--internal-elevated-remove-retry";

    public bool CanRetryAsAdministrator =>
        OperatingSystem.IsWindows() &&
        !Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, ElevatedRetryFlag, StringComparison.OrdinalIgnoreCase));

    public bool TryRunElevatedRemove(TextWriter standardOut, bool verbose, out int exitCode, out string? failureReason)
    {
        exitCode = 1;
        failureReason = null;

        if (!CanRetryAsAdministrator)
        {
            failureReason = "Administrator retry is not available for this process.";
            return false;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            failureReason = "Unable to resolve the current executable path.";
            return false;
        }

        if (verbose)
        {
            standardOut.WriteLine("Retrying the remove command with administrator privileges.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.CurrentDirectory
            };

            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            startInfo.ArgumentList.Add(ElevatedRetryFlag);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                failureReason = "Failed to launch the elevated remove process.";
                return false;
            }

            process.WaitForExit();
            exitCode = process.ExitCode;
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            failureReason = "The administrator elevation prompt was canceled.";
            return false;
        }
        catch (Exception ex)
        {
            failureReason = ex.Message;
            return false;
        }
    }
}
