using System.Collections.Immutable;
using System.Diagnostics;

namespace DotNetInstallManager.Services;

internal static class InstallEnvironment
{
    private const string AutoToken = "<auto>";

    public static string ResolveInstallRoot(string installDir)
    {
        var candidate = string.Equals(installDir, AutoToken, StringComparison.OrdinalIgnoreCase)
            ? ResolveDefaultInstallRoot()
            : installDir;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new InstallException("Installation root cannot be empty.");
        }

        return Path.GetFullPath(candidate);
    }

    public static void ConfigurePath(string installRoot, bool noPath, bool verbose, TextWriter stdout)
    {
        var binPath = Path.GetFullPath(installRoot);
        if (noPath)
        {
            stdout.WriteLine($"Binaries of dotnet can be found in {binPath}");
            return;
        }

        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var segment in SplitPath(current))
        {
            if (string.Equals(Path.GetFullPath(segment), binPath, comparison))
            {
                if (verbose)
                {
                    stdout.WriteLine($"Current process PATH already contains \"{binPath}\"");
                }

                return;
            }
        }

        stdout.WriteLine($"Adding to current process PATH: \"{binPath}\". Note: This change only affects the current process.");
        var updated = string.IsNullOrEmpty(current)
            ? binPath
            : string.Concat(binPath, Path.PathSeparator, current);
        Environment.SetEnvironmentVariable("PATH", updated, EnvironmentVariableTarget.Process);
    }

    private static string ResolveDefaultInstallRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_INSTALL_DIR") ?? Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var discovered = TryResolveInstallRootFromDotNetCommand();
        if (!string.IsNullOrWhiteSpace(discovered) && Directory.Exists(discovered))
        {
            return discovered;
        }

        if (OperatingSystem.IsWindows())
        {
            const string defaultDotnetRoot = @"C:\Program Files\dotnet";
            if (Directory.Exists(defaultDotnetRoot))
            {
                return defaultDotnetRoot;
            }

            throw new InstallException("Unable to resolve the default installation root. Use --install-dir to specify it explicitly.");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InstallException("Unable to resolve the default installation root. Use --install-dir to specify it explicitly.");
        }

        return Path.Combine(home, ".dotnet");
    }

    private static string? TryResolveInstallRootFromDotNetCommand()
    {
        var command = OperatingSystem.IsWindows() ? "where" : "which";
        var arguments = "dotnet";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            var candidate = output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

            return string.IsNullOrWhiteSpace(candidate) ? null : Path.GetFullPath(candidate);
        }
        catch
        {
            return null;
        }
    }

    private static ImmutableArray<string> SplitPath(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        return value
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToImmutableArray();
    }
}
