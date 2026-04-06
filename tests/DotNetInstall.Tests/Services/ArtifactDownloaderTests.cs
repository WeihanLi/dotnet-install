using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using DotNetInstall.Options;
using DotNetInstall.Services;

namespace DotNetInstall.Tests.Services;

public sealed class ArtifactDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnet-install-tests", Guid.NewGuid().ToString("N"));

    public ArtifactDownloaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsSuccess_WhenHashMatches()
    {
        var payload = Encoding.UTF8.GetBytes("verified payload");
        var expectedHash = Convert.ToHexString(SHA512.HashData(payload)).ToLowerInvariant();
        var destinationPath = Path.Combine(_root, "dotnet-sdk.zip");
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            }));

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var downloader = new ArtifactDownloader(httpClient, stdout, stderr, verbose: true);

        var result = await downloader.DownloadAsync(
            CreatePlan(expectedHash),
            CreateOptions(destinationPath),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(destinationPath, result.DownloadPath);
        Assert.Equal("https://example.invalid/dotnet-sdk.zip", result.SourceUrl);
        Assert.True(File.Exists(destinationPath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(destinationPath));
        Assert.DoesNotContain("Hash verification failed", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsFailureAndDeletesFile_WhenHashDoesNotMatch()
    {
        var payload = Encoding.UTF8.GetBytes("tampered payload");
        var destinationPath = Path.Combine(_root, "dotnet-sdk.zip");
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            }));

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var downloader = new ArtifactDownloader(httpClient, stdout, stderr, verbose: true);

        var result = await downloader.DownloadAsync(
            CreatePlan(new string('0', 128)),
            CreateOptions(destinationPath),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.DownloadPath);
        Assert.Null(result.SourceUrl);
        Assert.False(File.Exists(destinationPath));
        Assert.Contains("Hash verification failed", stderr.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static InstallPlan CreatePlan(string? expectedHash) =>
        new(
            ChannelVersion: "10.0",
            ReleaseVersion: "10.0.5",
            ProductVersion: "10.0.201",
            ProductKind: InstallProductKind.Sdk,
            TargetRid: "win-x64",
            AssetName: "dotnet-sdk-10.0.201-win-x64.zip",
            SourceUrl: "https://example.invalid/dotnet-sdk.zip",
            CandidateUrls: ["https://example.invalid/dotnet-sdk.zip"],
            ExpectedHash: expectedHash,
            IsPreview: false);

    private static InstallOptions CreateOptions(string destinationPath) =>
        new(
            Channel: "LTS",
            Quality: null,
            Version: "latest",
            Internal: false,
            GlobalJsonFile: null,
            InstallDir: "<auto>",
            Architecture: "x64",
            Runtime: null,
            SharedRuntime: false,
            DryRun: false,
            Yes: false,
            NoPath: false,
            PersistPath: false,
            AzureFeed: null,
            UncachedFeed: null,
            FeedCredential: null,
            ProxyAddress: null,
            ProxyUseDefaultCredentials: false,
            ProxyBypassList: [],
            OverrideNonVersionedFiles: false,
            KeepZip: true,
            ZipPath: new FileInfo(destinationPath),
            Verbose: true,
            RuntimeId: null,
            UserProvidedOs: null,
            DownloadTimeoutSeconds: 30);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responseFactory(request));
    }
}
