using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using DotNetInstall.Options;
using DotNetInstall.Services;

namespace DotNetInstall.Tests.Services;

public sealed class SelfUpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotnet-install-self-update-tests", Guid.NewGuid().ToString("N"));

    public SelfUpdaterTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_ResolvesLatestStableRelease_WithoutApplying()
    {
        var executablePath = CreateExecutable("dotnet-install.exe");
        var assetName = SelfUpdater.GetReleaseAssetName("1.2.3", "win-x64");
        using var httpClient = CreateHttpClient(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["https://api.github.com/repos/WeihanLi/dotnet-install/releases/latest"] = () => JsonResponse(
                $$"""
                {
                  "tag_name": "v1.2.3",
                  "draft": false,
                  "prerelease": false,
                  "assets": [
                    {
                      "name": "{{assetName}}",
                      "browser_download_url": "https://downloads.test/{{assetName}}"
                    },
                    {
                      "name": "{{assetName}}.sha256",
                      "browser_download_url": "https://downloads.test/{{assetName}}.sha256"
                    }
                  ]
                }
                """)
        });

        var applier = new FakeSelfUpdateApplier();
        using var updater = new SelfUpdater(
            httpClient,
            disposeHttpClient: false,
            applier,
            () => executablePath,
            () => "1.2.2",
            () => "win-x64");

        var output = new StringWriter();
        var exitCode = await updater.ExecuteAsync(new SelfUpdateOptions(DryRun: true), output, TextWriter.Null, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.False(applier.WasCalled);
        Assert.Contains("TargetVersion: 1.2.3", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("DryRun: True", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToPrerelease_WhenStableReleaseIsMissingAssets()
    {
        var executablePath = CreateExecutable("dotnet-install");
        var prereleaseAssetName = SelfUpdater.GetReleaseAssetName("2.0.0-rc.1", "linux-x64");
        using var httpClient = CreateHttpClient(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["https://api.github.com/repos/WeihanLi/dotnet-install/releases/latest"] = () => JsonResponse(
                """
                {
                  "tag_name": "v2.0.0",
                  "draft": false,
                  "prerelease": false,
                  "assets": []
                }
                """),
            ["https://api.github.com/repos/WeihanLi/dotnet-install/releases?per_page=20"] = () => JsonResponse(
                $$"""
                [
                  {
                    "tag_name": "v2.0.0",
                    "draft": false,
                    "prerelease": false,
                    "assets": []
                  },
                  {
                    "tag_name": "v2.0.0-rc.1",
                    "draft": false,
                    "prerelease": true,
                    "assets": [
                      {
                        "name": "{{prereleaseAssetName}}",
                        "browser_download_url": "https://downloads.test/{{prereleaseAssetName}}"
                      },
                      {
                        "name": "{{prereleaseAssetName}}.sha256",
                        "browser_download_url": "https://downloads.test/{{prereleaseAssetName}}.sha256"
                      }
                    ]
                  }
                ]
                """)
        });

        var applier = new FakeSelfUpdateApplier();
        using var updater = new SelfUpdater(
            httpClient,
            disposeHttpClient: false,
            applier,
            () => executablePath,
            () => "1.9.0",
            () => "linux-x64");

        var output = new StringWriter();
        var exitCode = await updater.ExecuteAsync(new SelfUpdateOptions(DryRun: true), output, TextWriter.Null, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.False(applier.WasCalled);
        Assert.Contains("TargetVersion: 2.0.0-rc.1", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_DownloadsVerifiesAndInvokesApplier_WhenUpdateIsAvailable()
    {
        var executablePath = CreateExecutable("dotnet-install.exe");
        var assetName = SelfUpdater.GetReleaseAssetName("1.2.3", "win-x64");
        var payload = Encoding.UTF8.GetBytes("updated executable payload");
        var expectedHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        using var httpClient = CreateHttpClient(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["https://api.github.com/repos/WeihanLi/dotnet-install/releases/latest"] = () => JsonResponse(
                $$"""
                {
                  "tag_name": "v1.2.3",
                  "draft": false,
                  "prerelease": false,
                  "assets": [
                    {
                      "name": "{{assetName}}",
                      "browser_download_url": "https://downloads.test/{{assetName}}"
                    },
                    {
                      "name": "{{assetName}}.sha256",
                      "browser_download_url": "https://downloads.test/{{assetName}}.sha256"
                    }
                  ]
                }
                """),
            [$"https://downloads.test/{assetName}"] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            },
            [$"https://downloads.test/{assetName}.sha256"] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{expectedHash}  {assetName}")
            }
        });

        var applier = new FakeSelfUpdateApplier();
        using var updater = new SelfUpdater(
            httpClient,
            disposeHttpClient: false,
            applier,
            () => executablePath,
            () => "1.2.2",
            () => "win-x64");

        var output = new StringWriter();
        var exitCode = await updater.ExecuteAsync(new SelfUpdateOptions(DryRun: false), output, TextWriter.Null, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.True(applier.WasCalled);
        Assert.Equal(executablePath, applier.CurrentExecutablePath);
        Assert.Equal(payload, applier.StagedPayload);
        Assert.Contains("Self-update completed: 1.2.2 -> 1.2.3", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsDownload_WhenCurrentVersionMatchesLatestRelease()
    {
        var executablePath = CreateExecutable("dotnet-install.exe");
        var assetName = SelfUpdater.GetReleaseAssetName("1.2.3", "win-x64");
        using var httpClient = CreateHttpClient(new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["https://api.github.com/repos/WeihanLi/dotnet-install/releases/latest"] = () => JsonResponse(
                $$"""
                {
                  "tag_name": "v1.2.3",
                  "draft": false,
                  "prerelease": false,
                  "assets": [
                    {
                      "name": "{{assetName}}",
                      "browser_download_url": "https://downloads.test/{{assetName}}"
                    },
                    {
                      "name": "{{assetName}}.sha256",
                      "browser_download_url": "https://downloads.test/{{assetName}}.sha256"
                    }
                  ]
                }
                """)
        });

        var applier = new FakeSelfUpdateApplier();
        using var updater = new SelfUpdater(
            httpClient,
            disposeHttpClient: false,
            applier,
            () => executablePath,
            () => "1.2.3+abc123",
            () => "win-x64");

        var output = new StringWriter();
        var exitCode = await updater.ExecuteAsync(new SelfUpdateOptions(DryRun: false), output, TextWriter.Null, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.False(applier.WasCalled);
        Assert.Contains("already up to date", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789  file.exe")]
    [InlineData("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789 *file.exe")]
    public void ParseHash_ReturnsHashPrefix(string content)
    {
        var hash = SelfUpdater.ParseHash(content);

        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", hash);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateExecutable(string fileName)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllText(path, "existing executable");
        return path;
    }

    private static HttpClient CreateHttpClient(IReadOnlyDictionary<string, Func<HttpResponseMessage>> responses) =>
        new(new StubHttpMessageHandler(request =>
        {
            var key = request.RequestUri?.ToString()
                ?? throw new InvalidOperationException("Request URI was null.");
            if (!responses.TryGetValue(key, out var factory))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request
                };
            }

            var response = factory();
            response.RequestMessage = request;
            return response;
        }));

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class FakeSelfUpdateApplier : ISelfUpdateApplier
    {
        public bool WasCalled { get; private set; }

        public string? CurrentExecutablePath { get; private set; }

        public byte[]? StagedPayload { get; private set; }

        public Task<SelfUpdateApplyResult> ApplyAsync(
            string stagedPath,
            string currentExecutablePath,
            TextWriter standardOut,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            CurrentExecutablePath = currentExecutablePath;
            StagedPayload = File.ReadAllBytes(stagedPath);
            return Task.FromResult(new SelfUpdateApplyResult(DeferredUntilProcessExit: false));
        }
    }

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
