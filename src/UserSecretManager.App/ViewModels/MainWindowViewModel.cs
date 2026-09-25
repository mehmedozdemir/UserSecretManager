using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.App.Services;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.Workspace;

namespace UserSecretManager.App.ViewModels;

/// <summary>Sidebar with remembered solutions/projects and the currently opened item.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(6);

    private readonly AppServices _services;
    private readonly WorkspaceState _state;
    private readonly DispatcherTimer _statusTimer;
    private bool _revertingSelection;

    [ObservableProperty]
    private WorkspaceNodeViewModel? _selectedNode;

    [ObservableProperty]
    private object? _currentContent;

    [ObservableProperty]
    private string _sidebarFilter = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _statusIsError;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        _state = services.Workspace.Load();
        _statusTimer = new DispatcherTimer { Interval = StatusDuration };
        _statusTimer.Tick += (_, _) =>
        {
            StatusMessage = null;
            _statusTimer.Stop();
        };

        ApplyTheme(_state.Theme);
        RebuildNodes();
        RestoreLastSelection();
    }

    public ObservableCollection<WorkspaceNodeViewModel> Nodes { get; } = [];

    public bool HasNodes => Nodes.Count > 0;

    public bool HasStatus => StatusMessage is not null;

    public string ThemeLabel => _state.Theme switch
    {
        "Light" => "Açık tema",
        "Dark" => "Koyu tema",
        _ => "Sistem teması",
    };

    public string DataDirectory => _services.Location.RootDirectory;

    /// <summary>Extra configuration files remembered for a project.</summary>
    public IReadOnlyList<string> GetCustomConfigFiles(string projectPath) =>
        _state.Projects.FirstOrDefault(p => string.Equals(p.Path, projectPath, StringComparison.OrdinalIgnoreCase))
            ?.CustomConfigFiles ?? [];

    /// <summary>Stores the extra configuration files of a project, relative to its directory when inside it.</summary>
    public void SetCustomConfigFiles(string projectPath, IEnumerable<string> files)
    {
        var directory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        var preferences = _state.GetProject(projectPath);
        var normalized = files
            .Select(f => Path.GetFullPath(f, directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(f => IsInside(f, directory) ? Path.GetRelativePath(directory, f) : f)
            .ToList();
        preferences.CustomConfigFiles.Clear();
        preferences.CustomConfigFiles.AddRange(normalized);
        Save();
    }

    private static bool IsInside(string path, string directory) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    public void ShowStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    /// <summary>Adds the given solution/project files (also used for drag and drop).</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        WorkspaceNodeViewModel? lastAdded = null;
        var skipped = 0;
        foreach (var path in paths.Select(Path.GetFullPath))
        {
            var kind = SolutionReader.IsSolution(path) ? WorkspaceItemKind.Solution
                : SolutionReader.IsProject(path) ? WorkspaceItemKind.Project
                : (WorkspaceItemKind?)null;
            if (kind is null)
            {
                skipped++;
                continue;
            }

            var existing = _state.Items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _state.Items.Add(new WorkspaceItem { Path = path, Kind = kind.Value });
            }

            lastAdded = FindNode(path);
            if (lastAdded is null)
            {
                RebuildNodes();
                lastAdded = FindNode(path);
            }
        }

        Save();
        if (skipped > 0)
        {
            ShowStatus($"{skipped} dosya desteklenmiyor (.sln, .slnx, .csproj, .fsproj, .vbproj).", isError: true);
        }

        if (lastAdded is not null)
        {
            lastAdded.IsExpanded = true;
            SelectedNode = lastAdded;
        }
    }

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    partial void OnSidebarFilterChanged(string value)
    {
        var terms = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var node in Nodes)
        {
            node.ApplyFilter(terms);
        }
    }

    partial void OnSelectedNodeChanged(WorkspaceNodeViewModel? oldValue, WorkspaceNodeViewModel? newValue)
    {
        if (_revertingSelection)
        {
            return;
        }

        if (CurrentContent is ProjectViewModel { HasUnsavedChanges: true })
        {
            _ = ConfirmSwitchAsync(oldValue, newValue);
            return;
        }

        Open(newValue);
    }

    private async Task ConfirmSwitchAsync(WorkspaceNodeViewModel? oldValue, WorkspaceNodeViewModel? newValue)
    {
        var discard = await _services.Dialogs.ConfirmAsync("Kaydedilmemiş değişiklikler",
            "Secrets sekmesinde kaydedilmemiş değişiklikler var. Başka bir öğeye geçerseniz bu değişiklikler atılacak.",
            "Değişiklikleri at", isDestructive: true);
        if (discard)
        {
            Open(newValue);
            return;
        }

        _revertingSelection = true;
        SelectedNode = oldValue;
        _revertingSelection = false;
    }

    private void Open(WorkspaceNodeViewModel? node)
    {
        (CurrentContent as IDisposable)?.Dispose();
        CurrentContent = null;
        if (node is null || node.IsMissing)
        {
            return;
        }

        if (node.IsSolution)
        {
            node.IsExpanded = true;
            CurrentContent = new SolutionOverviewViewModel(node, _services.SecretsStore, this);
            Touch(node);
            return;
        }

        try
        {
            var project = new ProjectViewModel(_services, ProjectInspector.Inspect(node.Path), this);
            project.Reload();
            project.SharedIdProjects = FindSharedIdProjects(node, project.Info.SecretsId.Id);
            CurrentContent = project;
            _state.LastProjectPath = node.Path;
            Touch(node);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            ShowStatus($"Proje açılamadı: {ex.Message}", isError: true);
        }
    }

    private static List<string> FindSharedIdProjects(WorkspaceNodeViewModel node, string? id)
    {
        if (id is null || node.Parent is null)
        {
            return [];
        }

        var shared = new List<string>();
        foreach (var sibling in node.Parent.Children.Where(c => c != node))
        {
            try
            {
                if (string.Equals(ProjectInspector.Inspect(sibling.Path).SecretsId.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    shared.Add(sibling.Title);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // An unreadable sibling cannot share the id in any way we can act on; skip it.
            }
        }

        return shared;
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var paths = await _services.Dialogs.PickSolutionOrProjectFilesAsync();
        if (paths.Count > 0)
        {
            AddPaths(paths);
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(WorkspaceNodeViewModel? node)
    {
        var root = RootOf(node);
        if (root?.Item is null)
        {
            return;
        }

        var confirmed = await _services.Dialogs.ConfirmAsync("Listeden kaldır",
            $"'{root.Title}' listeden kaldırılacak. Dosyalara ve secret'lara dokunulmaz.", "Kaldır");
        if (!confirmed)
        {
            return;
        }

        _state.Items.Remove(root.Item);
        if (SelectedNode is not null && RootOf(SelectedNode) == root)
        {
            SelectedNode = null;
        }

        Nodes.Remove(root);
        OnPropertyChanged(nameof(HasNodes));
        Save();
    }

    [RelayCommand]
    private void ToggleFavorite(WorkspaceNodeViewModel? node)
    {
        var root = RootOf(node);
        if (root?.Item is null)
        {
            return;
        }

        root.Item.IsFavorite = !root.Item.IsFavorite;
        root.IsFavorite = root.Item.IsFavorite;
        Save();
        var selected = SelectedNode;
        RebuildNodes();
        _revertingSelection = true;
        SelectedNode = selected is null ? null : FindNode(selected.Path);
        _revertingSelection = false;
    }

    [RelayCommand]
    private void RefreshNode(WorkspaceNodeViewModel? node)
    {
        var root = RootOf(node);
        if (root is null)
        {
            return;
        }

        root.Refresh();
        if (SelectedNode == root || SelectedNode?.Parent == root)
        {
            Open(SelectedNode);
        }
    }

    [RelayCommand]
    private Task OpenFolderAsync(WorkspaceNodeViewModel? node) =>
        node is null ? Task.CompletedTask : _services.Platform.OpenFolderAsync(node.Path);

    [RelayCommand]
    private Task CopyPathAsync(WorkspaceNodeViewModel? node) =>
        node is null ? Task.CompletedTask : _services.Platform.CopyToClipboardAsync(node.Path);

    [RelayCommand]
    private void CycleTheme()
    {
        _state.Theme = _state.Theme switch
        {
            "System" => "Light",
            "Light" => "Dark",
            _ => "System",
        };
        ApplyTheme(_state.Theme);
        OnPropertyChanged(nameof(ThemeLabel));
        Save();
    }

    [RelayCommand]
    private Task OpenDataFolderAsync() => _services.Platform.OpenFolderAsync(_services.Location.RootDirectory);

    private static void ApplyTheme(string theme)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    private void RebuildNodes()
    {
        var expanded = Nodes.Where(n => n.IsExpanded).Select(n => n.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Nodes.Clear();
        var ordered = _state.Items
            .OrderByDescending(i => i.IsFavorite)
            .ThenBy(i => Path.GetFileNameWithoutExtension(i.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var item in ordered)
        {
            var node = WorkspaceNodeViewModel.CreateRoot(item);
            node.IsExpanded = expanded.Contains(item.Path);
            Nodes.Add(node);
        }

        OnPropertyChanged(nameof(HasNodes));
        OnSidebarFilterChanged(SidebarFilter);
    }

    private void RestoreLastSelection()
    {
        if (_state.LastProjectPath is not { } last)
        {
            return;
        }

        var node = FindNode(last);
        if (node?.Parent is { } parent)
        {
            parent.IsExpanded = true;
        }

        SelectedNode = node;
    }

    private WorkspaceNodeViewModel? FindNode(string path) =>
        Nodes.SelectMany(n => n.Children.Prepend(n))
            .FirstOrDefault(n => string.Equals(n.Path, path, StringComparison.OrdinalIgnoreCase));

    private static WorkspaceNodeViewModel? RootOf(WorkspaceNodeViewModel? node) => node?.Parent ?? node;

    private void Touch(WorkspaceNodeViewModel node)
    {
        if (RootOf(node)?.Item is { } item)
        {
            item.LastOpenedAt = DateTimeOffset.Now;
        }

        Save();
    }

    private void Save()
    {
        try
        {
            _services.Workspace.Save(_state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Çalışma alanı kaydedilemedi: {ex.Message}", isError: true);
        }
    }
}
