using System.Text.Json.Serialization;

namespace DotNetInstall.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReleaseIndexDocument))]
[JsonSerializable(typeof(ReleaseDocument))]
internal sealed partial class ReleaseMetadataJsonContext : JsonSerializerContext
{
}
