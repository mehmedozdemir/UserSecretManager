using CommunityToolkit.Mvvm.Input;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.Secrets;

namespace UserSecretManager.App.ViewModels;

/// <summary>Summary of every project in a solution: config files, secrets id and secret count.</summary>
public sealed partial class SolutionOverviewViewModel
{
    private readonly MainWindowViewModel _main;

    public SolutionOverviewViewModel(WorkspaceNodeViewModel node, UserSecretsStore store, MainWindowViewModel main)
    {
        _main = main;
        Node = node;
        Projects = node.Children.Select(c => SolutionProjectRowViewModel.Create(c, store, this)).ToList();
        var sharedGroups = Projects.Where(p => p.SecretsId is not null)
            .GroupBy(p => p.SecretsId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var group in sharedGroups)
        {
            foreach (var project in group)
            {
                project.SharedWith = group.Where(p => p != project).Select(p => p.Name).ToList();
            }
        }
    }

    public WorkspaceNodeViewModel Node { get; }

    public string Name => Node.Title;

    public string Path => Node.Path;

    public IReadOnlyList<SolutionProjectRowViewModel> Projects { get; }

    public bool IsEmpty => Projects.Count == 0;

    public string Summary
    {
        get
        {
            var withConfig = Projects.Count(p => p.ConfigFileCount > 0);
            var withSecrets = Projects.Count(p => p.SecretCount > 0);
            return $"{Projects.Count} proje · {withConfig} projede appsettings · {withSecrets} projede secret";
        }
    }

    internal void Open(WorkspaceNodeViewModel node) => _main.SelectedNode = node;
}

public sealed partial class SolutionProjectRowViewModel
{
    private readonly WorkspaceNodeViewModel _node;
    private readonly SolutionOverviewViewModel _owner;

    private SolutionProjectRowViewModel(WorkspaceNodeViewModel node, SolutionOverviewViewModel owner)
    {
        _node = node;
        _owner = owner;
    }

    public string Name => _node.Title;

    public string? SecretsId { get; private init; }

    public string SecretsIdText { get; private init; } = string.Empty;

    public int ConfigFileCount { get; private init; }

    public string EnvironmentsText { get; private init; } = string.Empty;

    public int SecretCount { get; private init; }

    public int SuggestedCount { get; private init; }

    public string? Error { get; private init; }

    public bool HasError => Error is not null;

    public IReadOnlyList<string> SharedWith { get; set; } = [];

    public bool IsShared => SharedWith.Count > 0;

    public string SharedText => IsShared ? $"Paylaşılan id: {string.Join(", ", SharedWith)}" : string.Empty;

    public static SolutionProjectRowViewModel Create(WorkspaceNodeViewModel node, UserSecretsStore store,
        SolutionOverviewViewModel owner)
    {
        try
        {
            var info = ProjectInspector.Inspect(node.Path);
            var files = info.ConfigFilePaths.Select(AppSettingsFile.Load)
                .Order(Comparer<AppSettingsFile>.Create(AppSettingsFile.CompareForDisplay))
                .ToList();
            var secretCount = 0;
            if (info.SecretsId.Id is { } id)
            {
                var path = store.GetFilePath(id);
                secretCount = File.Exists(path) ? store.Load(id).Count : 0;
            }

            var suggested = files.Where(f => f.Document is not null)
                .SelectMany(f => f.Document!.Values)
                .Where(v => v.HasContent)
                .GroupBy(v => v.Key, ConfigKey.Comparer)
                .Count(g => Core.Analysis.SensitivityAnalyzer.Analyze(g.Key, g.Select(v => v.Value)).Level
                            != Core.Analysis.SensitivityLevel.None);

            return new SolutionProjectRowViewModel(node, owner)
            {
                SecretsId = info.SecretsId.Id,
                SecretsIdText = info.SecretsId.Id ?? "—",
                ConfigFileCount = files.Count,
                EnvironmentsText = files.Count == 0 ? "appsettings yok" : string.Join(", ", files.Select(f => f.DisplayName)),
                SecretCount = secretCount,
                SuggestedCount = suggested,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or System.Text.Json.JsonException)
        {
            return new SolutionProjectRowViewModel(node, owner) { Error = ex.Message, SecretsIdText = "—" };
        }
    }

    [RelayCommand]
    private void Open() => _owner.Open(_node);
}
