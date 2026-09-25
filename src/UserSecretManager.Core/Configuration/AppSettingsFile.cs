using System.Text.RegularExpressions;
using UserSecretManager.Core.IO;

namespace UserSecretManager.Core.Configuration;

/// <summary>
/// An <c>appsettings*.json</c> file of a project, loaded with its parsed document (if it is valid JSON).
/// </summary>
public sealed partial class AppSettingsFile
{
    /// <summary>The environment name for which the host loads user secrets by default.</summary>
    public const string DevelopmentEnvironment = "Development";

    private AppSettingsFile(string path, string? environment, TextFileContent content,
        JsonConfigDocument? document, string? parseError)
    {
        Path = path;
        Environment = environment;
        Content = content;
        Document = document;
        ParseError = parseError;
    }

    /// <summary>Full path of the file.</summary>
    public string Path { get; }

    /// <summary>File name without directory.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Environment name (<c>Development</c>, <c>Production</c>...) or <c>null</c> for the base file.</summary>
    public string? Environment { get; }

    /// <summary>Whether this is <c>appsettings.json</c>, which every environment loads.</summary>
    public bool IsBase => Environment is null;

    /// <summary>Whether this file belongs to the Development environment.</summary>
    public bool IsDevelopment => IsDevelopmentName(Environment);

    /// <summary>Short label for display: "Base" or the environment name.</summary>
    public string DisplayName => Environment ?? "Base";

    /// <summary>Raw content as read from disk.</summary>
    public TextFileContent Content { get; }

    /// <summary>Parsed document; <c>null</c> when the file is not valid JSON.</summary>
    public JsonConfigDocument? Document { get; }

    /// <summary>Parse error message when <see cref="Document"/> is <c>null</c>.</summary>
    public string? ParseError { get; }

    /// <summary>Whether <paramref name="environment"/> is the Development environment.</summary>
    public static bool IsDevelopmentName(string? environment) =>
        string.Equals(environment, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns whether <paramref name="fileName"/> is an appsettings file and, if so, its environment name.
    /// </summary>
    public static bool TryGetEnvironment(string fileName, out string? environment)
    {
        var match = FileNamePattern().Match(fileName);
        environment = match.Success && match.Groups[1].Success ? match.Groups[1].Value : null;
        return match.Success;
    }

    /// <summary>Loads and parses the file.</summary>
    public static AppSettingsFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!TryGetEnvironment(System.IO.Path.GetFileName(path), out var environment))
        {
            throw new ArgumentException($"'{path}' bir appsettings dosyası değil.", nameof(path));
        }

        var content = TextFileContent.Read(path);
        _ = JsonConfigDocument.TryParse(content.Text, out var document, out var error);
        return new AppSettingsFile(path, environment, content, document, error);
    }

    /// <summary>Sort order: base first, Development second, the rest alphabetically.</summary>
    public static int CompareForDisplay(AppSettingsFile? left, AppSettingsFile? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null || right is null)
        {
            return left is null ? -1 : 1;
        }

        var rank = Rank(left).CompareTo(Rank(right));
        return rank != 0
            ? rank
            : string.Compare(left.Environment, right.Environment, StringComparison.OrdinalIgnoreCase);

        static int Rank(AppSettingsFile file) => file.IsBase ? 0 : file.IsDevelopment ? 1 : 2;
    }

    [GeneratedRegex(@"^appsettings(?:\.(.+))?\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();
}
