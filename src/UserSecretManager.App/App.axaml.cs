using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using UserSecretManager.App.Services;
using UserSecretManager.App.ViewModels;
using UserSecretManager.App.Views;
using UserSecretManager.Core.IO;

namespace UserSecretManager.App;

public partial class App : Application
{
    /// <summary>Overrides where the workspace list and backups are stored (portable use, sandboxed testing).</summary>
    public const string DataDirectoryVariable = "USERSECRETMANAGER_DATA_DIR";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var platform = new AvaloniaPlatformServices(() => window);
            var dataDirectory = Environment.GetEnvironmentVariable(DataDirectoryVariable);
            var location = string.IsNullOrWhiteSpace(dataDirectory) ? AppDataLocation.Default : new AppDataLocation(dataDirectory);
            var services = new AppServices(location, platform, platform);
            window.DataContext = new MainWindowViewModel(services);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
