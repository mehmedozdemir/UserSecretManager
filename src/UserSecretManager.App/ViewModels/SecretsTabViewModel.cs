using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Migration;

namespace UserSecretManager.App.ViewModels;

/// <summary>View and edit the project's user secrets. Edits are staged and saved together.</summary>
public sealed partial class SecretsTabViewModel : ObservableObject
{
    private readonly ProjectViewModel _project;
    private readonly List<SecretRowViewModel> _allRows = [];
    private HashSet<string> _configKeys = new(ConfigKey.Comparer);

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private bool _revealAll;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _fileExists;

    [ObservableProperty]
    private string? _loadError;

    public SecretsTabViewModel(ProjectViewModel project)
    {
        _project = project;
    }

    public ObservableCollection<SecretRowViewModel> Rows { get; } = [];

    public bool IsDirty => _allRows.Any(r => r.IsChanged);

    public bool HasErrors => _allRows.Any(r => r.KeyError is not null);

    public bool CanSave => IsDirty && !HasErrors && LoadError is null;

    public int SelectedCount => _allRows.Count(r => r.IsSelected && !r.IsNew);

    public bool HasSelection => SelectedCount > 0;

    public bool CanMoveBack => HasSelection && !IsDirty;

    public bool IsEmpty => Rows.Count == 0;

    public bool HasFilePath => FilePath.Length > 0;

    public string EmptyMessage => _allRows.Count == 0
        ? "Bu proje için henüz secret yok. Yapılandırma sekmesinden taşıyabilir veya yeni ekleyebilirsiniz."
        : "Filtreye uyan secret yok.";

    public string Summary
    {
        get
        {
            var count = _allRows.Count(r => !r.IsNew);
            var orphans = _allRows.Count(r => r.IsOrphan && !r.IsNew);
            var text = $"{count} secret";
            if (orphans > 0)
            {
                text += $" · {orphans} sahipsiz";
            }

            var pending = _allRows.Count(r => r.IsChanged);
            return pending > 0 ? $"{text} · {pending} kaydedilmemiş değişiklik" : text;
        }
    }

    public void Load(ProjectConfiguration configuration)
    {
        _configKeys = configuration.ValidFiles
            .SelectMany(f => f.Document!.Values.Select(v => v.Key))
            .ToHashSet(ConfigKey.Comparer);
        FilePath = configuration.SecretsFilePath;
        FileExists = configuration.Secrets?.Exists == true;
        LoadError = configuration.SecretsError;

        _allRows.Clear();
        foreach (var pair in configuration.Secrets ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            _allRows.Add(new SecretRowViewModel(this, pair.Key, pair.Value));
        }

        ApplyFilter();
        OnStateChanged();
    }

    public IReadOnlyList<KeyValuePair<string, string>> BuildFinalSecrets() =>
        _allRows.Where(r => !r.IsDeleted)
            .Select(r => new KeyValuePair<string, string>(r.Key.Trim(), r.Value))
            .ToList();

    public string DescribeChanges()
    {
        var added = _allRows.Count(r => r.IsNew && !r.IsDeleted);
        var modified = _allRows.Count(r => !r.IsNew && !r.IsDeleted && r.IsModified);
        var deleted = _allRows.Count(r => !r.IsNew && r.IsDeleted);
        var parts = new List<string>();
        if (added > 0)
        {
            parts.Add($"{added} eklendi");
        }

        if (modified > 0)
        {
            parts.Add($"{modified} değişti");
        }

        if (deleted > 0)
        {
            parts.Add($"{deleted} silindi");
        }

        return "Secret'lar düzenlendi: " + string.Join(", ", parts);
    }

    internal bool IsOrphanKey(string key) => !_configKeys.Contains(key.Trim());

    internal void OnStateChanged()
    {
        foreach (var row in _allRows)
        {
            row.Validate(_allRows);
        }

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanMoveBack));
        SaveCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        MoveBackCommand.NotifyCanExecuteChanged();
        _project.OnSecretsDirtyChanged();
    }

    internal void RemoveNewRow(SecretRowViewModel row)
    {
        _allRows.Remove(row);
        Rows.Remove(row);
        OnPropertyChanged(nameof(IsEmpty));
        OnStateChanged();
    }

    internal Task CopyAsync(string text) => _project.Services.Platform.CopyToClipboardAsync(text);

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnRevealAllChanged(bool value)
    {
        foreach (var row in _allRows)
        {
            row.IsRevealed = value;
        }
    }

    partial void OnFilePathChanged(string value) => OnPropertyChanged(nameof(HasFilePath));

    [RelayCommand]
    private void AddSecret()
    {
        var row = new SecretRowViewModel(this, null, null) { IsRevealed = true };
        _allRows.Add(row);
        Filter = string.Empty;
        ApplyFilter();
        OnStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => _project.SaveSecretsAsync();

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task DiscardAsync()
    {
        if (await _project.Services.Dialogs.ConfirmAsync("Değişiklikleri at",
                "Kaydedilmemiş secret değişiklikleri geri alınacak.", "Değişiklikleri at", isDestructive: true))
        {
            _project.Reload();
        }
    }

    [RelayCommand(CanExecute = nameof(CanMoveBack))]
    private Task MoveBackAsync() =>
        _project.MoveBackAsync(_allRows.Where(r => r.IsSelected && !r.IsNew).Select(r => r.OriginalKey!).ToList());

    [RelayCommand]
    private Task CopyPathAsync() => CopyAsync(FilePath);

    [RelayCommand]
    private Task OpenFolderAsync() => _project.Services.Platform.OpenFolderAsync(FilePath);

    private void ApplyFilter()
    {
        var terms = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Rows.Clear();
        foreach (var row in _allRows.Where(r => r.IsNew || terms.All(t => r.Key.Contains(t, StringComparison.OrdinalIgnoreCase))))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }
}

public sealed partial class SecretRowViewModel : ObservableObject
{
    private readonly SecretsTabViewModel _owner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified), nameof(IsChanged), nameof(StatusText), nameof(IsOrphan))]
    private string _key;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModified), nameof(IsChanged), nameof(StatusText))]
    private string _value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged), nameof(StatusText))]
    private bool _isDeleted;

    [ObservableProperty]
    private bool _isRevealed;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyError))]
    private string? _keyError;

    public SecretRowViewModel(SecretsTabViewModel owner, string? key, string? value)
    {
        _owner = owner;
        OriginalKey = key;
        OriginalValue = value;
        _key = key ?? string.Empty;
        _value = value ?? string.Empty;
    }

    public string? OriginalKey { get; }

    public string? OriginalValue { get; }

    public bool IsNew => OriginalKey is null;

    public bool IsModified => !IsNew && (!string.Equals(Key, OriginalKey, StringComparison.Ordinal) ||
                                         !string.Equals(Value, OriginalValue, StringComparison.Ordinal));

    public bool IsChanged => IsNew || IsDeleted || IsModified;

    public bool IsOrphan => Key.Trim().Length > 0 && _owner.IsOrphanKey(Key);

    public bool HasKeyError => KeyError is not null;

    public string StatusText => IsDeleted ? "Silinecek" : IsNew ? "Yeni" : IsModified ? "Değişti" : string.Empty;

    public string DeleteToolTip => IsDeleted ? "Silmeyi geri al" : "Sil";

    internal void Validate(IReadOnlyList<SecretRowViewModel> rows)
    {
        if (IsDeleted)
        {
            KeyError = null;
            return;
        }

        var key = Key.Trim();
        KeyError = key.Length == 0 ? "Anahtar boş olamaz"
            : key.StartsWith(':') || key.EndsWith(':') || key.Contains("::", StringComparison.Ordinal) ? "Geçersiz anahtar"
            : rows.Any(r => !ReferenceEquals(r, this) && !r.IsDeleted && ConfigKey.Comparer.Equals(r.Key.Trim(), key))
                ? "Bu anahtar zaten var"
                : null;
    }

    partial void OnKeyChanged(string value) => _owner.OnStateChanged();

    partial void OnValueChanged(string value) => _owner.OnStateChanged();

    partial void OnIsDeletedChanged(bool value)
    {
        OnPropertyChanged(nameof(DeleteToolTip));
        _owner.OnStateChanged();
    }

    partial void OnIsSelectedChanged(bool value) => _owner.OnStateChanged();

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;

    [RelayCommand]
    private Task CopyValueAsync() => _owner.CopyAsync(Value);

    [RelayCommand]
    private Task CopyKeyAsync() => _owner.CopyAsync(Key);

    [RelayCommand]
    private void ToggleDelete()
    {
        if (IsNew)
        {
            _owner.RemoveNewRow(this);
            return;
        }

        IsDeleted = !IsDeleted;
    }
}
