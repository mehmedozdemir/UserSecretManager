using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Migration;
using UserSecretManager.Core.Profiles;

namespace UserSecretManager.App.ViewModels;

/// <summary>Where a new profile's initial values come from.</summary>
public enum ProfileSource
{
    CurrentSecrets,
    Environment,
    OtherProfile,
    Empty,
}

/// <summary>
/// Creates or edits a profile. Values are edited in a table and saved only to the profile; secrets.json is not touched
/// until the profile is applied.
/// </summary>
public sealed partial class ProfileEditorViewModel : DialogViewModelBase
{
    private readonly ProjectConfiguration _configuration;
    private readonly Func<string, IReadOnlyList<KeyValuePair<string, string>>> _loadProfile;
    private readonly Func<string, bool> _profileExists;
    private bool _filling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError), nameof(WillOverwrite))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFromCurrentSecrets), nameof(IsFromEnvironment), nameof(IsFromOtherProfile), nameof(IsEmptySource))]
    private ProfileSource _source = ProfileSource.CurrentSecrets;

    [ObservableProperty]
    private string _environment;

    [ObservableProperty]
    private string? _otherProfile;

    [ObservableProperty]
    private string? _sourceNote;

    [ObservableProperty]
    private bool _isEdited;

    [ObservableProperty]
    private bool _revealAll;

    private ProfileEditorViewModel(ProjectConfiguration configuration, IReadOnlyList<string> profileNames,
        Func<string, IReadOnlyList<KeyValuePair<string, string>>> loadProfile, Func<string, bool> profileExists,
        string? originalName)
    {
        _configuration = configuration;
        _loadProfile = loadProfile;
        _profileExists = profileExists;
        OriginalName = originalName;
        _name = originalName ?? string.Empty;
        OtherProfiles = profileNames;
        _otherProfile = profileNames.Count > 0 ? profileNames[0] : null;
        Environments = configuration.Files.Select(f => f.Environment).OfType<string>()
            .Concat(configuration.Project.LaunchEnvironments)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _environment = Environments.FirstOrDefault(e => !AppSettingsFile.IsDevelopmentName(e))
                       ?? (Environments.Count > 0 ? Environments[0] : "Production");
        Rows.CollectionChanged += (_, _) => OnRowsChanged();
    }

    /// <summary>Editor for a new profile, pre-filled from the current secrets.</summary>
    public static ProfileEditorViewModel ForNew(ProjectConfiguration configuration, IReadOnlyList<string> profileNames,
        Func<string, IReadOnlyList<KeyValuePair<string, string>>> loadProfile, Func<string, bool> profileExists)
    {
        var editor = new ProfileEditorViewModel(configuration, profileNames, loadProfile, profileExists, originalName: null);
        editor.FillFromSource();
        return editor;
    }

    /// <summary>Editor for an existing profile.</summary>
    public static ProfileEditorViewModel ForExisting(ProjectConfiguration configuration, IReadOnlyList<string> profileNames,
        Func<string, IReadOnlyList<KeyValuePair<string, string>>> loadProfile, Func<string, bool> profileExists,
        string name, string? description, IReadOnlyList<KeyValuePair<string, string>> values)
    {
        var editor = new ProfileEditorViewModel(configuration,
            profileNames.Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase)).ToList(),
            loadProfile, profileExists, name)
        {
            Description = description,
        };
        editor.Fill(values);
        return editor;
    }

    public override string Title => IsNew ? "Yeni profil" : $"Profili düzenle: {OriginalName}";

    public override double DialogWidth => 920;

    public override double DialogHeight => 700;

    public override bool CanResize => true;

    public string? OriginalName { get; }

    public bool IsNew => OriginalName is null;

    public ObservableCollection<ProfileEntryViewModel> Rows { get; } = [];

    public IReadOnlyList<string> Environments { get; }

    public bool HasEnvironments => Environments.Count > 0;

    public IReadOnlyList<string> OtherProfiles { get; }

    public bool HasOtherProfiles => OtherProfiles.Count > 0;

    public bool IsFromCurrentSecrets
    {
        get => Source == ProfileSource.CurrentSecrets;
        set => SelectSource(value, ProfileSource.CurrentSecrets);
    }

    public bool IsFromEnvironment
    {
        get => Source == ProfileSource.Environment;
        set => SelectSource(value, ProfileSource.Environment);
    }

    public bool IsFromOtherProfile
    {
        get => Source == ProfileSource.OtherProfile;
        set => SelectSource(value, ProfileSource.OtherProfile);
    }

    public bool IsEmptySource
    {
        get => Source == ProfileSource.Empty;
        set => SelectSource(value, ProfileSource.Empty);
    }

    public string? NameError
    {
        get
        {
            if (Name.Length == 0)
            {
                return null;
            }

            var error = SecretProfileStore.ValidateName(Name);
            if (error is not null || IsNew)
            {
                return error;
            }

            var renamed = !string.Equals(Name.Trim(), OriginalName, StringComparison.OrdinalIgnoreCase);
            return renamed && _profileExists(Name) ? "Bu adda başka bir profil var" : null;
        }
    }

    public bool HasNameError => NameError is not null;

    /// <summary>Only for new profiles: saving replaces the existing profile with the same name.</summary>
    public bool WillOverwrite => IsNew && SecretProfileStore.ValidateName(Name) is null && _profileExists(Name);

    public bool HasSourceNote => SourceNote is not null;

    public bool HasRows => Rows.Count > 0;

    public bool HasRowErrors => Rows.Any(r => r.HasKeyError);

    public string RowsSummary => $"{Rows.Count} secret" + (HasRowErrors ? " · hatalı anahtarları düzeltin" : string.Empty);

    public IReadOnlyList<KeyValuePair<string, string>> Values =>
        Rows.Select(r => new KeyValuePair<string, string>(r.Key.Trim(), r.Value)).ToList();

    public int MissingFromSecretsCount => _configuration.Secrets?.Keys.Count(k => !ContainsKey(k)) ?? 0;

    public bool CanAddMissingFromSecrets => MissingFromSecretsCount > 0;

    public string MissingFromSecretsText => $"secrets.json'dan eksikleri ekle ({MissingFromSecretsCount})";

    internal void OnRowsChanged()
    {
        if (!_filling)
        {
            IsEdited = true;
        }

        foreach (var row in Rows)
        {
            row.Validate(Rows);
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasRowErrors));
        OnPropertyChanged(nameof(RowsSummary));
        OnPropertyChanged(nameof(MissingFromSecretsCount));
        OnPropertyChanged(nameof(CanAddMissingFromSecrets));
        OnPropertyChanged(nameof(MissingFromSecretsText));
        SaveCommand.NotifyCanExecuteChanged();
    }

    internal void Remove(ProfileEntryViewModel row) => Rows.Remove(row);

    partial void OnEnvironmentChanged(string value) => FillIfUntouched();

    partial void OnOtherProfileChanged(string? value) => FillIfUntouched();

    partial void OnRevealAllChanged(bool value)
    {
        foreach (var row in Rows)
        {
            row.IsRevealed = value;
        }
    }

    private bool CanSave() => SecretProfileStore.ValidateName(Name) is null && NameError is null && Rows.Count > 0 && !HasRowErrors;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => Close(true);

    [RelayCommand]
    private void AddRow()
    {
        Rows.Add(new ProfileEntryViewModel(this, string.Empty, string.Empty) { IsRevealed = true });
    }

    [RelayCommand]
    private void AddMissingFromSecrets()
    {
        foreach (var (key, value) in _configuration.Secrets?.Where(p => !ContainsKey(p.Key)).ToList() ?? [])
        {
            Rows.Add(new ProfileEntryViewModel(this, key, value) { IsRevealed = RevealAll });
        }
    }

    [RelayCommand]
    private void RefillFromSource() => FillFromSource();

    private void SelectSource(bool selected, ProfileSource source)
    {
        if (!selected || Source == source)
        {
            return;
        }

        Source = source;
        FillIfUntouched();
    }

    private void FillIfUntouched()
    {
        if (!IsNew)
        {
            return;
        }

        if (IsEdited)
        {
            SourceNote = "Tabloyu elle değiştirdiniz; kaynak değişikliği tabloya uygulanmadı. İsterseniz \"Kaynaktan yeniden doldur\"u kullanın.";
            return;
        }

        FillFromSource();
    }

    private void FillFromSource()
    {
        SourceNote = null;
        var secrets = _configuration.Secrets!;
        switch (Source)
        {
            case ProfileSource.CurrentSecrets:
                Fill(secrets.ToList());
                break;
            case ProfileSource.Environment:
                var result = ProfileValues.FromEnvironment(_configuration, Environment, secrets.Keys);
                Fill(result.Values);
                if (result.MissingKeys.Count > 0)
                {
                    SourceNote = $"{Environment} ortamında değeri olmayan {result.MissingKeys.Count} anahtar alınmadı: " +
                                 string.Join(", ", result.MissingKeys) + ". Tabloya ekleyip değer girebilirsiniz.";
                }

                break;
            case ProfileSource.OtherProfile:
                Fill(OtherProfile is null ? [] : LoadOther(OtherProfile));
                break;
            default:
                Fill([]);
                break;
        }
    }

    private IReadOnlyList<KeyValuePair<string, string>> LoadOther(string name)
    {
        try
        {
            return _loadProfile(name);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or FormatException)
        {
            SourceNote = $"'{name}' profili açılamadı: {ex.Message}";
            return [];
        }
    }

    private void Fill(IEnumerable<KeyValuePair<string, string>> values)
    {
        _filling = true;
        Rows.Clear();
        foreach (var (key, value) in values)
        {
            Rows.Add(new ProfileEntryViewModel(this, key, value) { IsRevealed = RevealAll });
        }

        _filling = false;
        IsEdited = false;
        OnRowsChanged();
    }

    private bool ContainsKey(string key) => Rows.Any(r => ConfigKey.Comparer.Equals(r.Key.Trim(), key));
}

/// <summary>One editable key/value of a profile.</summary>
public sealed partial class ProfileEntryViewModel : ObservableObject
{
    private readonly ProfileEditorViewModel _owner;

    [ObservableProperty]
    private string _key;

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private bool _isRevealed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeyError))]
    private string? _keyError;

    public ProfileEntryViewModel(ProfileEditorViewModel owner, string key, string value)
    {
        _owner = owner;
        _key = key;
        _value = value;
    }

    public bool HasKeyError => KeyError is not null;

    internal void Validate(IReadOnlyCollection<ProfileEntryViewModel> rows)
    {
        var key = Key.Trim();
        KeyError = ConfigKey.Validate(key)
                   ?? (rows.Any(r => !ReferenceEquals(r, this) && ConfigKey.Comparer.Equals(r.Key.Trim(), key))
                       ? "Bu anahtar zaten var"
                       : null);
    }

    partial void OnKeyChanged(string value) => _owner.OnRowsChanged();

    partial void OnValueChanged(string value) => _owner.OnRowsChanged();

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;

    [RelayCommand]
    private void Remove() => _owner.Remove(this);
}
