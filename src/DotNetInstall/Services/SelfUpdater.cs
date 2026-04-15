using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DotNetInstall.Options;

namespace DotNetInstall.Services;

internal interface ISelfUpdater : IDisposable
{
    Task<int> ExecuteAsync(
        SelfUpdateOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken);
}

internal interface ISelfUpdateApplier
{
    Task<SelfUpdateApplyResult> ApplyAsync(
        string stagedPath,
        string currentExecutablePath,
        TextWriter standardOut,
        CancellationToken cancellationToken);
}

internal sealed record SelfUpdateApplyResult(bool DeferredUntilProcessExit);

internal sealed record SelfUpdateAssetInfo(
    string Tag,
    string NormalizedVersion,
    string DownloadUrl,
    string Sha256Url);

internal sealed class SelfUpdater : ISelfUpdater
{
    private const string Repository = "WeihanLi/dotnet-install";
    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly ISelfUpdateApplier _applier;
    private readonly Func<string?> _processPathAccessor;
    private readonly Func<string> _currentVersionAccessor;
    private readonly Func<string> _runtimeIdentifierResolver;

    public SelfUpdater()
        : this(
            CreateHttpClient(),
            disposeHttpClient: true,
            new InPlaceSelfUpdateApplier(),
            static () => Environment.ProcessPath,
            GetCurrentToolVersion,
            ResolveRuntimeIdentifier)
    {
    }

    internal SelfUpdater(
        HttpClient httpClient,
        bool disposeHttpClient,
        ISelfUpdateApplier applier,
        Func<string?> processPathAccessor,
        Func<string> currentVersionAccessor,
        Func<string> runtimeIdentifierResolver)
    {
        _httpClient = httpClient;
        _disposeHttpClient = disposeHttpClient;
        _applier = applier;
        _processPathAccessor = processPathAccessor;
        _currentVersionAccessor = currentVersionAccessor;
        _runtimeIdentifierResolver = runtimeIdentifierResolver;
    }

    public async Task<int> ExecuteAsync(
        SelfUpdateOptions options,
        TextWriter standardOut,
        TextWriter standardError,
        CancellationToken cancellationToken)
    {
        var currentExecutablePath = _processPathAccessor();
        if (string.IsNullOrWhiteSpace(currentExecutablePath))
        {
            throw new InstallException("Unable to determine the current executable path for self-update.");
        }

        var runtimeIdentifier = _runtimeIdentifierResolver();
        var assetInfo = await ResolveLatestPublishedAssetAsync(runtimeIdentifier, options.Prerelease, cancellationToken);
        var currentVersion = NormalizeVersion(_currentVersionAccessor());
        var targetVersion = NormalizeVersion(assetInfo.NormalizedVersion);

        WritePlan(currentVersion, targetVersion, runtimeIdentifier, currentExecutablePath, assetInfo, options, standardOut);

        if (!string.IsNullOrWhiteSpace(currentVersion) &&
            string.Equals(currentVersion, targetVersion, StringComparison.OrdinalIgnoreCase))
        {
            standardOut.WriteLine($"dotnet-install is already up to date at version {targetVersion}.");
            return 0;
        }

        if (options.DryRun)
        {
            standardOut.WriteLine("Dry-run requested; no files were changed.");
            return 0;
        }

        var stagedPath = CreateStagingPath(currentExecutablePath);
        try
        {
            await DownloadToFileAsync(assetInfo.DownloadUrl, stagedPath, cancellationToken);
            var expectedHash = await DownloadExpectedHashAsync(assetInfo.Sha256Url, cancellationToken);
            VerifyDownloadedHash(stagedPath, expectedHash);

            var applyResult = await _applier.ApplyAsync(stagedPath, currentExecutablePath, standardOut, cancellationToken);
            if (applyResult.DeferredUntilProcessExit)
            {
                standardOut.WriteLine($"Self-update scheduled: {currentVersion} -> {targetVersion}");
                standardOut.WriteLine("The replacement will complete after the current process exits.");
            }
            else
            {
                standardOut.WriteLine($"Self-update completed: {currentVersion} -> {targetVersion}");
            }

            return 0;
        }
        catch
        {
            TryDeleteFile(stagedPath);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static string ResolveRuntimeIdentifier() => RuntimeInformation.RuntimeIdentifier;

    internal static string GetReleaseAssetName(string version, string runtimeIdentifier) =>
        runtimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase)
            ? $"dotnet-install-{version}-{runtimeIdentifier}.exe"
            : $"dotnet-install-{version}-{runtimeIdentifier}";

    internal static string? ParseHash(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (token is not null && token.Length == 64 && token.All(Uri.IsHexDigit))
            {
                return token.ToLowerInvariant();
            }
        }

        return null;
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        };

        var client = new HttpClient(handler, disposeHandler: true);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-install-self-update");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, br, deflate");
        return client;
    }

    private static string GetCurrentToolVersion() =>
        typeof(SelfUpdater).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    private static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var normalized = version.Trim().TrimStart('v', 'V');
        var buildMetadataSeparator = normalized.IndexOf('+');
        if (buildMetadataSeparator >= 0)
        {
            normalized = normalized[..buildMetadataSeparator];
        }

        return normalized;
    }

    private static string CreateStagingPath(string currentExecutablePath)
    {
        var directory = Path.GetDirectoryName(currentExecutablePath);
        var fileName = Path.GetFileName(currentExecutablePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            throw new InstallException($"Unable to create a staging path for '{currentExecutablePath}'.");
        }

        return Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.update");
    }

    private static void VerifyDownloadedHash(string filePath, string expectedHash)
    {
        var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath))).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallException($"Self-update aborted because SHA-256 verification failed for '{filePath}'.");
        }
    }

    private static void WritePlan(
        string currentVersion,
        string targetVersion,
        string runtimeIdentifier,
        string currentExecutablePath,
        SelfUpdateAssetInfo assetInfo,
        SelfUpdateOptions options,
        TextWriter standardOut)
    {
        standardOut.WriteLine("dotnet-install self-update plan");
        standardOut.WriteLine($"CurrentVersion: {currentVersion}");
        standardOut.WriteLine($"TargetVersion: {targetVersion}");
        standardOut.WriteLine($"RuntimeIdentifier: {runtimeIdentifier}");
        standardOut.WriteLine($"ExecutablePath: {currentExecutablePath}");
        standardOut.WriteLine($"DownloadUrl: {assetInfo.DownloadUrl}");
        standardOut.WriteLine($"Prerelease: {options.Prerelease}");
        standardOut.WriteLine($"DryRun: {options.DryRun}");
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private async Task<SelfUpdateAssetInfo> ResolveLatestPublishedAssetAsync(string runtimeIdentifier, bool includePrerelease, CancellationToken cancellationToken)
    {
        var release = await GetLatestPublishedReleaseAsync(runtimeIdentifier, includePrerelease, cancellationToken);
        var normalizedVersion = NormalizeVersion(release.TagName);
        var assetName = GetReleaseAssetName(normalizedVersion, runtimeIdentifier);
        var sha256Name = $"{assetName}.sha256";
        var assets = release.Assets ?? [];

        var binaryAsset = assets.FirstOrDefault(asset => string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase));
        var sha256Asset = assets.FirstOrDefault(asset => string.Equals(asset.Name, sha256Name, StringComparison.OrdinalIgnoreCase));
        if (binaryAsset is null || sha256Asset is null)
        {
            throw new InstallException($"Release '{release.TagName}' does not contain the required assets for RID '{runtimeIdentifier}'.");
        }

        return new SelfUpdateAssetInfo(release.TagName, normalizedVersion, binaryAsset.DownloadUrl, sha256Asset.DownloadUrl);
    }

    private async Task<GitHubRelease> GetLatestPublishedReleaseAsync(string runtimeIdentifier, bool includePrerelease, CancellationToken cancellationToken)
    {
        if (!includePrerelease)
        {
            var stableRelease = await TryGetLatestStableReleaseAsync(runtimeIdentifier, cancellationToken);
            if (stableRelease is not null)
            {
                return stableRelease;
            }
        }

        var releases = await GetJsonAsync(
            $"https://api.github.com/repos/{Repository}/releases?per_page=20",
            ReleaseMetadataJsonContext.Default.GitHubReleaseArray,
            cancellationToken) ?? [];

        var publishedReleases = releases.Where(release => !release.Draft).ToArray();
        if (publishedReleases.Length == 0)
        {
            throw new InstallException($"No published releases were found for '{Repository}'.");
        }

        var publishedCandidate = publishedReleases.FirstOrDefault(release =>
            HasRequiredAssets(release, runtimeIdentifier));
        if (publishedCandidate is not null)
        {
            return publishedCandidate;
        }

        throw new InstallException($"No release with matching assets were found for '{Repository}' and RID '{runtimeIdentifier}'.");
    }

    private async Task<GitHubRelease?> TryGetLatestStableReleaseAsync(string runtimeIdentifier, CancellationToken cancellationToken)
    {
        try
        {
            var release = await GetJsonAsync(
                $"https://api.github.com/repos/{Repository}/releases/latest",
                ReleaseMetadataJsonContext.Default.GitHubRelease,
                cancellationToken);
            return release is not null && !release.Draft && !release.Prerelease && HasRequiredAssets(release, runtimeIdentifier)
                ? release
                : null;
        }
        catch (InstallException)
        {
            return null;
        }
    }

    private static bool HasRequiredAssets(GitHubRelease release, string runtimeIdentifier)
    {
        var normalizedVersion = NormalizeVersion(release.TagName);
        if (string.IsNullOrWhiteSpace(normalizedVersion))
        {
            return false;
        }

        var assetName = GetReleaseAssetName(normalizedVersion, runtimeIdentifier);
        var sha256Name = $"{assetName}.sha256";
        var assetNames = new HashSet<string>((release.Assets ?? []).Select(asset => asset.Name), StringComparer.OrdinalIgnoreCase);
        return assetNames.Contains(assetName) && assetNames.Contains(sha256Name);
    }

    private async Task<T?> GetJsonAsync<T>(string url, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InstallException($"Failed to query GitHub releases from '{url}'. HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken);
    }

    private async Task DownloadToFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InstallException($"Failed to download self-update asset from '{url}'. HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = File.Create(destinationPath);
        await responseStream.CopyToAsync(destinationStream, cancellationToken);
    }

    private async Task<string> DownloadExpectedHashAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InstallException($"Failed to download self-update SHA-256 file from '{url}'. HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var hash = ParseHash(content);
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw new InstallException($"Unable to parse SHA-256 content from '{url}'.");
        }

        return hash;
    }

    private sealed class InPlaceSelfUpdateApplier : ISelfUpdateApplier
    {
        public Task<SelfUpdateApplyResult> ApplyAsync(
            string stagedPath,
            string currentExecutablePath,
            TextWriter standardOut,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (OperatingSystem.IsWindows())
            {
                ScheduleWindowsReplacement(stagedPath, currentExecutablePath);
                return Task.FromResult(new SelfUpdateApplyResult(DeferredUntilProcessExit: true));
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                PrepareUnixPermissions(stagedPath, currentExecutablePath);
                File.Move(stagedPath, currentExecutablePath, overwrite: true);
                return Task.FromResult(new SelfUpdateApplyResult(DeferredUntilProcessExit: false));
            }

            throw new InstallException("Self-update replacement is not supported on this operating system.");
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void PrepareUnixPermissions(string stagedPath, string currentExecutablePath)
        {
            try
            {
                var currentMode = File.GetUnixFileMode(currentExecutablePath);
                File.SetUnixFileMode(stagedPath, currentMode);
            }
            catch
            {
                File.SetUnixFileMode(
                    stagedPath,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead |
                    UnixFileMode.OtherExecute);
            }
        }

        private static void ScheduleWindowsReplacement(string stagedPath, string currentExecutablePath)
        {
            var scriptPath = Path.Combine(Path.GetTempPath(), $"dotnet-install-self-update-{Guid.NewGuid():N}.ps1");
            var script = $$"""
$ErrorActionPreference = 'SilentlyContinue'
$target = '{{EscapePowerShellLiteral(currentExecutablePath)}}'
$staged = '{{EscapePowerShellLiteral(stagedPath)}}'
for ($attempt = 0; $attempt -lt 120; $attempt++) {
    try {
        if (Test-Path -LiteralPath $target) {
            Remove-Item -LiteralPath $target -Force
        }
        Move-Item -LiteralPath $staged -Destination $target -Force
        exit 0
    }
    catch {
        Start-Sleep -Milliseconds 500
    }
}
exit 1
""";
            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            _ = Process.Start(startInfo)
                ?? throw new InstallException("Failed to launch the Windows self-update helper process.");
        }

        private static string EscapePowerShellLiteral(string value) =>
            value.Replace("'", "''", StringComparison.Ordinal);
    }
}
