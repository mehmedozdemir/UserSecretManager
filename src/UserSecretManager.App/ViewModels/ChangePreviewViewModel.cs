using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Migration;

namespace UserSecretManager.App.ViewModels;

/// <summary>Diff preview of a change set, one file at a time.</summary>
public sealed partial class ChangePreviewViewModel : ObservableObject
{
    [ObservableProperty]
    private FileDiffViewModel? _selectedFile;

    public ObservableCollection<FileDiffViewModel> Files { get; } = [];

    public bool IsEmpty => Files.Count == 0;

    public void Load(ChangeSet? changeSet)
    {
        var previous = SelectedFile?.Path;
        Files.Clear();
        foreach (var change in changeSet?.Changes ?? [])
        {
            Files.Add(new FileDiffViewModel(change));
        }

        SelectedFile = Files.FirstOrDefault(f => f.Path == previous) ?? Files.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed class FileDiffViewModel(FileChange change)
{
    public string Path { get; } = change.Path;

    public string DisplayName { get; } = change.DisplayName;

    public string KindLabel { get; } = change.Kind switch
    {
        FileChangeKind.Secrets => "User Secrets",
        FileChangeKind.ProjectFile => "Proje dosyası",
        _ => "appsettings",
    };

    public bool IsNewFile { get; } = change.IsNewFile;

    public IReadOnlyList<DiffLineViewModel> Lines { get; } =
        LineDiff.Compute(change.OriginalText ?? string.Empty, change.NewContent.Text)
            .Select(l => new DiffLineViewModel(l))
            .ToList();

    public int AddedCount => Lines.Count(l => l.IsAdded);

    public int RemovedCount => Lines.Count(l => l.IsRemoved);
}

public sealed class DiffLineViewModel(DiffLine line)
{
    public string OldNumber { get; } = line.OldNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public string NewNumber { get; } = line.NewNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public string Marker { get; } = line.Kind switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "−",
        _ => " ",
    };

    public string Text { get; } = line.Text;

    public bool IsAdded { get; } = line.Kind == DiffLineKind.Added;

    public bool IsRemoved { get; } = line.Kind == DiffLineKind.Removed;

    public bool IsGap { get; } = line.Kind == DiffLineKind.Gap;
}
