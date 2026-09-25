using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.Core.Migration;
using UserSecretManager.Core.Profiles;

namespace UserSecretManager.App.ViewModels;

/// <summary>Encrypted secret profiles of the project's UserSecretsId.</summary>
public sealed partial class ProfilesTabViewModel(ProjectViewModel project) : ObservableObject
{
    private ProjectConfiguration? _configuration;

    [ObservableProperty]
    private string? _error;

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    public string? UserSecretsId => _configuration?.Project.SecretsId.Id;

    public bool IsAvailable => UserSecretsId is not null && _configuration?.Secrets is not null;

    public bool IsUnavailable => !IsAvailable;

    public bool IsEmpty => IsAvailable && Profiles.Count == 0;

    public string UnavailableMessage => _configuration?.Project.SecretsId.Source == Core.Discovery.UserSecretsIdSource.Unresolvable
        ? "UserSecretsId çözümlenemediği için profiller kullanılamaz."
        : "Profiller UserSecretsId'ye bağlıdır. Önce bir değeri secret'a taşıyın veya Secrets sekmesinden kaydedin; id otomatik eklenir.";

    private SecretProfileStore Store => project.Services.Profiles;

    public void Load(ProjectConfiguration configuration)
    {
        _configuration = configuration;
        Profiles.Clear();
        Error = null;
        if (IsAvailable)
        {
            foreach (var info in Store.List(UserSecretsId!))
            {
                Profiles.Add(new ProfileRowViewModel(info, IsActive(info.Name), this));
            }
        }

        OnPropertyChanged(nameof(UserSecretsId));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(IsUnavailable));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(UnavailableMessage));
    }

    private bool IsActive(string name)
    {
        try
        {
            return SecretProfileStore.Matches(Store.Load(UserSecretsId!, name), _configuration!.Secrets);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or FormatException)
        {
            return false;
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        var editor = ProfileEditorViewModel.ForNew(_configuration!, ProfileNames(), LoadProfile, ProfileExists);
        if (await project.Services.Dialogs.ShowDialogAsync(editor))
        {
            var matchesCurrent = SecretProfileStore.Matches(editor.Values, _configuration!.Secrets);
            Run(() => Store.Save(UserSecretsId!, editor.Name, editor.Values, editor.Description),
                matchesCurrent
                    ? $"'{editor.Name.Trim()}' profili kaydedildi. Şu anki secret'larla aynı olduğu için etkin görünür; başka bir profile geçtikten sonra buna dönmek için uygulayabilirsiniz."
                    : $"'{editor.Name.Trim()}' profili kaydedildi.");
        }
    }

    internal async Task EditAsync(ProfileRowViewModel row)
    {
        IReadOnlyList<KeyValuePair<string, string>> values;
        try
        {
            values = LoadProfile(row.Name);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or FormatException)
        {
            await project.Services.Dialogs.ShowMessageAsync("Profil açılamadı", ex.Message);
            return;
        }

        var editor = ProfileEditorViewModel.ForExisting(_configuration!, ProfileNames(), LoadProfile, ProfileExists,
            row.Name, row.Description, values);
        if (!await project.Services.Dialogs.ShowDialogAsync(editor))
        {
            return;
        }

        var newName = editor.Name.Trim();
        Run(() =>
        {
            if (!string.Equals(newName, row.Name, StringComparison.Ordinal))
            {
                Store.Rename(UserSecretsId!, row.Name, newName);
            }

            Store.Save(UserSecretsId!, newName, editor.Values, editor.Description);
        }, $"'{newName}' profili güncellendi. secrets.json'a yazmak için profili uygulayın.");
    }

    private List<string> ProfileNames() => Profiles.Select(p => p.Name).ToList();

    private IReadOnlyList<KeyValuePair<string, string>> LoadProfile(string name) => Store.Load(UserSecretsId!, name);

    private bool ProfileExists(string name) => Store.Exists(UserSecretsId!, name);

    internal async Task ApplyAsync(ProfileRowViewModel row)
    {
        if (!await project.EnsureNoUnsavedSecretsAsync())
        {
            return;
        }

        IReadOnlyList<KeyValuePair<string, string>> values;
        try
        {
            values = Store.Load(UserSecretsId!, row.Name);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or FormatException)
        {
            await project.Services.Dialogs.ShowMessageAsync("Profil açılamadı", ex.Message);
            return;
        }

        var dialog = new ApplySecretsViewModel($"'{row.Name}' profilini uygula", $"'{row.Name}' profili", values,
            _configuration!, project.Services.ChangeSets);
        if (await project.Services.Dialogs.ShowDialogAsync(dialog) && dialog.ChangeSet is { IsEmpty: false } changeSet)
        {
            await project.ApplyChangesAsync(changeSet, $"'{row.Name}' profili uygulandı.");
        }
    }

    internal async Task OverwriteAsync(ProfileRowViewModel row)
    {
        var confirmed = await project.Services.Dialogs.ConfirmAsync("Profili güncelle",
            $"'{row.Name}' profilinin değerleri şu anki {_configuration!.Secrets!.Count} secret ile değiştirilecek.",
            "Güncelle", isDestructive: true);
        if (confirmed)
        {
            Run(() => Store.Save(UserSecretsId!, row.Name, _configuration.Secrets.ToList(), row.Description),
                $"'{row.Name}' profili güncellendi.");
        }
    }

    internal async Task DeleteAsync(ProfileRowViewModel row)
    {
        var confirmed = await project.Services.Dialogs.ConfirmAsync("Profili sil",
            $"'{row.Name}' profili kalıcı olarak silinecek. secrets.json değişmez.", "Sil", isDestructive: true);
        if (confirmed)
        {
            Run(() => Store.Delete(UserSecretsId!, row.Name), $"'{row.Name}' profili silindi.");
        }
    }

    private void Run(Action action, string success)
    {
        try
        {
            action();
            project.ShowStatus(success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                                       or InvalidOperationException or ArgumentException)
        {
            Error = ex.Message;
        }

        Load(_configuration!);
    }
}

public sealed partial class ProfileRowViewModel(SecretProfileInfo info, bool isActive, ProfilesTabViewModel owner)
{
    public string Name => info.Name;

    public string? Description => info.Description;

    public bool HasDescription => !string.IsNullOrEmpty(info.Description);

    public bool IsActive { get; } = isActive;

    public string Details => string.Create(CultureInfo.InvariantCulture,
        $"{info.KeyCount} secret · güncellendi {info.UpdatedAt.ToLocalTime():dd.MM.yyyy HH:mm}");

    [RelayCommand]
    private Task ApplyAsync() => owner.ApplyAsync(this);

    [RelayCommand]
    private Task OverwriteAsync() => owner.OverwriteAsync(this);

    [RelayCommand]
    private Task EditAsync() => owner.EditAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => owner.DeleteAsync(this);
}
