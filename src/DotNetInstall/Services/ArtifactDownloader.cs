using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using DotNetInstall.Options;

namespace DotNetInstall.Services;

internal sealed record DownloadResult(bool Success, string? DownloadPath, string? SourceUrl)
{
    public static DownloadResult Failed { get; } = new(false, null, null);
}

internal sealed class ArtifactDownloader
{
    private const int DownloadBufferSize = 81920;
    private const int ProgressPercentStep = 10;
    private const long UnknownLengthEstimatedTotalBytes = 300L * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly bool _verbose;

    public ArtifactDownloader(HttpClient httpClient, TextWriter stdout, TextWriter stderr, bool verbose)
    {
        _httpClient = httpClient;
        _stdout = stdout;
        _stderr = stderr;
        _verbose = verbose;
    }

    public async Task<DownloadResult> DownloadAsync(InstallPlan plan, InstallOptions options, CancellationToken cancellationToken)
    {
        var destinationPath = ResolveDestinationPath(plan, options);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        foreach (var url in plan.CandidateUrls)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (_verbose)
                {
                    _stdout.WriteLine($"Attempting download from {url}");
                }

                var success = await TryDownloadAsync(url, destinationPath, cancellationToken);
                if (!success)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(plan.ExpectedHash))
                {
                    var hashValid = await VerifyHashAsync(destinationPath, plan.ExpectedHash!, cancellationToken);
                    if (!hashValid)
                    {
                        if (_verbose)
                        {
                            _stderr.WriteLine($"Hash verification failed for {destinationPath} ({url}).");
                        }

                        TryDelete(destinationPath);
                        continue;
                    }
                }

                _stdout.WriteLine($"Downloaded {plan.ProductKind} {plan.ProductVersion} ({plan.TargetRid}) to {destinationPath}");
                return new DownloadResult(true, destinationPath, url);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _stderr.WriteLine($"Download from {url} failed: {ex.Message}");
                if (_verbose)
                {
                    _stderr.WriteLine(ex.ToString());
                }
            }
        }

        return DownloadResult.Failed;
    }

    private async Task<bool> TryDownloadAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("dotnet-install-nativeaot");
        request.Headers.AcceptEncoding.ParseAdd("gzip, br, deflate");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _stderr.WriteLine($"HTTP {(int)response.StatusCode} while downloading {url}");
            return false;
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await CopyToFileWithProgressAsync(responseStream, fileStream, response.Content.Headers.ContentLength, cancellationToken);
        return true;
    }

    private async Task CopyToFileWithProgressAsync(
        Stream responseStream,
        Stream fileStream,
        long? contentLength,
        CancellationToken cancellationToken)
    {
        var totalBytes = contentLength.GetValueOrDefault();
        var hasKnownLength = totalBytes > 0;
        var progressTotalBytes = hasKnownLength ? totalBytes : UnknownLengthEstimatedTotalBytes;
        var buffer = new byte[DownloadBufferSize];
        long downloadedBytes = 0;
        var nextPercentToReport = 0;

        WriteProgress(downloadedBytes, progressTotalBytes, hasKnownLength);
        nextPercentToReport = ProgressPercentStep;

        while (true)
        {
            var bytesRead = await responseStream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            var percent = (int)(downloadedBytes * 100 / progressTotalBytes);
            if (nextPercentToReport <= 100 && percent >= nextPercentToReport)
            {
                WriteProgress(downloadedBytes, progressTotalBytes, hasKnownLength);
                nextPercentToReport = ((percent / ProgressPercentStep) + 1) * ProgressPercentStep;
            }
        }

        if (nextPercentToReport <= 100 && (downloadedBytes >= progressTotalBytes || !hasKnownLength))
        {
            WriteProgress(downloadedBytes, progressTotalBytes, hasKnownLength, 100);
        }
    }

    private void WriteProgress(long downloadedBytes, long totalBytes, bool hasKnownLength, int? percentOverride = null)
    {
        var percent = percentOverride ?? (int)(downloadedBytes * 100 / totalBytes);
        if (!hasKnownLength && percentOverride is null)
        {
            percent = Math.Min(percent, 99);
        }

        var downloadedDisplay = hasKnownLength
            ? FormatBytes(Math.Min(downloadedBytes, totalBytes))
            : FormatBytes(downloadedBytes);
        var totalDisplay = hasKnownLength ? FormatBytes(totalBytes) : $"~{FormatBytes(totalBytes)}";
        _stdout.WriteLine($"Download progress: {percent}% ({downloadedDisplay} / {totalDisplay})");
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{bytes} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }

    private static async Task<bool> VerifyHashAsync(string path, string expectedHash, CancellationToken cancellationToken)
    {
        var normalized = NormalizeHash(expectedHash);
        await using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha512 = SHA512.Create();
        var actualBytes = await sha512.ComputeHashAsync(fileStream, cancellationToken);
        var actual = Convert.ToHexString(actualBytes);
        return actual.Equals(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHash(string hash)
    {
        var builder = new StringBuilder(hash.Length);
        foreach (var c in hash)
        {
            if (c == '-' || char.IsWhiteSpace(c))
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    private static string ResolveDestinationPath(InstallPlan plan, InstallOptions options)
    {
        if (options.ZipPath is { } explicitPath)
        {
            return explicitPath.FullName;
        }

        var folder = Path.Combine(Path.GetTempPath(), "dotnet-install");
        var extension = plan.AssetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? ".tar.gz"
            : Path.GetExtension(plan.AssetName);
        var fileName = $"{plan.ProductKind.ToString().ToLowerInvariant()}-{plan.ProductVersion}-{plan.TargetRid}-{Guid.NewGuid():N}{extension}";
        return Path.Combine(folder, fileName);
    }
}
