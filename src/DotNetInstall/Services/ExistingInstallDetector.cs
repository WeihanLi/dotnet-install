using System.Diagnostics;

namespace DotNetInstall.Services;

internal sealed record ExistingInstallMatch(string Source, string Location);

internal sealed record ExistingInstallDetectionResult(IReadOnlyList<ExistingInstallMatch> Matches)
{
    public bool IsInstalled => Matches.Count > 0;
}

internal static class ExistingInstallDetector
{
    public static async Task<ExistingInstallDetectionResult> DetectAsync(
        InstallPlan plan,
        string installRoot,
        CancellationToken cancellationToken)
    {
        var matches = new List<ExistingInstallMatch>();

        matches.AddRange(DetectInInstallRoot(plan, installRoot));

        if (plan.ProductKind == InstallProductKind.Sdk)
        {
            var output = await TryRunDotNetCommandAsync("--list-sdks", cancellationToken);
            if (!string.IsNullOrWhiteSpace(output))
            {
                matches.AddRange(ParseSdkMatches(output, plan.ProductVersion));
            }
        }
        else
        {
            var runtimeName = InstallVerifier.GetListRuntimeProductName(plan.ProductKind);
            var output = await TryRunDotNetCommandAsync("--list-runtimes", cancellationToken);
            if (!string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(runtimeName))
            {
                matches.AddRange(ParseRuntimeMatches(output, runtimeName, plan.ProductVersion));
            }
        }

        return new ExistingInstallDetectionResult(
            matches
                .DistinctBy(match => $"{match.Source}\0{match.Location}", StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    internal static IReadOnlyList<ExistingInstallMatch> DetectInInstallRoot(InstallPlan plan, string installRoot) =>
        InstallVerifier.GetInstalledLocations(plan, installRoot)
            .Select(path => new ExistingInstallMatch("install root", path))
            .ToList();

    internal static IReadOnlyList<ExistingInstallMatch> ParseSdkMatches(string output, string requestedVersion)
    {
        var requestedVersions = BuildRequestedVersionSet(requestedVersion);
        var matches = new List<ExistingInstallMatch>();

        foreach (var line in SplitLines(output))
        {
            var openBracketIndex = line.LastIndexOf(" [", StringComparison.Ordinal);
            if (openBracketIndex <= 0 || !line.EndsWith(']'))
            {
                continue;
            }

            var version = line[..openBracketIndex].Trim();
            if (!requestedVersions.Contains(version))
            {
                continue;
            }

            var location = line[(openBracketIndex + 2)..^1].Trim();
            if (!string.IsNullOrWhiteSpace(location))
            {
                matches.Add(new ExistingInstallMatch("dotnet --list-sdks", location));
            }
        }

        return matches;
    }

    internal static IReadOnlyList<ExistingInstallMatch> ParseRuntimeMatches(string output, string runtimeName, string requestedVersion)
    {
        var requestedVersions = BuildRequestedVersionSet(requestedVersion);
        var matches = new List<ExistingInstallMatch>();

        foreach (var line in SplitLines(output))
        {
            var openBracketIndex = line.LastIndexOf(" [", StringComparison.Ordinal);
            if (openBracketIndex <= 0 || !line.EndsWith(']'))
            {
                continue;
            }

            var descriptor = line[..openBracketIndex].Trim();
            var descriptorParts = descriptor.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (descriptorParts.Length != 2)
            {
                continue;
            }

            if (!string.Equals(descriptorParts[0], runtimeName, StringComparison.OrdinalIgnoreCase) ||
                !requestedVersions.Contains(descriptorParts[1]))
            {
                continue;
            }

            var location = line[(openBracketIndex + 2)..^1].Trim();
            if (!string.IsNullOrWhiteSpace(location))
            {
                matches.Add(new ExistingInstallMatch("dotnet --list-runtimes", location));
            }
        }

        return matches;
    }

    private static async Task<string?> TryRunDotNetCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
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

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static HashSet<string> BuildRequestedVersionSet(string requestedVersion)
    {
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            requestedVersion
        };

        if (requestedVersion.Contains("rtm", StringComparison.OrdinalIgnoreCase) ||
            requestedVersion.Contains("servicing", StringComparison.OrdinalIgnoreCase))
        {
            versions.Add(requestedVersion.Split('-', 2, StringSplitOptions.TrimEntries)[0]);
        }

        return versions;
    }

    private static IEnumerable<string> SplitLines(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
