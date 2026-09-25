using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.Core.Migration;
using UserSecretManager.Core.Profiles;

namespace UserSecretManager.App.ViewModels;

/// <summary>Create a profile from the current secrets or from an environment's values.</summary>
public sealed partial class ProfileEditorViewModel : DialogViewModelBase
{
    private readonly ProjectConfiguration _configuration;
    private readonly SecretProfileStore _store;
    private readonly string _userSecretsId;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private bool _fromCurrentSecrets = true;

    [ObservableProperty]
    private string _environment;

    [ObservableProperty]
    private IReadOnlyList<KeyValuePair<string, string>> _previewValues = [];

    [ObservableProperty]
    private IReadOnlyList<ProfileValueRowViewModel> _previewRows = [];

    [ObservableProperty]
    private string? _missingText;

    [ObservableProperty]
    private bool _willOverwrite;

    public ProfileEditorViewModel(ProjectConfiguration configuration, SecretProfileStore store, string userSecretsId)
    {
        _configuration = configuration;
        _store = store;
        _userSecretsId = userSecretsId;
        Environments = configuration.Files.Select(f => f.Environment).OfType<string>()
            .Concat(configuration.Project.LaunchEnvironments)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _environment = Environments.FirstOrDefault(e => !Core.Configuration.AppSettingsFile.IsDevelopmentName(e))
                       ?? (Environments.Count > 0 ? Environments[0] : "Production");
        Refresh();
    }

    public override string Title => "Yeni profil";

    public override double DialogWidth => 760;

    public override double DialogHeight => 600;

    public override bool CanResize => true;

    public IReadOnlyList<string> Environments { get; }

    public bool HasEnvironments => Environments.Count > 0;

    public string? NameError => Name.Length == 0 ? null : SecretProfileStore.ValidateName(Name);

    public bool HasNameError => NameError is not null;

    public bool HasMissing => MissingText is not null;

    public bool HasPreview => PreviewValues.Count > 0;

    partial void OnNameChanged(string value)
    {
        WillOverwrite = SecretProfileStore.ValidateName(value) is null && _store.Exists(_userSecretsId, value);
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(HasNameError));
    }

    partial void OnFromCurrentSecretsChanged(bool value) => Refresh();

    partial void OnEnvironmentChanged(string value) => Refresh();

    private bool CanSave() => SecretProfileStore.ValidateName(Name) is null && PreviewValues.Count > 0;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => Close(true);

    private void Refresh()
    {
        var secrets = _configuration.Secrets!;
        if (FromCurrentSecrets)
        {
            PreviewValues = secrets.ToList();
            MissingText = null;
        }
        else
        {
            var result = ProfileValues.FromEnvironment(_configuration, Environment, secrets.Keys);
            PreviewValues = result.Values;
            MissingText = result.MissingKeys.Count == 0 ? null
                : $"{Environment} ortamında değeri olmayan {result.MissingKeys.Count} anahtar profile alınmayacak: {string.Join(", ", result.MissingKeys)}";
        }

        PreviewRows = PreviewValues.Select(v => new ProfileValueRowViewModel(v.Key, v.Value)).ToList();
        OnPropertyChanged(nameof(HasMissing));
        OnPropertyChanged(nameof(HasPreview));
        SaveCommand.NotifyCanExecuteChanged();
    }
}

public sealed class ProfileValueRowViewModel(string key, string value)
{
    public string Key { get; } = key;

    public string MaskedValue { get; } = ValueMask.Mask(value);
}

/// <summary>Preview and confirm writing a set of secret values (a profile or an import) to secrets.json.</summary>
public sealed partial class ApplySecretsViewModel : DialogViewModelBase
{
    private readonly IReadOnlyList<KeyValuePair<string, string>> _values;
    private readonly ProjectConfiguration _configuration;
    private readonly ChangeSetFactory _factory;

    [ObservableProperty]
    private bool _replace = true;

    [ObservableProperty]
    private string? _error;

    public ApplySecretsViewModel(string title, string sourceName, IReadOnlyList<KeyValuePair<string, string>> values,
        ProjectConfiguration configuration, ChangeSetFactory factory, string? details = null, string? warning = null)
    {
        Title = title;
        SourceName = sourceName;
        Details = details;
        Warning = warning;
        _values = values;
        _configuration = configuration;
        _factory = factory;

        // Replacing with values that lack some current keys would delete those secrets, so merge by default then.
        MissingKeyCount = configuration.Secrets!.Keys
            .Count(k => !values.Any(v => Core.Configuration.ConfigKey.Comparer.Equals(v.Key, k)));
        _replace = MissingKeyCount == 0;
        Refresh();
    }

    public override string Title { get; }

    public string SourceName { get; }

    public string? Details { get; }

    public bool HasDetails => Details is not null;

    public string? Warning { get; }

    public bool HasWarning => Warning is not null;

    public int MissingKeyCount { get; }

    public bool ReplaceDeletesSecrets => Replace && MissingKeyCount > 0;

    public string DeleteWarning => $"{SourceName} içinde olmayan {MissingKeyCount} secret silinecek.";

    public override double DialogWidth => 900;

    public override double DialogHeight => 620;

    public override bool CanResize => true;

    public ChangePreviewViewModel Preview { get; } = new();

    public ChangeSet? ChangeSet { get; private set; }

    public bool CanApply => ChangeSet is { IsEmpty: false };

    public string Summary => ChangeSet is { IsEmpty: true }
        ? "secrets.json zaten bu değerlerle aynı; değişiklik yok."
        : Replace
            ? $"secrets.json tamamen {SourceName} ile değiştirilecek; içinde olmayan secret'lar silinir."
            : $"{SourceName} içindeki değerler mevcut secret'ların üzerine yazılacak; diğer secret'lar korunur.";

    partial void OnReplaceChanged(bool value) => Refresh();

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => Close(true);

    private void Refresh()
    {
        try
        {
            var mode = Replace ? ProfileApplyMode.Replace : ProfileApplyMode.Merge;
            var target = ProfileValues.Apply(_configuration.Secrets!, _values, mode);
            ChangeSet = _factory.ReplaceSecrets(_configuration, target, $"{SourceName} uygulandı ({(Replace ? "değiştir" : "birleştir")})");
            Error = null;
        }
        catch (InvalidOperationException ex)
        {
            ChangeSet = null;
            Error = ex.Message;
        }

        Preview.Load(ChangeSet);
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ReplaceDeletesSecrets));
        ApplyCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Single text input with validation.</summary>
public sealed partial class TextInputViewModel : DialogViewModelBase
{
    private readonly Func<string, string?> _validate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorText), nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _text;

    public TextInputViewModel(string title, string label, string initialValue, Func<string, string?> validate)
    {
        Title = title;
        Label = label;
        _text = initialValue;
        _validate = validate;
    }

    public override string Title { get; }

    public override double DialogHeight => 200;

    public string Label { get; }

    public string? ErrorText => _validate(Text);

    public bool HasError => ErrorText is not null;

    private bool CanConfirm() => ErrorText is null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => Close(true);
}

/// <summary>Asks for a password, optionally twice.</summary>
public sealed partial class PasswordDialogViewModel : DialogViewModelBase
{
    private readonly Func<string, string?> _validate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorText), nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErrorText), nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _confirmation = string.Empty;

    [ObservableProperty]
    private bool _reveal;

    public PasswordDialogViewModel(string title, string message, bool requireConfirmation, Func<string, string?> validate)
    {
        Title = title;
        Message = message;
        RequireConfirmation = requireConfirmation;
        _validate = validate;
    }

    public override string Title { get; }

    public override double DialogHeight => RequireConfirmation ? 330 : 270;

    public string Message { get; }

    public bool RequireConfirmation { get; }

    public string? ErrorText => Password.Length == 0 ? null
        : _validate(Password) ?? (RequireConfirmation && Confirmation.Length > 0 && Confirmation != Password
            ? "Parolalar eşleşmiyor"
            : null);

    public bool HasError => ErrorText is not null;

    private bool CanConfirm() => _validate(Password) is null && (!RequireConfirmation || Confirmation == Password);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => Close(true);
}
