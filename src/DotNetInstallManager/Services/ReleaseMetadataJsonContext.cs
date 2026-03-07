using System.Text.Json.Serialization;

namespace DotNetInstallManager.Services;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReleaseIndexDocument))]
[JsonSerializable(typeof(ReleaseDocument))]
internal sealed partial class ReleaseMetadataJsonContext : JsonSerializerContext
{
}
