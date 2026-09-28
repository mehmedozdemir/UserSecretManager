using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace UserSecretManager.App.Services;

/// <summary>Result of an update check.</summary>
public enum UpdateCheckStatus
{
    /// <summary>The app was not installed with the setup, so it cannot update itself.</summary>
    NotInstalled,

    /// <summary>Already on the latest version.</summary>
    UpToDate,

    /// <summary>A newer version is available.</summary>
    Available,
}

/// <summary>
/// Self-update through Velopack using the GitHub releases of the project. Only runs when the user asks for it; the
/// application makes no network calls otherwise.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Public releases page, also the update source.</summary>
    public const string RepositoryUrl = "https://github.com/mehmedozdemir/UserSecretManager";

    private UpdateManager? _manager;
    private UpdateInfo? _pending;

    /// <summary>Version shown in the UI.</summary>
    public static string CurrentVersion
    {
        get
        {
            var informational = typeof(UpdateService).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }
    }

    /// <summary>Releases page for manual download.</summary>
    public static string ReleasesUrl => RepositoryUrl + "/releases/latest";

    /// <summary>Version found by the last check, if any.</summary>
    public string? AvailableVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>Checks GitHub for a newer release.</summary>
    public async Task<UpdateCheckStatus> CheckAsync()
    {
        if (GetManager() is not { IsInstalled: true } manager)
        {
            return UpdateCheckStatus.NotInstalled;
        }

        _pending = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _pending is null ? UpdateCheckStatus.UpToDate : UpdateCheckStatus.Available;
    }

    /// <summary>Downloads the update found by <see cref="CheckAsync"/>, then restarts into it.</summary>
    public async Task DownloadAndRestartAsync(Action<int>? progress = null)
    {
        var pending = _pending ?? throw new InvalidOperationException("Önce güncelleme denetimi yapılmalı.");
        var manager = GetManager() ?? throw new InvalidOperationException("Güncelleme yöneticisi kullanılamıyor.");
        await manager.DownloadUpdatesAsync(pending, progress).ConfigureAwait(false);
        manager.ApplyUpdatesAndRestart(pending);
    }

    /// <summary>
    /// Created on first use: Velopack needs <c>VelopackApp.Run()</c> to have located the install, which is not the case
    /// when the app is hosted elsewhere (tests, previews). Then the app simply counts as not installed.
    /// </summary>
    private UpdateManager? GetManager()
    {
        if (_manager is not null)
        {
            return _manager;
        }

        try
        {
            _manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return _manager;
    }
}
