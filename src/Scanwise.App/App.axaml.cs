using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Scanwise.App.Services;
using Scanwise.App.Services.Platform;
using Scanwise.App.ViewModels;
using Scanwise.App.Views;
using Scanwise.Core.Settings;

namespace Scanwise.App;

public partial class App : Application
{
    /// <summary>Started with --scan (from a shortcut or the jump list / launcher / Dock menu).</summary>
    public static bool ScanOnStart { get; set; }

    /// <summary>Started with --self-check: where the macOS self-check report goes.</summary>
    public static string? SelfCheckReport { get; set; }

    /// <summary>--keep-open=N: seconds to stay open after the self-check (for a screenshot).</summary>
    public static int SelfCheckKeepOpenSeconds { get; set; }

    /// <summary>The process exit code: the self-check's failure count, otherwise 0.</summary>
    public static int ExitCode { get; private set; }

    private readonly SingleInstance _instance = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        if (AppShortcuts.IsMac)
        {
            TitleCase.ApplyToMenus();
            Styles.Add(new StyleInclude(new Uri("avares://Scanwise/")) { Source = new Uri("avares://Scanwise/MacStyles.axaml") });
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new AppServices(AppPaths.Default());
            ApplyTheme(services.Settings.Theme);
            FollowContrastSetting();
            var window = new MainWindow();
            WindowFrames.Restore(window, services.Settings.MainWindowFrame);
            window.Closing += (_, _) =>
            {
                services.Settings.MainWindowFrame = WindowFrames.Capture(window, services.Settings.MainWindowFrame);
                services.SaveSettings();
            };
            var ui = new UiServices(window);
            var vm = new MainViewModel(services, ui);
            window.DataContext = vm;
            if (AppShortcuts.IsMac)
            {
                NativeMenu.SetMenu(this, MacMenuBar.AppMenu(vm, window.OpenSettingsCommand));
                NativeMenu.SetMenu(window, MacMenuBar.WindowMenu(vm, window, window.OpenHelpCommand));
            }
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => vm.OnShutdown();
            window.Opened += async (_, _) => await vm.InitializeAsync();
            if (SelfCheckReport is { } report && OperatingSystem.IsMacOS())
            {
                ScheduleSelfCheck(desktop, window, vm, report);
            }
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
            services.Shortcuts.ScanNewRequested += () => Dispatcher.UIThread.Post(async () =>
            {
                BringToFront(window);
                await vm.ScanStartFreshCommand.ExecuteAsync(null);
            });
            services.Shortcuts.InstallLauncherEntries();
            if (services.Settings.ScanHotkey)
            {
                // Re-point the shortcut at this copy (the folder may have moved) and register the macOS hotkey.
                _ = RefreshHotkeyAsync(services);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void ScheduleSelfCheck(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, MainViewModel vm, string report) =>
        window.Opened += async (_, _) =>
        {
            await Task.Delay(1500); // Let the menu bar, Dock menu and glass panel settle.
            ExitCode = await MacSelfCheck.RunAsync(window, vm, report);
            await Task.Delay(TimeSpan.FromSeconds(SelfCheckKeepOpenSeconds));
            desktop.Shutdown(ExitCode);
        };

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

    private ResourceDictionary? _highContrast;

    /// <summary>
    /// Increase Contrast (macOS) or a contrast theme (Windows): stronger card and pane borders, and captions at
    /// full strength instead of faded.
    /// </summary>
    private void FollowContrastSetting()
    {
        if (PlatformSettings is not { } platform)
        {
            return;
        }
        void Apply(PlatformColorValues values)
        {
            var high = values.ContrastPreference == ColorContrastPreference.High;
            if (high && _highContrast is null)
            {
                _highContrast = new ResourceDictionary
                {
                    ThemeDictionaries =
                    {
                        [ThemeVariant.Light] = new ResourceDictionary { ["CardBorder"] = new SolidColorBrush(Color.Parse("#99000000")) },
                        [ThemeVariant.Dark] = new ResourceDictionary { ["CardBorder"] = new SolidColorBrush(Color.Parse("#99FFFFFF")) },
                    },
                };
                Resources.MergedDictionaries.Add(_highContrast);
                Styles.Add(_strongCaptions);
            }
            else if (!high && _highContrast is not null)
            {
                Resources.MergedDictionaries.Remove(_highContrast);
                Styles.Remove(_strongCaptions);
                _highContrast = null;
            }
        }
        Apply(platform.GetColorValues());
        platform.ColorValuesChanged += (_, values) => Dispatcher.UIThread.Post(() => Apply(values));
    }

    private readonly Style _strongCaptions = new(x => x.OfType<TextBlock>().Class("caption"))
    {
        Setters = { new Setter(Visual.OpacityProperty, 1.0) },
    };

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
