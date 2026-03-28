using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DotNetInstallManager.Services;

internal sealed class ReleaseMetadataClient : IReleaseMetadataClient
{
    private static readonly Uri ReleaseIndexUri = new("https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json");

    private readonly HttpClient _httpClient;

    public ReleaseMetadataClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ReleaseIndexDocument> GetReleaseIndexAsync(CancellationToken cancellationToken) =>
        await FetchAsync(
            ReleaseIndexUri,
            ReleaseMetadataJsonContext.Default.ReleaseIndexDocument,
            cancellationToken)
        ?? throw new InstallException("Failed to read releases-index metadata.");

    public async Task<ReleaseDocument> GetChannelReleaseDocumentAsync(string releasesJsonUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(releasesJsonUrl))
        {
            throw new InstallException("The releases.json url is not provided.");
        }

        var uri = new Uri(releasesJsonUrl);
        return await FetchAsync(
                uri,
                ReleaseMetadataJsonContext.Default.ReleaseDocument,
                cancellationToken)
            ?? throw new InstallException($"Failed to read channel release metadata from {releasesJsonUrl}.");
    }

    private async Task<T?> FetchAsync<T>(Uri uri, JsonTypeInfo<T> jsonTypeInfo, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken);
    }
}
