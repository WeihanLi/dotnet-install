using System.Text.Json.Serialization;

namespace DotNetInstall.Services;

internal sealed record ReleaseIndexDocument(
    [property: JsonPropertyName("releases-index")] IReadOnlyList<ReleaseIndexEntry> Entries);

internal sealed record ReleaseIndexEntry(
    [property: JsonPropertyName("channel-version")] string ChannelVersion,
    [property: JsonPropertyName("product")] string Product,
    [property: JsonPropertyName("release-type")] string ReleaseType,
    [property: JsonPropertyName("support-phase")] string SupportPhase,
    [property: JsonPropertyName("releases.json")] string ReleasesJsonUrl,
    [property: JsonPropertyName("latest-release")] string LatestRelease,
    [property: JsonPropertyName("latest-sdk")] string LatestSdk,
    [property: JsonPropertyName("latest-runtime")] string LatestRuntime);

internal sealed record ReleaseDocument(
    [property: JsonPropertyName("channel-version")] string ChannelVersion,
    [property: JsonPropertyName("release-type")] string ReleaseType,
    [property: JsonPropertyName("support-phase")] string SupportPhase,
    [property: JsonPropertyName("releases")] IReadOnlyList<ReleaseEntry> Releases);

internal sealed record ReleaseEntry(
    [property: JsonPropertyName("release-version")] string ReleaseVersion,
    [property: JsonPropertyName("release-date")] string ReleaseDate,
    [property: JsonPropertyName("sdk")] ReleaseProduct? Sdk,
    [property: JsonPropertyName("sdks")] IReadOnlyList<ReleaseProduct>? Sdks,
    [property: JsonPropertyName("runtime")] ReleaseProduct? Runtime,
    [property: JsonPropertyName("aspnetcore-runtime")] ReleaseProduct? AspNetCoreRuntime,
    [property: JsonPropertyName("windowsdesktop")] ReleaseProduct? WindowsDesktopRuntime);

internal sealed record ReleaseProduct(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("version-display")] string? VersionDisplay,
    [property: JsonPropertyName("files")] IReadOnlyList<ReleaseFile>? Files);

internal sealed record ReleaseFile(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("rid")] string Rid,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("hash")] string? Hash);

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAsset>? Assets);

internal sealed record GitHubReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
