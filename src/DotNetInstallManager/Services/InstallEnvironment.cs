using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

    public static void ConfigurePath(string installRoot, bool noPath, bool persistPath, bool verbose, bool shouldUpdatePath, TextWriter stdout)
        => ConfigurePath(
            installRoot,
            noPath,
            persistPath,
            verbose,
            shouldUpdatePath,
            stdout,
            Environment.GetEnvironmentVariable,
            Environment.SetEnvironmentVariable,
            OperatingSystem.IsWindows());

    public static bool ShouldUpdatePathForInstall(string installRoot)
        => ShouldUpdatePathForInstall(
            installRoot,
            TryResolveInstallRootFromDotNetCommand,
            Environment.GetEnvironmentVariable,
            OperatingSystem.IsWindows(),
            Directory.Exists,
            File.Exists);

    internal static bool ShouldUpdatePathForInstall(
        string installRoot,
        Func<string?> tryResolveDotNetCommandRoot,
        Func<string, string?> getEnvironmentVariable,
        bool isWindows,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists)
    {
        var comparison = isWindows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var candidates = new HashSet<string>(comparison)
        {
            Path.GetFullPath(installRoot)
        };

        AddCandidate(candidates, getEnvironmentVariable("DOTNET_INSTALL_DIR"));
        AddCandidate(candidates, getEnvironmentVariable("DOTNET_ROOT"));
        AddCandidate(candidates, tryResolveDotNetCommandRoot());

        if (isWindows)
        {
            AddCandidate(candidates, @"C:\Program Files\dotnet");
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                AddCandidate(candidates, Path.Combine(home, ".dotnet"));
            }
        }

        return !candidates.Any(path => LooksLikeDotNetRoot(path, isWindows, directoryExists, fileExists));

        static void AddCandidate(HashSet<string> candidates, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            try
            {
                candidates.Add(Path.GetFullPath(candidate));
            }
            catch
            {
                // Ignore malformed environment values and continue probing other candidates.
            }
        }
    }

    internal static void ConfigurePath(
        string installRoot,
        bool noPath,
        bool persistPath,
        bool verbose,
        bool shouldUpdatePath,
        TextWriter stdout,
        Func<string, EnvironmentVariableTarget, string?> getEnvironmentVariable,
        Action<string, string?, EnvironmentVariableTarget> setEnvironmentVariable,
        bool isWindows)
    {
        var binPath = Path.GetFullPath(installRoot);
        if (noPath)
        {
            stdout.WriteLine($"Binaries of dotnet can be found in {binPath}");
            return;
        }

        if (!shouldUpdatePath)
        {
            stdout.WriteLine($"Skipping PATH update because an existing .NET installation was detected. Binaries of dotnet can be found in {binPath}");
            return;
        }

        if (persistPath && !isWindows)
        {
            throw new InstallException("--persist-path is only supported on Windows. Configure your shell profile to persist PATH on this platform.");
        }

        var comparison = isWindows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        EnsurePathContains(
            "PATH",
            binPath,
            EnvironmentVariableTarget.Process,
            "current process PATH",
            "Adding to current process PATH",
            "This change only affects the current process.",
            verbose,
            stdout,
            getEnvironmentVariable,
            setEnvironmentVariable,
            comparison);

        if (persistPath)
        {
            EnsurePathContains(
                "PATH",
                binPath,
                EnvironmentVariableTarget.User,
                "user PATH",
                "Persisting to user PATH",
                "This change will apply to new shells.",
                verbose,
                stdout,
                getEnvironmentVariable,
                setEnvironmentVariable,
                comparison);
        }
    }

    private static void EnsurePathContains(
        string variableName,
        string binPath,
        EnvironmentVariableTarget target,
        string displayName,
        string actionPrefix,
        string note,
        bool verbose,
        TextWriter stdout,
        Func<string, EnvironmentVariableTarget, string?> getEnvironmentVariable,
        Action<string, string?, EnvironmentVariableTarget> setEnvironmentVariable,
        StringComparison comparison)
    {
        var current = getEnvironmentVariable(variableName, target) ?? string.Empty;

        foreach (var segment in SplitPath(current))
        {
            if (string.Equals(Path.GetFullPath(segment), binPath, comparison))
            {
                if (verbose)
                {
                    stdout.WriteLine($"{char.ToUpperInvariant(displayName[0])}{displayName[1..]} already contains \"{binPath}\"");
                }

                return;
            }
        }

        stdout.WriteLine($"{actionPrefix}: \"{binPath}\". Note: {note}");
        var updated = string.IsNullOrEmpty(current)
            ? binPath
            : string.Concat(binPath, Path.PathSeparator, current);
        setEnvironmentVariable(variableName, updated, target);
    }

    private static bool LooksLikeDotNetRoot(
        string path,
        bool isWindows,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists)
    {
        var dotnetExecutable = Path.Combine(path, isWindows ? "dotnet.exe" : "dotnet");
        return fileExists(dotnetExecutable) ||
               directoryExists(Path.Combine(path, "sdk")) ||
               directoryExists(Path.Combine(path, "shared")) ||
               directoryExists(Path.Combine(path, "host", "fxr"));
    }

    private static string ResolveDefaultInstallRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_INSTALL_DIR") ?? Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (TryResolveDotnetPath() is { } dotnetPath)
        {
            var candidate = Path.GetDirectoryName(dotnetPath);
            if (!string.IsNullOrEmpty(candidate))
            {
               return candidate;
            }
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

    private static string? TryResolveDotnetPath()
        => TryResolveDotnetPath(
            Environment.GetEnvironmentVariable,
            OperatingSystem.IsWindows(),
            Directory.Exists,
            File.Exists,
            UnixInteropHelper.RealPath);

    internal static string? TryResolveDotnetPath(
        Func<string, string?> getEnvironmentVariable,
        bool isWindows,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists,
        Func<string, string?> resolveRealPath)
    {
        var executableName = isWindows ? "dotnet.exe" : "dotnet";
        var commandPath = getEnvironmentVariable("PATH")?
            .Split([Path.PathSeparator], options: StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim('"'))
            .Where(p => !string.IsNullOrWhiteSpace(p)
                   && !Path.GetInvalidPathChars().Any(p.Contains)
                   && directoryExists(p)
                   )
            .Select(p => Path.Combine(p, executableName))
            .FirstOrDefault(fileExists);

        if (string.IsNullOrWhiteSpace(commandPath) || isWindows)
        {
            return commandPath;
        }

        return resolveRealPath(commandPath);
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
                .Select(p => OperatingSystem.IsWindows() ? p : UnixInteropHelper.RealPath(p)) // On Unix, resolve symlinks to get the actual location of the dotnet binary
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

file static class UnixInteropHelper
{
    // Ansi marshaling on Unix is actually UTF8
    // ReSharper disable InconsistentNaming
    private const CharSet UTF8 = CharSet.Ansi;
    private static string? PtrToStringUTF8(IntPtr ptr) => Marshal.PtrToStringAnsi(ptr);

    [DllImport("libc", CharSet = UTF8, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr realpath(string path, IntPtr buffer);

    [DllImport("libc", ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
    private static extern void free(IntPtr ptr);

    public static string? RealPath(string path)
    {
        var ptr = realpath(path, IntPtr.Zero);
        var result = PtrToStringUTF8(ptr);
        free(ptr);
        return result;
    }
}
