using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.Workspace;

namespace UserSecretManager.App.ViewModels;

/// <summary>A solution or project in the sidebar tree.</summary>
public sealed partial class WorkspaceNodeViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isVisible = true;

    private WorkspaceNodeViewModel(string path, WorkspaceItemKind kind, WorkspaceItem? item, WorkspaceNodeViewModel? parent)
    {
        Path = path;
        Kind = kind;
        Item = item;
        Parent = parent;
        _isFavorite = item?.IsFavorite == true;
        Refresh();
    }

    public string Path { get; }

    public WorkspaceItemKind Kind { get; }

    /// <summary>The persisted item for root nodes; <c>null</c> for projects inside a solution.</summary>
    public WorkspaceItem? Item { get; }

    public WorkspaceNodeViewModel? Parent { get; }

    public ObservableCollection<WorkspaceNodeViewModel> Children { get; } = [];

    public string Title => System.IO.Path.GetFileNameWithoutExtension(Path);

    public string Subtitle => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public bool IsSolution => Kind == WorkspaceItemKind.Solution;

    public bool IsProject => Kind == WorkspaceItemKind.Project;

    public bool IsRoot => Parent is null;

    public bool IsMissing { get; private set; }

    public string ToolTip => IsMissing ? $"Bulunamadı: {Path}" : Path;

    public static WorkspaceNodeViewModel CreateRoot(WorkspaceItem item) => new(item.Path, item.Kind, item, null);

    /// <summary>Re-reads the solution's project list and whether the file exists.</summary>
    public void Refresh()
    {
        IsMissing = !File.Exists(Path);
        Children.Clear();
        if (IsSolution && !IsMissing)
        {
            try
            {
                foreach (var project in SolutionReader.GetProjectPaths(Path))
                {
                    Children.Add(new WorkspaceNodeViewModel(project, WorkspaceItemKind.Project, null, this));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                IsMissing = true;
            }
        }

        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(ToolTip));
    }

    /// <summary>Filters by name; a solution stays visible when any of its projects match.</summary>
    public void ApplyFilter(string[] terms)
    {
        foreach (var child in Children)
        {
            child.IsVisible = Matches(child.Title, terms);
        }

        IsVisible = Matches(Title, terms) || Children.Any(c => c.IsVisible);
        if (IsSolution && terms.Length > 0 && Children.Any(c => c.IsVisible))
        {
            IsExpanded = true;
        }

        if (IsVisible && Matches(Title, terms))
        {
            foreach (var child in Children)
            {
                child.IsVisible = true;
            }
        }
    }

    private static bool Matches(string text, string[] terms) =>
        terms.All(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));
}
