using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.IO;
using UserSecretManager.Core.Migration;
using UserSecretManager.Core.Transfer;

namespace UserSecretManager.App.ViewModels;

/// <summary>View and edit the project's user secrets. Edits are staged and saved together.</summary>
public sealed partial class SecretsTabViewModel : ObservableObject
{
    private readonly ProjectViewModel _project;
    private readonly List<SecretRowViewModel> _allRows = [];
    private HashSet<string> _configKeys = new(ConfigKey.Comparer);
    private ProjectConfiguration? _configuration;

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

    public bool HasTemplate => _configuration is not null &&
                               File.Exists(SecretsTemplate.PathFor(_configuration.Project.Directory));

    public void Load(ProjectConfiguration configuration)
    {
        _configuration = configuration;
        OnPropertyChanged(nameof(HasTemplate));
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

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialogs = _project.Services.Dialogs;
        if (IsDirty)
        {
            await dialogs.ShowMessageAsync("Dışa aktar", "Önce Secrets sekmesindeki değişiklikleri kaydedin veya atın.");
            return;
        }

        if (_configuration?.Secrets is not { Count: > 0 } secrets)
        {
            await dialogs.ShowMessageAsync("Dışa aktar", "Dışa aktarılacak secret yok.");
            return;
        }

        var password = new PasswordDialogViewModel("Dışa aktarım parolası",
            $"{secrets.Count} secret bu parolayla şifrelenecek. Dosyayı alan kişiye parolayı ayrı bir kanaldan iletin.",
            requireConfirmation: true, SecretsArchive.ValidatePassword);
        if (!await dialogs.ShowDialogAsync(password))
        {
            return;
        }

        var path = await dialogs.PickSaveFileAsync("Secret'ları dışa aktar",
            _configuration.Project.Name + SecretsArchive.FileExtension, "User Secret Manager dışa aktarımı",
            SecretsArchive.FileExtension);
        if (path is null)
        {
            return;
        }

        var content = new SecretsArchiveContent(_configuration.Project.Name, _configuration.Project.SecretsId.Id,
            DateTimeOffset.Now, secrets.ToList());
        try
        {
            var data = await Task.Run(() => SecretsArchive.Export(content, password.Password));
            AtomicFile.WriteAllBytes(path, data);
            _project.ShowStatus($"{secrets.Count} secret dışa aktarıldı: {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await dialogs.ShowMessageAsync("Dışa aktarılamadı", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var dialogs = _project.Services.Dialogs;
        if (LoadError is not null || _configuration?.Secrets is null || !await _project.EnsureNoUnsavedSecretsAsync())
        {
            return;
        }

        var path = await dialogs.PickOpenFileAsync("Dışa aktarım dosyasını seçin", "User Secret Manager dışa aktarımı",
            SecretsArchive.FileExtension);
        if (path is null)
        {
            return;
        }

        var password = new PasswordDialogViewModel("Parola", $"{Path.GetFileName(path)} dosyasının parolasını girin.",
            requireConfirmation: false, p => p.Length == 0 ? "Parola gerekli" : null);
        if (!await dialogs.ShowDialogAsync(password))
        {
            return;
        }

        SecretsArchiveContent content;
        try
        {
            var data = await File.ReadAllBytesAsync(path);
            content = await Task.Run(() => SecretsArchive.Import(data, password.Password));
        }
        catch (Exception ex) when (ex is SecretsArchiveException or IOException or UnauthorizedAccessException)
        {
            await dialogs.ShowMessageAsync("İçe aktarılamadı", ex.Message);
            return;
        }

        var details = string.Create(CultureInfo.InvariantCulture,
            $"Kaynak: {content.ProjectName ?? "bilinmiyor"} · {content.ExportedAt.ToLocalTime():dd.MM.yyyy HH:mm} · {content.Secrets.Count} secret");
        var ownId = _configuration.Project.SecretsId.Id;
        var warning = content.UserSecretsId is { } id && ownId is not null && !string.Equals(id, ownId, StringComparison.OrdinalIgnoreCase)
            ? $"Dosya farklı bir UserSecretsId'den ({id}) dışa aktarılmış. Doğru projeye aktardığınızdan emin olun."
            : null;
        var dialog = new ApplySecretsViewModel("Secret'ları içe aktar", "içe aktarılan dosya", content.Secrets,
            _configuration, _project.Services.ChangeSets, details, warning);
        if (await dialogs.ShowDialogAsync(dialog) && dialog.ChangeSet is { IsEmpty: false } changeSet)
        {
            await _project.ApplyChangesAsync(changeSet, $"{content.Secrets.Count} secret içe aktarıldı.");
        }
    }

    [RelayCommand]
    private async Task WriteTemplateAsync()
    {
        var dialogs = _project.Services.Dialogs;
        if (_configuration is null || !await _project.EnsureNoUnsavedSecretsAsync())
        {
            return;
        }

        ChangeSet changeSet;
        try
        {
            changeSet = ChangeSetFactory.WriteTemplate(_configuration);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            await dialogs.ShowMessageAsync("Şablon oluşturulamadı", ex.Message);
            return;
        }

        if (changeSet.IsEmpty)
        {
            _project.ShowStatus($"{SecretsTemplate.FileName} zaten güncel.");
            return;
        }

        var confirm = new ConfirmChangesViewModel($"{SecretsTemplate.FileName} oluştur",
            "Şablon yalnızca anahtar adlarını içerir, değerler boştur. Repoya eklenerek ekibe hangi secret'ların gerektiğini gösterir.",
            changeSet, "Oluştur");
        if (await dialogs.ShowDialogAsync(confirm))
        {
            await _project.ApplyChangesAsync(changeSet, $"{SecretsTemplate.FileName} güncellendi.");
        }
    }

    [RelayCommand]
    private async Task FillFromTemplateAsync()
    {
        if (_configuration is null || LoadError is not null)
        {
            return;
        }

        IReadOnlyList<string> keys;
        try
        {
            keys = SecretsTemplate.ReadKeys(TextFileContent.Read(SecretsTemplate.PathFor(_configuration.Project.Directory)).Text);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            await _project.Services.Dialogs.ShowMessageAsync("Şablon okunamadı", ex.Message);
            return;
        }

        var existing = _allRows.Where(r => !r.IsDeleted).Select(r => r.Key.Trim()).ToHashSet(ConfigKey.Comparer);
        var missing = keys.Where(k => !existing.Contains(k)).ToList();
        foreach (var key in missing)
        {
            _allRows.Add(new SecretRowViewModel(this, null, null) { Key = key, IsRevealed = true });
        }

        Filter = string.Empty;
        ApplyFilter();
        OnStateChanged();
        _project.ShowStatus(missing.Count == 0
            ? "Şablondaki tüm anahtarlar zaten tanımlı."
            : $"Şablondan {missing.Count} eksik anahtar eklendi; değerlerini girip kaydedin.");
    }

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
        KeyError = ConfigKey.Validate(key)
                   ?? (rows.Any(r => !ReferenceEquals(r, this) && !r.IsDeleted && ConfigKey.Comparer.Equals(r.Key.Trim(), key))
                       ? "Bu anahtar zaten var"
                       : null);
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
