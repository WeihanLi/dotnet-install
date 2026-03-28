namespace DotNetInstallManager.Services;

internal interface IReleaseMetadataClient
{
    Task<ReleaseIndexDocument> GetReleaseIndexAsync(CancellationToken cancellationToken);

    Task<ReleaseDocument> GetChannelReleaseDocumentAsync(string releasesJsonUrl, CancellationToken cancellationToken);
}
