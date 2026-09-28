using Avalonia;
using Velopack;

namespace UserSecretManager.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Must run first: handles install/uninstall/update hooks and exits early when Velopack invokes the app for them.
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
