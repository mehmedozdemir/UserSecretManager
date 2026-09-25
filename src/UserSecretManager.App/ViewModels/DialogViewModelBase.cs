using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UserSecretManager.App.ViewModels;

/// <summary>Base for view models shown in <c>DialogWindow</c>.</summary>
public abstract partial class DialogViewModelBase : ObservableObject
{
    public event EventHandler? CloseRequested;

    public abstract string Title { get; }

    public virtual double DialogWidth => 560;

    public virtual double DialogHeight => 260;

    public virtual bool CanResize => false;

    /// <summary>Whether the dialog was accepted.</summary>
    public bool Result { get; private set; }

    [RelayCommand]
    protected void Cancel() => Close(false);

    protected void Close(bool result)
    {
        Result = result;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A simple message or confirmation.</summary>
public sealed partial class MessageDialogViewModel(
    string title, string message, string confirmText, string? cancelText, bool isDestructive) : DialogViewModelBase
{
    public override string Title { get; } = title;

    public string Message { get; } = message;

    public string ConfirmText { get; } = confirmText;

    public string? CancelText { get; } = cancelText;

    public bool HasCancel => CancelText is not null;

    public bool IsDestructive { get; } = isDestructive;

    public bool IsNotDestructive => !IsDestructive;

    [RelayCommand]
    private void Confirm() => Close(true);
}
