namespace UserSecretManager.App.Services;

/// <summary>Clipboard and shell operations.</summary>
public interface IPlatformService
{
    /// <summary>Copies text to the clipboard.</summary>
    Task CopyToClipboardAsync(string text);

    /// <summary>Opens a folder in the system file manager.</summary>
    Task OpenFolderAsync(string path);
}
