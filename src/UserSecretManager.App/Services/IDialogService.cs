using UserSecretManager.App.ViewModels;

namespace UserSecretManager.App.Services;

/// <summary>Opens dialogs and pickers; keeps view models free of window types.</summary>
public interface IDialogService
{
    /// <summary>Lets the user pick solution or project files. Returns local paths.</summary>
    Task<IReadOnlyList<string>> PickSolutionOrProjectFilesAsync();

    /// <summary>Lets the user pick JSON files, starting in <paramref name="startDirectory"/>.</summary>
    Task<IReadOnlyList<string>> PickJsonFilesAsync(string startDirectory);

    /// <summary>Lets the user choose where to save a file. Returns <c>null</c> when cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string fileTypeName, string extension);

    /// <summary>Lets the user pick one file. Returns <c>null</c> when cancelled.</summary>
    Task<string?> PickOpenFileAsync(string title, string fileTypeName, string extension);

    /// <summary>Asks a yes/no question.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false);

    /// <summary>Shows a message.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Shows a dialog whose content is <paramref name="viewModel"/>; returns whether it was accepted.</summary>
    Task<bool> ShowDialogAsync(DialogViewModelBase viewModel);
}
