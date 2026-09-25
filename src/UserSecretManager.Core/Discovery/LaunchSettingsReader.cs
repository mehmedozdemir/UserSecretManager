using System.Text.Json;

namespace UserSecretManager.Core.Discovery;

/// <summary>A profile from <c>Properties/launchSettings.json</c>.</summary>
/// <param name="Name">Profile name.</param>
/// <param name="EnvironmentVariables">Environment variables the profile sets.</param>
public sealed record LaunchProfile(string Name, IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    private static readonly string[] EnvironmentVariableNames = ["ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT"];

    /// <summary>The hosting environment the profile selects, if it sets one.</summary>
    public string? Environment => EnvironmentVariableNames
        .Select(n => EnvironmentVariables.TryGetValue(n, out var value) ? value : null)
        .FirstOrDefault(v => !string.IsNullOrEmpty(v));
}

/// <summary>Reads launch profiles of a project.</summary>
public static class LaunchSettingsReader
{
    private static readonly JsonDocumentOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads the profiles in <paramref name="projectDirectory"/>; returns none if the file is missing or invalid.</summary>
    public static IReadOnlyList<LaunchProfile> Read(string projectDirectory)
    {
        var path = Path.Combine(projectDirectory, "Properties", "launchSettings.json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(IO.TextFileContent.Read(path).Text, Options);
            if (!document.RootElement.TryGetProperty("profiles", out var profiles) ||
                profiles.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return profiles.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.Object)
                .Select(p => new LaunchProfile(p.Name, ReadVariables(p.Value)))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Dictionary<string, string> ReadVariables(JsonElement profile)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (profile.TryGetProperty("environmentVariables", out var element) && element.ValueKind == JsonValueKind.Object)
        {
            foreach (var variable in element.EnumerateObject())
            {
                variables[variable.Name] = variable.Value.ValueKind == JsonValueKind.String
                    ? variable.Value.GetString() ?? string.Empty
                    : variable.Value.GetRawText();
            }
        }

        return variables;
    }
}
