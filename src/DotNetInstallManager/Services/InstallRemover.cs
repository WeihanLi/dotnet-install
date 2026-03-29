namespace DotNetInstallManager.Services;

internal sealed record RemovalResult(
    string InstallRoot,
    string Version,
    bool SdkOnly,
    bool DryRun,
    IReadOnlyList<string> MatchedPaths,
    IReadOnlyList<string> MissingPaths);

internal sealed class InstallRemover
{
    private static readonly string[] RuntimeRelativeRoots =
    [
        Path.Combine("host", "fxr"),
        Path.Combine("shared", "Microsoft.NETCore.App"),
        Path.Combine("shared", "Microsoft.AspNetCore.App"),
        Path.Combine("shared", "Microsoft.WindowsDesktop.App")
    ];

    private readonly TextWriter _stdout;
    private readonly bool _verbose;

    public InstallRemover(TextWriter stdout, bool verbose)
    {
        _stdout = stdout;
        _verbose = verbose;
    }

    public RemovalResult RemoveVersion(string installRoot, string version, bool sdkOnly, bool dryRun, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InstallException("A version is required for removal.");
        }

        var normalizedRoot = Path.GetFullPath(installRoot);
        var matchedPaths = new List<string>();
        var missingPaths = new List<string>();

        foreach (var target in GetCandidatePaths(normalizedRoot, version.Trim(), sdkOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(target))
            {
                if (_verbose)
                {
                    _stdout.WriteLine($"Skipping missing path \"{target}\"");
                }

                missingPaths.Add(target);
                continue;
            }

            matchedPaths.Add(target);

            if (dryRun)
            {
                _stdout.WriteLine($"Would remove \"{target}\"");
                continue;
            }

            Directory.Delete(target, recursive: true);
            _stdout.WriteLine($"Removed \"{target}\"");
            TryDeleteEmptyParents(target, normalizedRoot);
        }

        if (matchedPaths.Count == 0)
        {
            throw new InstallException(
                $"No SDK or runtime directories matching version '{version}' were found under '{normalizedRoot}'.");
        }

        return new RemovalResult(normalizedRoot, version, sdkOnly, dryRun, matchedPaths, missingPaths);
    }

    private static IEnumerable<string> GetCandidatePaths(string installRoot, string version, bool sdkOnly)
    {
        yield return GetVersionPath(installRoot, "sdk", version);

        if (sdkOnly)
        {
            yield break;
        }

        foreach (var relativeRoot in RuntimeRelativeRoots)
        {
            yield return GetVersionPath(installRoot, relativeRoot, version);
        }
    }

    private static string GetVersionPath(string installRoot, string relativeRoot, string version)
    {
        var path = Path.GetFullPath(Path.Combine(installRoot, relativeRoot, version));
        EnsureWithinInstallRoot(path, installRoot);
        return path;
    }

    private static void EnsureWithinInstallRoot(string candidatePath, string installRoot)
    {
        var fullRoot = Path.GetFullPath(installRoot);
        var fullCandidate = Path.GetFullPath(candidatePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootedPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) || fullRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!string.Equals(fullCandidate, fullRoot, comparison) && !fullCandidate.StartsWith(rootedPrefix, comparison))
        {
            throw new InstallException($"Removal target '{candidatePath}' resolves outside the installation root '{installRoot}'.");
        }
    }

    private static void TryDeleteEmptyParents(string removedPath, string installRoot)
    {
        var fullRoot = Path.GetFullPath(installRoot);
        var current = Directory.GetParent(removedPath);
        while (current is not null && !PathsEqual(current.FullName, fullRoot))
        {
            if (current.EnumerateFileSystemInfos().Any())
            {
                break;
            }

            var parentPath = current.Parent?.FullName;
            current.Delete();
            current = parentPath is null ? null : new DirectoryInfo(parentPath);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
