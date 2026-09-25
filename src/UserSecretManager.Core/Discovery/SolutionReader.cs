using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace UserSecretManager.Core.Discovery;

/// <summary>Reads project paths from <c>.sln</c> and <c>.slnx</c> solution files.</summary>
public static partial class SolutionReader
{
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    /// <summary>Whether <paramref name="path"/> is a solution file.</summary>
    public static bool IsSolution(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> is a supported project file.</summary>
    public static bool IsProject(string path) =>
        ProjectExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the full paths of existing projects referenced by the solution, ordered by name.</summary>
    public static IReadOnlyList<string> GetProjectPaths(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(solutionPath)) ?? string.Empty;
        var relativePaths = solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ReadSlnx(solutionPath)
            : ReadSln(solutionPath);

        return relativePaths
            .Where(IsProject)
            .Select(p => Path.GetFullPath(Path.Combine(directory, NormalizeSeparators(p))))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> ReadSln(string path) =>
        File.ReadLines(path)
            .Select(line => SlnProjectLine().Match(line))
            .Where(m => m.Success)
            .Select(m => m.Groups["path"].Value);

    private static IEnumerable<string> ReadSlnx(string path) =>
        XDocument.Load(path)
            .Descendants()
            .Where(e => e.Name.LocalName == "Project")
            .Select(e => (string?)e.Attribute("Path"))
            .OfType<string>();

    private static string NormalizeSeparators(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    [GeneratedRegex("""^Project\("\{[^}]+\}"\)\s*=\s*"[^"]*"\s*,\s*"(?<path>[^"]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex SlnProjectLine();
}
