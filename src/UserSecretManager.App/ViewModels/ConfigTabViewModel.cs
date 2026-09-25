using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.Core.Analysis;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Migration;

namespace UserSecretManager.App.ViewModels;

/// <summary>Key × environment matrix where the user picks the keys to move.</summary>
public sealed partial class ConfigTabViewModel : ObservableObject
{
    private readonly ProjectViewModel _project;
    private List<ConfigRowViewModel> _allRows = [];

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private bool _showSuggestedOnly;

    [ObservableProperty]
    private bool _hideMoved;

    [ObservableProperty]
    private bool _revealValues;

    public ConfigTabViewModel(ProjectViewModel project)
    {
        _project = project;
    }

    public ObservableCollection<ConfigColumnViewModel> Columns { get; } = [];

    public ObservableCollection<ConfigRowViewModel> Rows { get; } = [];

    public int SelectedCount => _allRows.Count(r => r.IsSelected);

    public int SuggestedCount => _allRows.Count(r => r.IsSuggested && r.HasFileContent);

    public bool HasSelection => SelectedCount > 0;

    public bool HasFiles => Columns.Count > 0;

    public bool IsEmpty => Rows.Count == 0;

    public string EmptyMessage => !HasFiles
        ? "Bu projede appsettings*.json dosyası bulunamadı."
        : "Filtreye uyan anahtar yok.";

    public string SelectionSummary => SelectedCount == 0
        ? $"{_allRows.Count} anahtar · {SuggestedCount} öneri"
        : $"{SelectedCount} anahtar seçili";

    public void Load(ProjectConfiguration configuration)
    {
        var previouslySelected = _allRows.Where(r => r.IsSelected).Select(r => r.Key).ToHashSet(ConfigKey.Comparer);
        var files = configuration.ValidFiles.ToList();

        Columns.Clear();
        foreach (var file in files)
        {
            Columns.Add(new ConfigColumnViewModel(file));
        }

        _allRows = configuration.BuildEntries()
            .Where(e => !e.IsSecretOnly)
            .Select(e => new ConfigRowViewModel(e, files, this, previouslySelected.Contains(e.Key)))
            .ToList();
        ApplyFilter();
    }

    internal void OnRowSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));
        MigrateCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnShowSuggestedOnlyChanged(bool value) => ApplyFilter();

    partial void OnHideMovedChanged(bool value) => ApplyFilter();

    partial void OnRevealValuesChanged(bool value)
    {
        foreach (var row in _allRows)
        {
            row.RefreshMask();
        }
    }

    [RelayCommand]
    private void SelectSuggested()
    {
        foreach (var row in _allRows.Where(r => r.IsSuggested && r.HasFileContent))
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectVisible()
    {
        foreach (var row in Rows.Where(r => r.HasFileContent))
        {
            row.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in _allRows)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MigrateAsync() =>
        _project.MigrateAsync(_allRows.Where(r => r.IsSelected).Select(r => r.Key).ToList());

    private void ApplyFilter()
    {
        var terms = Filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Rows.Clear();
        foreach (var row in _allRows.Where(r => Matches(r, terms)))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(HasFiles));
        OnRowSelectionChanged();
        OnPropertyChanged(nameof(SuggestedCount));
    }

    private bool Matches(ConfigRowViewModel row, string[] terms)
    {
        if (ShowSuggestedOnly && !row.IsSuggested)
        {
            return false;
        }

        if (HideMoved && row.IsMoved)
        {
            return false;
        }

        return terms.All(t => row.Key.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ConfigColumnViewModel(AppSettingsFile file)
{
    public string Header { get; } = file.DisplayName;

    public string FileName { get; } = file.FileName;

    public bool IsDevelopment { get; } = file.IsDevelopment;

    public bool IsOtherEnvironment { get; } = !file.AppliesToAllEnvironments && !file.IsDevelopment;

    public bool IsCustom { get; } = file.IsCustom;

    public bool HasError { get; } = file.Document is null;

    public string ToolTip { get; } = file.Document is null
        ? $"{file.FileName} okunamadı: {file.ParseError}"
        : file.IsCustom
            ? $"{file.Path} — ek dosya; tüm ortamlarda, varsayılan kaynaklardan sonra yüklendiği varsayılır"
        : file.IsBase
            ? $"{file.FileName} — tüm ortamlarda yüklenir"
            : file.IsDevelopment
                ? $"{file.FileName} — user secrets bu ortamda yüklenir"
                : $"{file.FileName} — user secrets bu ortamda varsayılan olarak yüklenmez";
}

public sealed partial class ConfigRowViewModel : ObservableObject
{
    private readonly ConfigTabViewModel _owner;

    [ObservableProperty]
    private bool _isSelected;

    public ConfigRowViewModel(ConfigEntry entry, IReadOnlyList<AppSettingsFile> files, ConfigTabViewModel owner, bool isSelected)
    {
        _owner = owner;
        _isSelected = isSelected;
        Key = entry.Key;
        HintLevel = entry.Hint.Level;
        HintText = entry.Hint.Reason;
        HasSecret = entry.HasSecret;
        HasFileContent = entry.HasFileContent;
        Cells = files.Select(f => new ConfigCellViewModel(entry, f, this)).ToList();
    }

    public string Key { get; }

    public SensitivityLevel HintLevel { get; }

    public string HintText { get; }

    public bool IsSuggested => HintLevel != SensitivityLevel.None;

    public bool IsHighHint => HintLevel == SensitivityLevel.High;

    public bool IsMediumHint => HintLevel == SensitivityLevel.Medium;

    public bool HasSecret { get; }

    public bool HasFileContent { get; }

    /// <summary>Already in secrets and no file holds a value any more.</summary>
    public bool IsMoved => HasSecret && !HasFileContent;

    /// <summary>In secrets but files still carry a value (the secret overrides it in Development).</summary>
    public bool IsOverridden => HasSecret && HasFileContent;

    public bool RevealValues => _owner.RevealValues;

    public IReadOnlyList<ConfigCellViewModel> Cells { get; }

    public void RefreshMask()
    {
        foreach (var cell in Cells)
        {
            cell.RefreshMask();
        }
    }

    partial void OnIsSelectedChanged(bool value) => _owner.OnRowSelectionChanged();
}

public sealed partial class ConfigCellViewModel : ObservableObject
{
    private readonly ConfigRowViewModel _row;
    private readonly string? _value;

    public ConfigCellViewModel(ConfigEntry entry, AppSettingsFile file, ConfigRowViewModel row)
    {
        _row = row;
        IsPresent = entry.FileValues.TryGetValue(file, out _value);
    }

    public bool IsPresent { get; }

    public bool IsEmptyValue => IsPresent && string.IsNullOrEmpty(_value);

    public bool HasValue => IsPresent && !string.IsNullOrEmpty(_value);

    public bool IsAbsent => !IsPresent;

    public string Display => !IsPresent ? "—"
        : string.IsNullOrEmpty(_value) ? "\"\""
        : _row.IsSuggested && !_row.RevealValues ? ValueMask.Mask(_value)
        : _value;

    public string? ToolTip => HasValue && (_row.RevealValues || !_row.IsSuggested) ? _value : null;

    public void RefreshMask()
    {
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(ToolTip));
    }
}
