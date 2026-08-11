using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LaptopQATestingMac.Services;

namespace LaptopQATestingMac;

public sealed partial class App : Application
{
    public static string? StartupDataRoot { get; set; }
    public static bool StartupRemovableDataRootDetected { get; set; }
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var preview = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("--preview-config", StringComparison.OrdinalIgnoreCase));
            if (preview is not null)
            {
                var requested = preview.Contains('=') ? preview[(preview.IndexOf('=') + 1)..] : "Light";
                var theme = requested.Equals("Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : requested.Equals("AMOLED", StringComparison.OrdinalIgnoreCase) ? "AMOLED" : "Light";
                desktop.MainWindow = new SettingsWindow(new AppConfig { AppTheme = theme, ThemePreferenceSet = true });
            }
            else
            {
                var storage = new ConfigService(StartupDataRoot);
                var config = storage.Load();
                var joke = StartupJokeService.Next(storage.DataRoot);
                var splash = new StartupSplashWindow(joke, config.AppTheme, config.AppLanguage);
                desktop.MainWindow = splash;
                splash.Opened += async (_, _) =>
                {
                    await Task.Delay(2200);
                    var main = new MainWindow();
                    desktop.MainWindow = main;
                    main.Show();
                    splash.Close();
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
