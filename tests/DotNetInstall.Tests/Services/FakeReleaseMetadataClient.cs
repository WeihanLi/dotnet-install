using DotNetInstall.Services;

namespace DotNetInstall.Tests.Services;

/// <summary>
/// In-memory implementation of IReleaseMetadataClient for use in unit tests.
/// </summary>
internal sealed class FakeReleaseMetadataClient : IReleaseMetadataClient
{
    private readonly ReleaseIndexDocument _index;
    private readonly Dictionary<string, ReleaseDocument> _channels;

    public FakeReleaseMetadataClient(ReleaseIndexDocument index, Dictionary<string, ReleaseDocument> channels)
    {
        _index = index;
        _channels = channels;
    }

    public Task<ReleaseIndexDocument> GetReleaseIndexAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_index);

    public Task<ReleaseDocument> GetChannelReleaseDocumentAsync(string releasesJsonUrl, CancellationToken cancellationToken)
    {
        if (_channels.TryGetValue(releasesJsonUrl, out var doc))
        {
            return Task.FromResult(doc);
        }

        throw new InvalidOperationException($"No fake channel document registered for URL: {releasesJsonUrl}");
    }

    /// <summary>Builds a minimal release index with a single LTS entry pointing to the given url.</summary>
    public static FakeReleaseMetadataClient CreateSimple(
        string channelVersion,
        string releaseType,
        ReleaseDocument document,
        string releasesJsonUrl = "https://fake.test/releases.json")
    {
        var index = new ReleaseIndexDocument(
        [
            new ReleaseIndexEntry(
                ChannelVersion: channelVersion,
                Product: ".NET",
                ReleaseType: releaseType,
                SupportPhase: "active",
                ReleasesJsonUrl: releasesJsonUrl,
                LatestRelease: document.Releases[0].ReleaseVersion,
                LatestSdk: document.Releases[0].Sdk?.Version ?? "",
                LatestRuntime: document.Releases[0].Runtime?.Version ?? "")
        ]);

        return new FakeReleaseMetadataClient(index, new Dictionary<string, ReleaseDocument>
        {
            [releasesJsonUrl] = document
        });
    }

    /// <summary>Creates a minimal test SDK release with a single win-x64 zip file.</summary>
    public static ReleaseDocument CreateSdkReleaseDocument(
        string channelVersion,
        string releaseVersion,
        string sdkVersion,
        string runtimeVersion,
        string rid = "win-x64")
    {
        var sdkFile = new ReleaseFile(
            Name: $"dotnet-sdk-{sdkVersion}-{rid}.zip",
            Rid: rid,
            Url: $"https://builds.dotnet.microsoft.com/dotnet/Sdk/{sdkVersion}/dotnet-sdk-{sdkVersion}-{rid}.zip",
            Hash: null);

        var runtimeFile = new ReleaseFile(
            Name: $"dotnet-runtime-{runtimeVersion}-{rid}.zip",
            Rid: rid,
            Url: $"https://builds.dotnet.microsoft.com/dotnet/Runtime/{runtimeVersion}/dotnet-runtime-{runtimeVersion}-{rid}.zip",
            Hash: null);

        var sdk = new ReleaseProduct(Version: sdkVersion, VersionDisplay: sdkVersion, Files: [sdkFile]);
        var runtime = new ReleaseProduct(Version: runtimeVersion, VersionDisplay: runtimeVersion, Files: [runtimeFile]);

        var entry = new ReleaseEntry(
            ReleaseVersion: releaseVersion,
            ReleaseDate: "2024-01-01",
            Sdk: sdk,
            Sdks: [sdk],
            Runtime: runtime,
            AspNetCoreRuntime: null,
            WindowsDesktopRuntime: null);

        return new ReleaseDocument(
            ChannelVersion: channelVersion,
            ReleaseType: "lts",
            SupportPhase: "active",
            Releases: [entry]);
    }
}
