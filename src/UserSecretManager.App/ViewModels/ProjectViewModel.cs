using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UserSecretManager.App.Services;
using UserSecretManager.Core.Configuration;
using UserSecretManager.Core.Discovery;
using UserSecretManager.Core.History;
using UserSecretManager.Core.Migration;

namespace UserSecretManager.App.ViewModels;

/// <summary>An opened project: header, tabs and the operations that modify files.</summary>
public sealed partial class ProjectViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan OwnWriteGrace = TimeSpan.FromSeconds(2);

    private readonly MainWindowViewModel _main;
    private readonly List<FileSystemWatcher> _watchers = [];
    private DateTime _ignoreChangesUntil = DateTime.MinValue;

    [ObservableProperty]
    private ProjectInfo _info;

    [ObservableProperty]
    private ProjectConfiguration? _configuration;

    [ObservableProperty]
    private int _selectedTab;

    [ObservableProperty]
    private bool _isOutdated;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private IReadOnlyList<NoticeViewModel> _notices = [];

    [ObservableProperty]
    private IReadOnlyList<string> _sharedIdProjects = [];

    public ProjectViewModel(AppServices services, ProjectInfo info, MainWindowViewModel main)
    {
        Services = services;
        _info = info;
        _main = main;
        Config = new ConfigTabViewModel(this);
        Secrets = new SecretsTabViewModel(this);
        Effective = new EffectiveTabViewModel();
        Profiles = new ProfilesTabViewModel(this);
        History = new HistoryTabViewModel(this);
    }

    public AppServices Services { get; }

    public ConfigTabViewModel Config { get; }

    public SecretsTabViewModel Secrets { get; }

    public EffectiveTabViewModel Effective { get; }

    public ProfilesTabViewModel Profiles { get; }

    public HistoryTabViewModel History { get; }

    public string Name => Info.Name;

    public string ProjectPath => Info.ProjectPath;

    public string SdkText => Info.Sdk ?? "SDK bilinmiyor";

    public string SecretsIdText => Info.SecretsId.Id ?? Info.SecretsId.Source switch
    {
        UserSecretsIdSource.Unresolvable => Info.SecretsId.RawValue ?? "çözümlenemedi",
        _ => "tanımlı değil",
    };

    public string SecretsIdSourceText => Info.SecretsId.Source switch
    {
        UserSecretsIdSource.ProjectFile => "csproj",
        UserSecretsIdSource.DirectoryBuildProps => "Directory.Build.props",
        UserSecretsIdSource.AssemblyAttribute => "assembly attribute",
        UserSecretsIdSource.Unresolvable => "MSBuild ifadesi",
        _ => "ilk taşımada eklenecek",
    };

    public bool HasSecretsId => Info.SecretsId.IsDefined;

    public IReadOnlyList<EnvironmentChipViewModel> Environments =>
        (Configuration?.Files ?? [])
        .Where(f => !f.IsCustom)
        .Select(f => new EnvironmentChipViewModel(f.DisplayName, f.IsDevelopment, f.Document is null, f.FileName))
        .Concat(Info.LaunchEnvironments
            .Where(e => Configuration?.Files.Any(f => string.Equals(f.Environment, e, StringComparison.OrdinalIgnoreCase)) != true)
            .Select(e => new EnvironmentChipViewModel(e, AppSettingsFile.IsDevelopmentName(e), false,
                "launchSettings.json içinde tanımlı, appsettings dosyası yok")
            { IsLaunchOnly = true }))
        .ToList();

    public IReadOnlyList<CustomFileChipViewModel> CustomFiles =>
        (Configuration?.Files ?? [])
        .Where(f => f.IsCustom)
        .Select(f => new CustomFileChipViewModel(f.FileName, f.Path, f.Document is null, RemoveCustomFileCommand))
        .Concat((Configuration?.MissingCustomFiles ?? [])
            .Select(p => new CustomFileChipViewModel(Path.GetFileName(p), p, true, RemoveCustomFileCommand)))
        .ToList();

    public bool HasNotices => Notices.Count > 0;

    public bool HasUnsavedChanges => Secrets.IsDirty;

    public string SecretsTabHeader => HasUnsavedChanges ? "Secrets •" : "Secrets";

    public void Reload()
    {
        try
        {
            Info = ProjectInspector.Inspect(Info.ProjectPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            _main.ShowStatus($"Proje okunamadı: {ex.Message}", isError: true);
            return;
        }

        Configuration = ProjectConfiguration.Load(Info, Services.SecretsStore, _main.GetCustomConfigFiles(Info.ProjectPath));
        Config.Load(Configuration);
        Secrets.Load(Configuration);
        Effective.Load(Configuration);
        Profiles.Load(Configuration);
        History.Load();
        Notices = BuildNotices();
        IsOutdated = false;
        OnPropertyChanged(string.Empty);
        StartWatching();
    }

    internal void OnSecretsDirtyChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(SecretsTabHeader));
    }

    public async Task MigrateAsync(IReadOnlyList<string> keys)
    {
        if (!await EnsureNoUnsavedSecretsAsync())
        {
            return;
        }

        Reload();
        var plan = Services.Planner.CreatePlan(Configuration!, keys);
        var review = new MigrationReviewViewModel(plan);
        if (await Services.Dialogs.ShowDialogAsync(review) && review.ChangeSet is { } changeSet)
        {
            await ApplyAsync(changeSet, $"{plan.Items.Count} anahtar user secrets'a taşındı.");
            Config.ClearSelectionCommand.Execute(null);
        }
    }

    public async Task SaveSecretsAsync()
    {
        ChangeSet changeSet;
        try
        {
            changeSet = Services.ChangeSets.ReplaceSecrets(Configuration!, Secrets.BuildFinalSecrets(), Secrets.DescribeChanges());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            await Services.Dialogs.ShowMessageAsync("Kaydedilemedi", ex.Message);
            return;
        }

        var confirm = new ConfirmChangesViewModel("Secret değişikliklerini kaydet", Secrets.DescribeChanges(), changeSet, "Kaydet");
        if (await Services.Dialogs.ShowDialogAsync(confirm))
        {
            await ApplyAsync(changeSet, "Secret'lar kaydedildi.");
        }
    }

    public async Task MoveBackAsync(IReadOnlyList<string> keys)
    {
        var dialog = new MoveBackViewModel(Configuration!, keys);
        if (await Services.Dialogs.ShowDialogAsync(dialog) && dialog.ChangeSet is { } changeSet)
        {
            await ApplyAsync(changeSet, $"{keys.Count} secret appsettings'e geri taşındı.");
        }
    }

    public async Task RestoreBackupAsync(BackupEntry entry)
    {
        var confirmed = await Services.Dialogs.ConfirmAsync("Yedekten geri yükle",
            $"Şu işlemden önceki duruma dönülecek:\n\n{entry.Description}\n\n" +
            "Dosyaların şu anki hali de geçmişe yedeklenir, bu geri yüklemeyi de geri alabilirsiniz.",
            "Geri yükle", isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        try
        {
            _ignoreChangesUntil = DateTime.UtcNow + OwnWriteGrace;
            Services.Backups.Restore(entry);
            _main.ShowStatus("Yedek geri yüklendi.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Services.Dialogs.ShowMessageAsync("Geri yükleme başarısız", ex.Message);
        }

        Reload();
    }

    public void Dispose() => StopWatching();

    internal void ShowStatus(string message, bool isError = false) => _main.ShowStatus(message, isError);

    internal Task ApplyChangesAsync(ChangeSet changeSet, string successMessage) => ApplyAsync(changeSet, successMessage);

    internal async Task<bool> EnsureNoUnsavedSecretsAsync()
    {
        if (!Secrets.IsDirty)
        {
            return true;
        }

        return await Services.Dialogs.ConfirmAsync("Kaydedilmemiş değişiklikler",
            "Secrets sekmesinde kaydedilmemiş değişiklikler var. Devam ederseniz bu değişiklikler atılacak.",
            "Değişiklikleri at ve devam et", isDestructive: true);
    }

    private async Task ApplyAsync(ChangeSet changeSet, string successMessage)
    {
        IsBusy = true;
        try
        {
            _ignoreChangesUntil = DateTime.UtcNow + OwnWriteGrace;
            await Task.Run(() => Services.Executor.Apply(changeSet));
            _main.ShowStatus(successMessage);
        }
        catch (FileChangedExternallyException ex)
        {
            await Services.Dialogs.ShowMessageAsync("Dosya değişti", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            await Services.Dialogs.ShowMessageAsync("İşlem uygulanamadı",
                $"{ex.Message}\n\nDosyalarınız işlem öncesi haline geri döndürüldü.");
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }

    private List<NoticeViewModel> BuildNotices()
    {
        var notices = new List<NoticeViewModel>();
        if (Info.SecretsId.Source == UserSecretsIdSource.Unresolvable)
        {
            notices.Add(new NoticeViewModel(NoticeSeverity.Error,
                $"UserSecretsId bir MSBuild ifadesiyle tanımlı ({Info.SecretsId.RawValue}); secret'lar okunamaz veya yazılamaz."));
        }

        if (Configuration?.SecretsError is { } secretsError)
        {
            notices.Add(new NoticeViewModel(NoticeSeverity.Error, $"secrets.json okunamadı: {secretsError}"));
        }

        foreach (var missing in Configuration?.MissingCustomFiles ?? [])
        {
            notices.Add(new NoticeViewModel(NoticeSeverity.Warning, $"Ek yapılandırma dosyası bulunamadı: {missing}"));
        }

        foreach (var file in Configuration?.Files.Where(f => f.Document is null) ?? [])
        {
            notices.Add(new NoticeViewModel(NoticeSeverity.Warning, $"{file.FileName} geçerli JSON değil: {file.ParseError}"));
        }

        if (!Info.HasUserSecretsSupport)
        {
            notices.Add(new NoticeViewModel(NoticeSeverity.Warning,
                "User secrets desteği tespit edilemedi. Console/Worker/kütüphane projelerinde " +
                "Microsoft.Extensions.Configuration.UserSecrets paketi ve AddUserSecrets<T>() çağrısı gerekir."));
        }

        if (SharedIdProjects.Count > 0)
        {
            notices.Add(new NoticeViewModel(NoticeSeverity.Info,
                $"Bu UserSecretsId şu projelerle paylaşılıyor: {string.Join(", ", SharedIdProjects)}. Secret değişiklikleri hepsini etkiler."));
        }

        return notices;
    }

    partial void OnNoticesChanged(IReadOnlyList<NoticeViewModel> value) => OnPropertyChanged(nameof(HasNotices));

    partial void OnSharedIdProjectsChanged(IReadOnlyList<string> value) => Notices = BuildNotices();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (await EnsureNoUnsavedSecretsAsync())
        {
            Reload();
        }
    }

    [RelayCommand]
    private async Task AddCustomFileAsync()
    {
        var picked = await Services.Dialogs.PickJsonFilesAsync(Info.Directory);
        var accepted = picked.Where(p => !Info.ConfigFilePaths.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
        if (accepted.Count == 0)
        {
            if (picked.Count > 0)
            {
                _main.ShowStatus("Seçilen appsettings dosyaları zaten otomatik olarak yükleniyor.");
            }

            return;
        }

        if (!await EnsureNoUnsavedSecretsAsync())
        {
            return;
        }

        _main.SetCustomConfigFiles(Info.ProjectPath, CurrentCustomPaths().Concat(accepted));
        Reload();
        _main.ShowStatus($"{accepted.Count} ek yapılandırma dosyası eklendi.");
    }

    [RelayCommand]
    private async Task RemoveCustomFileAsync(string? path)
    {
        if (path is null || !await EnsureNoUnsavedSecretsAsync())
        {
            return;
        }

        _main.SetCustomConfigFiles(Info.ProjectPath,
            CurrentCustomPaths().Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
        Reload();
    }

    private IEnumerable<string> CurrentCustomPaths() =>
        _main.GetCustomConfigFiles(Info.ProjectPath).Select(p => Path.GetFullPath(p, Info.Directory));

    [RelayCommand]
    private Task CopySecretsIdAsync() => Services.Platform.CopyToClipboardAsync(Info.SecretsId.Id ?? string.Empty);

    [RelayCommand]
    private Task CopyProjectPathAsync() => Services.Platform.CopyToClipboardAsync(Info.ProjectPath);

    [RelayCommand]
    private Task OpenProjectFolderAsync() => Services.Platform.OpenFolderAsync(Info.Directory);

    private void StartWatching()
    {
        StopWatching();
        TryWatch(Info.Directory, "*.json");
        TryWatch(Info.Directory, "*.*proj");
        if (Configuration?.SecretsFilePath is { Length: > 0 } secretsPath)
        {
            TryWatch(Path.GetDirectoryName(secretsPath)!, "secrets.json");
        }
    }

    private void TryWatch(string directory, string filter)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var watcher = new FileSystemWatcher(directory, filter)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Deleted += OnFileChanged;
        watcher.Renamed += OnFileChanged;
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private bool IsCustomFile(string path) =>
        Configuration?.Files.Any(f => f.IsCustom && string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)) == true;

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.FullPath);
        var relevant = name.Equals("secrets.json", StringComparison.OrdinalIgnoreCase) ||
                       IsCustomFile(e.FullPath) ||
                       name.EndsWith("proj", StringComparison.OrdinalIgnoreCase) ||
                       AppSettingsFile.TryGetEnvironment(name, out _);
        if (!relevant || DateTime.UtcNow < _ignoreChangesUntil)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => IsOutdated = true);
    }
}

public sealed record CustomFileChipViewModel(string Name, string Path, bool HasError, IAsyncRelayCommand<string?> RemoveCommand);

public sealed record EnvironmentChipViewModel(string Name, bool IsDevelopment, bool HasError, string ToolTip)
{
    public bool IsLaunchOnly { get; init; }

    public bool IsRegular => !IsDevelopment && !HasError && !IsLaunchOnly;
}
