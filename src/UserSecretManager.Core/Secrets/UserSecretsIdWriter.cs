using System.Text.RegularExpressions;

namespace UserSecretManager.Core.Secrets;

/// <summary>
/// Adds a <c>&lt;UserSecretsId&gt;</c> property to a project file with a text edit so the rest of the file is untouched
/// (same result as <c>dotnet user-secrets init</c>).
/// </summary>
public static partial class UserSecretsIdWriter
{
    /// <summary>Creates a new id in the format used by <c>dotnet user-secrets init</c>.</summary>
    public static string NewId() => Guid.NewGuid().ToString();

    /// <summary>Returns the project text with the id added to the first unconditional <c>PropertyGroup</c>.</summary>
    public static string AddUserSecretsId(string projectText, string userSecretsId)
    {
        ArgumentNullException.ThrowIfNull(projectText);
        ArgumentException.ThrowIfNullOrWhiteSpace(userSecretsId);

        var newLine = IO.TextFileContent.DetectNewLine(projectText);
        var group = UnconditionalPropertyGroup().Match(projectText);
        if (group.Success)
        {
            var closeIndex = group.Index + group.Length - "</PropertyGroup>".Length;
            var body = group.Groups["body"].Value;
            var memberIndent = IndentOfLastElement(body) ?? group.Groups["indent"].Value + "  ";
            var insertion = $"{memberIndent}<UserSecretsId>{userSecretsId}</UserSecretsId>{newLine}{group.Groups["indent"].Value}";
            var lineStart = projectText.LastIndexOf('\n', closeIndex - 1) + 1;
            if (!string.IsNullOrWhiteSpace(projectText[lineStart..closeIndex]))
            {
                return projectText[..closeIndex] + $"<UserSecretsId>{userSecretsId}</UserSecretsId>" +
                       projectText[closeIndex..];
            }

            return projectText[..lineStart] + insertion + projectText[closeIndex..];
        }

        var projectTag = ProjectOpenTag().Match(projectText);
        if (!projectTag.Success)
        {
            throw new InvalidOperationException("Proje dosyasında <Project> öğesi bulunamadı.");
        }

        var insertAt = projectTag.Index + projectTag.Length;
        var newGroup = $"{newLine}  <PropertyGroup>{newLine}    <UserSecretsId>{userSecretsId}</UserSecretsId>{newLine}  </PropertyGroup>";
        return projectText[..insertAt] + newGroup + projectText[insertAt..];
    }

    private static string? IndentOfLastElement(string body)
    {
        var matches = ElementIndent().Matches(body);
        return matches.Count == 0 ? null : matches[^1].Groups["indent"].Value;
    }

    [GeneratedRegex(@"(?<indent>^[ \t]*)<PropertyGroup\s*>(?<body>.*?)</PropertyGroup>", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex UnconditionalPropertyGroup();

    [GeneratedRegex(@"^(?<indent>[ \t]*)<\w", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ElementIndent();

    [GeneratedRegex(@"<Project\b[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectOpenTag();
}
