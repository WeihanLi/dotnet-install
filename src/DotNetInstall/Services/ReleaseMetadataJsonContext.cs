using System.Text.Json.Serialization;

namespace DotNetInstall.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReleaseIndexDocument))]
[JsonSerializable(typeof(ReleaseDocument))]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubRelease[]))]
internal sealed partial class ReleaseMetadataJsonContext : JsonSerializerContext
{
}
