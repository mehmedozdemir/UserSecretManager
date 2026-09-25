using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using UserSecretManager.App.ViewModels;
using UserSecretManager.App.Views;

namespace UserSecretManager.App.Services;

/// <summary>Avalonia implementation of dialogs, pickers, clipboard and shell operations.</summary>
public sealed class AvaloniaPlatformServices(Func<Window?> owner) : IDialogService, IPlatformService
{
    private static readonly FilePickerFileType SolutionAndProjects = new("Solution / Proje")
    {
        Patterns = ["*.sln", "*.slnx", "*.csproj", "*.fsproj", "*.vbproj"],
    };

    public async Task<IReadOnlyList<string>> PickSolutionOrProjectFilesAsync()
    {
        var window = owner();
        if (window is null)
        {
            return [];
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Solution veya proje seçin",
            AllowMultiple = true,
            FileTypeFilter = [SolutionAndProjects],
        });

        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<IReadOnlyList<string>> PickJsonFilesAsync(string startDirectory)
    {
        var window = owner();
        if (window is null)
        {
            return [];
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Ek yapılandırma dosyası seçin",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
            SuggestedStartLocation = await window.StorageProvider.TryGetFolderFromPathAsync(startDirectory),
        });

        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string fileTypeName, string extension)
    {
        if (owner() is not { } window)
        {
            return null;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension.TrimStart('.'),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(fileTypeName) { Patterns = [$"*{extension}"] }],
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickOpenFileAsync(string title, string fileTypeName, string extension)
    {
        if (owner() is not { } window)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(fileTypeName) { Patterns = [$"*{extension}"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false) =>
        ShowDialogAsync(new MessageDialogViewModel(title, message, confirmText, "Vazgeç", isDestructive));

    public async Task ShowMessageAsync(string title, string message) =>
        await ShowDialogAsync(new MessageDialogViewModel(title, message, "Tamam", cancelText: null, isDestructive: false));

    public async Task<bool> ShowDialogAsync(DialogViewModelBase viewModel)
    {
        var window = owner();
        var dialog = new DialogWindow { DataContext = viewModel };
        if (window is null)
        {
            dialog.Show();
            return false;
        }

        await dialog.ShowDialog(window);
        return viewModel.Result;
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (owner()?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task OpenFolderAsync(string path)
    {
        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (directory is null || !Directory.Exists(directory) || owner() is not { } window)
        {
            return;
        }

        await window.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory));
    }
}
