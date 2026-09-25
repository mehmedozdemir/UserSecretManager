using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UserSecretManager.Core.Configuration;

namespace UserSecretManager.Core.Discovery;

/// <summary>Inspects a project file and its directory.</summary>
public static partial class ProjectInspector
{
    private const int MaxSourceFilesScanned = 5000;

    private static readonly string[] SupportPackages =
    [
        "Microsoft.Extensions.Configuration.UserSecrets",
        "Microsoft.Extensions.Hosting",
        "Microsoft.AspNetCore.App",
    ];

    private static readonly string[] SupportSdkMarkers = ["Microsoft.NET.Sdk.Web", "Microsoft.NET.Sdk.Worker", "Aspire"];

    private static readonly string[] IgnoredDirectories = ["bin", "obj", "node_modules", ".git", ".vs"];

    /// <summary>Inspects the project at <paramref name="projectPath"/>.</summary>
    public static ProjectInfo Inspect(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var fullPath = Path.GetFullPath(projectPath);
        var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var project = XDocument.Load(fullPath);
        var sdk = ReadSdk(project);

        return new ProjectInfo
        {
            ProjectPath = fullPath,
            Sdk = sdk,
            SecretsId = ResolveUserSecretsId(fullPath, project),
            HasUserSecretsSupport = HasSupport(project, sdk),
            ConfigFilePaths = FindConfigFiles(directory),
            LaunchEnvironments = ReadLaunchEnvironments(directory),
            GitRoot = FindGitRoot(directory),
        };
    }

    /// <summary>Returns the appsettings files in <paramref name="directory"/>, base first.</summary>
    public static IReadOnlyList<string> FindConfigFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "appsettings*.json", SearchOption.TopDirectoryOnly)
            .Where(p => AppSettingsFile.TryGetEnvironment(Path.GetFileName(p), out _))
            .OrderBy(p => Path.GetFileName(p).Length)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ReadSdk(XDocument project)
    {
        var root = project.Root;
        if (root is null)
        {
            return null;
        }

        return (string?)root.Attribute("Sdk")
               ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "Sdk")?.Attribute("Name")?.Value;
    }

    private static bool HasSupport(XDocument project, string? sdk)
    {
        if (sdk is not null && SupportSdkMarkers.Any(m => sdk.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return project.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "FrameworkReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .Any(include => SupportPackages.Any(p => include.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
    }

    private static UserSecretsIdInfo ResolveUserSecretsId(string projectPath, XDocument project)
    {
        var fromProject = ReadIdProperty(project, projectPath, UserSecretsIdSource.ProjectFile);
        if (fromProject is not null)
        {
            return fromProject;
        }

        var directory = new DirectoryInfo(Path.GetDirectoryName(projectPath) ?? string.Empty);
        for (var current = directory; current is not null; current = current.Parent)
        {
            var props = Path.Combine(current.FullName, "Directory.Build.props");
            if (!File.Exists(props))
            {
                continue;
            }

            var fromProps = ReadIdProperty(XDocument.Load(props), props, UserSecretsIdSource.DirectoryBuildProps);
            if (fromProps is not null)
            {
                return fromProps;
            }
        }

        return FindAssemblyAttribute(directory.FullName) ?? UserSecretsIdInfo.Missing;
    }

    private static UserSecretsIdInfo? ReadIdProperty(XDocument document, string path, UserSecretsIdSource source)
    {
        var raw = document.Descendants()
            .Where(e => e.Name.LocalName == "UserSecretsId")
            .Select(e => e.Value.Trim())
            .LastOrDefault(v => v.Length > 0);

        if (raw is null)
        {
            return null;
        }

        return raw.Contains("$(", StringComparison.Ordinal) || !IsValidId(raw)
            ? new UserSecretsIdInfo(null, UserSecretsIdSource.Unresolvable, path, raw)
            : new UserSecretsIdInfo(raw, source, path, raw);
    }

    private static UserSecretsIdInfo? FindAssemblyAttribute(string directory)
    {
        foreach (var file in EnumerateSourceFiles(directory).Take(MaxSourceFilesScanned))
        {
            var match = AssemblyAttributePattern().Match(File.ReadAllText(file));
            if (match.Success && IsValidId(match.Groups["id"].Value))
            {
                var id = match.Groups["id"].Value;
                return new UserSecretsIdInfo(id, UserSecretsIdSource.AssemblyAttribute, file, id);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current, "*.cs"))
            {
                yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(current))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>A user secrets id becomes a directory name, so it must be a valid file name.</summary>
    internal static bool IsValidId(string id) =>
        id.Length > 0 && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && id.Trim() == id && id is not "." and not "..";

    private static List<string> ReadLaunchEnvironments(string directory)
    {
        var path = Path.Combine(directory, "Properties", "launchSettings.json");
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var document = JsonConfigDocument.Parse(File.ReadAllText(path));
            return document.Values
                .Where(v => v.Key.StartsWith("profiles:", StringComparison.OrdinalIgnoreCase))
                .Where(v => ConfigKey.LastSegment(v.Key) is var name &&
                            (name.Equals("ASPNETCORE_ENVIRONMENT", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("DOTNET_ENVIRONMENT", StringComparison.OrdinalIgnoreCase)))
                .Select(v => v.Value)
                .OfType<string>()
                .Where(v => v.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? FindGitRoot(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            var git = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return current.FullName;
            }
        }

        return null;
    }

    [GeneratedRegex("""\[\s*assembly\s*:\s*(?:[\w.]+\.)?UserSecretsId(?:Attribute)?\s*\(\s*"(?<id>[^"]+)"\s*\)""", RegexOptions.CultureInvariant)]
    private static partial Regex AssemblyAttributePattern();
}
