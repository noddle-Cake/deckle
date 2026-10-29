using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Scanwise.App.Services;
using Scanwise.App.ViewModels;
using Scanwise.App.Views;
using Scanwise.Core.Settings;

namespace Scanwise.App;

public partial class App : Application
{
    /// <summary>Started with --scan (from a shortcut or the jump list / launcher / Dock menu).</summary>
    public static bool ScanOnStart { get; set; }

    private readonly SingleInstance _instance = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (AppShortcuts.IsMac)
        {
            Styles.Add(new StyleInclude(new Uri("avares://Scanwise/")) { Source = new Uri("avares://Scanwise/MacStyles.axaml") });
        }
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
            if (AppShortcuts.IsMac)
            {
                NativeMenu.SetMenu(this, MacMenuBar.AppMenu(vm, window.OpenSettingsCommand));
                NativeMenu.SetMenu(window, MacMenuBar.WindowMenu(vm, window));
            }
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => vm.OnShutdown();
            window.Opened += async (_, _) => await vm.InitializeAsync();
            if (ScanOnStart)
            {
                window.Opened += async (_, _) => await vm.ScanFromShortcutAsync();
            }
            desktop.Exit += (_, _) =>
            {
                _instance.Dispose();
                services.Dispose();
            };

            void HandleRequest(string request) => Dispatcher.UIThread.Post(async () =>
            {
                BringToFront(window);
                if (request == SingleInstance.ScanRequest)
                {
                    await vm.ScanFromShortcutAsync();
                }
            });
            _instance.Listen(HandleRequest);
            services.Shortcuts.ScanRequested += () => HandleRequest(SingleInstance.ScanRequest);
            services.Shortcuts.InstallLauncherEntries();
            if (services.Settings.ScanHotkey)
            {
                // Re-point the shortcut at this copy (the folder may have moved) and register the macOS hotkey.
                _ = RefreshHotkeyAsync(services);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Show();
        window.Activate();
    }

    private static async Task RefreshHotkeyAsync(AppServices services)
    {
        try
        {
            await services.Shortcuts.ApplyHotkeyAsync(true, services.Settings.ScanHotkeyKey.FirstOrDefault('S'));
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Trace.WriteLine($"Scanwise: {ex.Message}");
        }
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
