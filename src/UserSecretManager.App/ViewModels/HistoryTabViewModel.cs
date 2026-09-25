using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.Core.History;

namespace UserSecretManager.App.ViewModels;

/// <summary>Operations performed on the project, each restorable from its backup.</summary>
public sealed partial class HistoryTabViewModel(ProjectViewModel project) : ObservableObject
{
    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    public bool IsEmpty => Entries.Count == 0;

    public void Load()
    {
        Entries.Clear();
        foreach (var entry in project.Services.Backups.List(project.Info.ProjectPath))
        {
            Entries.Add(new HistoryEntryViewModel(entry, this));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    internal Task RestoreAsync(BackupEntry entry) => project.RestoreBackupAsync(entry);

    internal Task OpenFolderAsync(BackupEntry entry) =>
        project.Services.Platform.OpenFolderAsync(Path.Combine(project.Services.Location.BackupsDirectory, entry.Id));

    [RelayCommand]
    private Task OpenBackupsFolderAsync() =>
        project.Services.Platform.OpenFolderAsync(project.Services.Location.BackupsDirectory);
}

public sealed partial class HistoryEntryViewModel(BackupEntry entry, HistoryTabViewModel owner)
{
    public string When { get; } = entry.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    public string Description { get; } = entry.Description;

    public string FilesText { get; } = string.Join("  ·  ", entry.Files.Select(f =>
        f.Existed ? Path.GetFileName(f.OriginalPath) : $"{Path.GetFileName(f.OriginalPath)} (yeni)"));

    [RelayCommand]
    private Task RestoreAsync() => owner.RestoreAsync(entry);

    [RelayCommand]
    private Task OpenFolderAsync() => owner.OpenFolderAsync(entry);
}
