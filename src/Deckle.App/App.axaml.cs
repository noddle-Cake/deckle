using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Deckle.App.Services;
using Deckle.App.Services.Platform;
using Deckle.App.ViewModels;
using Deckle.App.Views;
using Deckle.Core.Settings;

namespace Deckle.App;

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

    /// <summary>Settings ▸ Look ▸ Restart Now.</summary>
    public static Action? RequestRestart { get; set; }

    /// <summary>--look=windows|macos|gnome: overrides the Look setting for this run (screenshots, testing).</summary>
    public static AppLook? LookOverride { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Fixes the look for this run (Settings ▸ Look, or --look) and loads its styles. The look decides the window
    /// structure, so a change applies on the next start.
    /// </summary>
    public void ApplyLook(AppLook setting)
    {
        PlatformLook.Current = PlatformLook.Resolve(LookOverride ?? setting);
        foreach (var file in LookStyles(PlatformLook.Current))
        {
            Styles.Add(new StyleInclude(new Uri("avares://Deckle/")) { Source = new Uri($"avares://Deckle/{file}") });
        }
        if (PlatformLook.IsToolbarLook)
        {
            TitleCase.ApplyToMenus(); // Apple and GNOME both use title case in menus.
        }
    }

    public static string[] LookStyles(AppLook look) => look switch
    {
        AppLook.MacOS => ["ToolbarLookStyles.axaml", "MacStyles.axaml"],
        AppLook.Gnome => ["ToolbarLookStyles.axaml", "GnomeStyles.axaml"],
        _ => [],
    };

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new AppServices(AppPaths.Default());
            ApplyLook(services.Settings.Look);
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
            // The menu bar: macOS's own on a Mac (whatever the look), inside the window for the macOS look elsewhere.
            if (AppShortcuts.IsMac || PlatformLook.IsMac)
            {
                NativeMenu.SetMenu(this, MacMenuBar.AppMenu(vm, window.OpenSettingsCommand));
                NativeMenu.SetMenu(window, MacMenuBar.WindowMenu(vm, window, window.OpenHelpCommand,
                    OperatingSystem.IsMacOS() ? null : window.OpenSettingsCommand));
            }
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => vm.OnShutdown();
            RequestRestart = () => Dispatcher.UIThread.Post(() =>
            {
                vm.OnShutdown(); // Pages and settings are saved before the new copy reads them.
                var start = new System.Diagnostics.ProcessStartInfo(ScanShortcuts.ExePath) { UseShellExecute = false };
                start.ArgumentList.Add(Program.WaitForArgument + Environment.ProcessId);
                System.Diagnostics.Process.Start(start);
                desktop.Shutdown();
            });
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
            System.Diagnostics.Trace.WriteLine($"Deckle: {ex.Message}");
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
