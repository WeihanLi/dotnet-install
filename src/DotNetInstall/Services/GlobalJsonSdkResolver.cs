using System.Text.Json;

namespace DotNetInstall.Services;

internal static class GlobalJsonSdkResolver
{
    internal static string ResolveSdkVersion(FileInfo globalJsonFile)
    {
        var selection = ResolveSdkSelection(globalJsonFile);
        return ResolveVersionSelector(selection, globalJsonFile.FullName);
    }

    private static GlobalJsonSdkSelection ResolveSdkSelection(FileInfo globalJsonFile)
    {
        if (!globalJsonFile.Exists)
        {
            throw new InstallException($"Unable to find `{globalJsonFile.FullName}`.");
        }

        try
        {
            using var stream = globalJsonFile.OpenRead();
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (!TryGetProperty(document.RootElement, "sdk", out var sdkElement) ||
                sdkElement.ValueKind != JsonValueKind.Object)
            {
                throw new InstallException($"Unable to parse the SDK node in `{globalJsonFile.FullName}`.");
            }

            if (!TryGetProperty(sdkElement, "version", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.String)
            {
                throw new InstallException($"Unable to find the SDK:version node in `{globalJsonFile.FullName}`.");
            }

            var version = versionElement.GetString();
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InstallException($"Unable to find the SDK:version node in `{globalJsonFile.FullName}`.");
            }

            string? rollForward = null;
            if (TryGetProperty(sdkElement, "rollForward", out var rollForwardElement) &&
                rollForwardElement.ValueKind == JsonValueKind.String)
            {
                rollForward = rollForwardElement.GetString();
            }

            return new GlobalJsonSdkSelection(version.Trim(), rollForward?.Trim());
        }
        catch (JsonException ex)
        {
            throw new InstallException($"Unable to parse `{globalJsonFile.FullName}` as JSON: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InstallException($"Unable to read `{globalJsonFile.FullName}`: {ex.Message}");
        }
    }

    private static string ResolveVersionSelector(GlobalJsonSdkSelection selection, string globalJsonPath)
    {
        if (string.IsNullOrWhiteSpace(selection.RollForward) ||
            selection.RollForward.Equals("disable", StringComparison.OrdinalIgnoreCase) ||
            selection.RollForward.Equals("patch", StringComparison.OrdinalIgnoreCase) ||
            selection.RollForward.Equals("latestPatch", StringComparison.OrdinalIgnoreCase))
        {
            return selection.Version;
        }

        if (selection.RollForward.Equals("feature", StringComparison.OrdinalIgnoreCase) ||
            selection.RollForward.Equals("latestFeature", StringComparison.OrdinalIgnoreCase))
        {
            return ToMajorMinorSelector(selection.Version, globalJsonPath);
        }

        if (selection.RollForward.Equals("minor", StringComparison.OrdinalIgnoreCase) ||
            selection.RollForward.Equals("latestMinor", StringComparison.OrdinalIgnoreCase) ||
            selection.RollForward.Equals("major", StringComparison.OrdinalIgnoreCase) ||
            selection.RollForward.Equals("latestMajor", StringComparison.OrdinalIgnoreCase))
        {
            return ToMajorSelector(selection.Version, globalJsonPath);
        }

        throw new InstallException($"RollForward value '{selection.RollForward}' in `{globalJsonPath}` is not supported.");
    }

    private static string ToMajorMinorSelector(string version, string globalJsonPath)
    {
        var (major, minor) = ParseVersionPrefix(version, globalJsonPath);
        return $"{major}.{minor}.x";
    }

    private static string ToMajorSelector(string version, string globalJsonPath)
    {
        var (major, _) = ParseVersionPrefix(version, globalJsonPath);
        return $"{major}.x";
    }

    private static (int Major, int Minor) ParseVersionPrefix(string version, string globalJsonPath)
    {
        var stablePart = version.Split('-', 2, StringSplitOptions.TrimEntries)[0];
        var parts = stablePart.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var major) &&
            int.TryParse(parts[1], out var minor))
        {
            return (major, minor);
        }

        throw new InstallException($"SDK version '{version}' in `{globalJsonPath}` is not a valid major.minor version.");
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(name) ||
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed record GlobalJsonSdkSelection(string Version, string? RollForward);
}
