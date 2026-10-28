using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Scanwise.App.Services;
using Scanwise.App.ViewModels;
using Scanwise.App.Views;
using Scanwise.Core.Settings;

namespace Scanwise.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new AppServices(AppPaths.Default());
            ApplyTheme(services.Settings.Theme);
            var window = new MainWindow();
            var ui = new UiServices(window);
            var vm = new MainViewModel(services, ui);
            window.DataContext = vm;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => vm.OnShutdown();
            window.Opened += async (_, _) => await vm.InitializeAsync();
            desktop.Exit += (_, _) => services.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is null)
        {
            return;
        }
        Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
