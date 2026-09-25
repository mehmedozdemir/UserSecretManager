using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Migration;

namespace UserSecretManager.App.ViewModels;

/// <summary>Move secrets back into an appsettings file.</summary>
public sealed partial class MoveBackViewModel : DialogViewModelBase
{
    private readonly ProjectConfiguration _configuration;
    private readonly IReadOnlyList<string> _keys;

    [ObservableProperty]
    private AppSettingsFile? _targetFile;

    [ObservableProperty]
    private bool _removeFromSecrets = true;

    [ObservableProperty]
    private string? _error;

    public MoveBackViewModel(ProjectConfiguration configuration, IReadOnlyList<string> keys)
    {
        _configuration = configuration;
        _keys = keys;
        Files = configuration.ValidFiles.ToList();
        _targetFile = Files.FirstOrDefault(f => f.IsDevelopment) ?? (Files.Count > 0 ? Files[0] : null);
        Refresh();
    }

    public override string Title => "appsettings'e geri taşı";

    public override double DialogWidth => 900;

    public override double DialogHeight => 620;

    public override bool CanResize => true;

    public IReadOnlyList<AppSettingsFile> Files { get; }

    public string KeysText => string.Join(", ", _keys);

    public int KeyCount => _keys.Count;

    public ChangePreviewViewModel Preview { get; } = new();

    public ChangeSet? ChangeSet { get; private set; }

    public bool CanApply => ChangeSet is { IsEmpty: false } && Error is null;

    public bool ShowsNonDevelopmentWarning => TargetFile is { AppliesToAllEnvironments: false, IsDevelopment: false };

    partial void OnTargetFileChanged(AppSettingsFile? value)
    {
        OnPropertyChanged(nameof(ShowsNonDevelopmentWarning));
        Refresh();
    }

    partial void OnRemoveFromSecretsChanged(bool value) => Refresh();

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => Close(true);

    private void Refresh()
    {
        try
        {
            ChangeSet = TargetFile is null ? null
                : ChangeSetFactory.MoveBack(_configuration, _keys.ToList(), TargetFile, RemoveFromSecrets);
            Error = TargetFile is null ? "Hedef appsettings dosyası yok." : null;
        }
        catch (InvalidOperationException ex)
        {
            ChangeSet = null;
            Error = ex.Message;
        }

        Preview.Load(ChangeSet);
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Confirm a change set after looking at its diff.</summary>
public sealed partial class ConfirmChangesViewModel : DialogViewModelBase
{
    public ConfirmChangesViewModel(string title, string message, ChangeSet changeSet, string confirmText)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        Preview.Load(changeSet);
    }

    public override string Title { get; }

    public override double DialogWidth => 900;

    public override double DialogHeight => 600;

    public override bool CanResize => true;

    public string Message { get; }

    public string ConfirmText { get; }

    public ChangePreviewViewModel Preview { get; } = new();

    [RelayCommand]
    private void Confirm() => Close(true);
}
