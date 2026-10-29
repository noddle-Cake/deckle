using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Scanwise.App.Controls;
using Scanwise.App.Services;
using Scanwise.App.Services.Platform;
using Scanwise.App.ViewModels;
using Scanwise.App.Views;
using Scanwise.Core.Settings;

namespace Scanwise.App.Tests;

/// <summary>
/// Settings ▸ Look (tasks.md phases 10–12): Windows, macOS and GNOME layouts, chosen independently of the OS.
/// Synchronous AvaloniaFacts on the UI thread, so switching PlatformLook can't leak into another test.
/// </summary>
public sealed class LookTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scanwise-look-tests", Guid.NewGuid().ToString("N"));

    private static readonly string ScreenshotDir = Environment.GetEnvironmentVariable("SCANWISE_SCREENSHOTS") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetTempPath(), "scanwise-screenshots");

    private static void In(AppLook look, Action body)
    {
        var was = PlatformLook.Current;
        PlatformLook.Current = look;
        try
        {
            body();
        }
        finally
        {
            PlatformLook.Current = was;
        }
    }

    private (MainWindow Window, MainViewModel Vm, AppServices Services) Open(AppLook look, double width = 1200)
    {
        var services = new AppServices(new AppPaths(Path.Combine(_root, "config"), Path.Combine(_root, "data")));
        services.Settings.SaveFolder = Path.Combine(_root, "out");
        var window = new MainWindow { Width = width, Height = 800 };
        LookStyles.AddTo(window, look);
        var vm = new MainViewModel(services, new FakeUi());
        window.DataContext = vm;
        window.Show();
        Pump();
        return (window, vm, services);
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static void Screenshot(Window window, string name)
    {
        Directory.CreateDirectory(ScreenshotDir);
        Pump();
        window.CaptureRenderedFrame()?.Save(Path.Combine(ScreenshotDir, name + ".png"));
    }

    [Fact]
    public void AutomaticFollowsTheOperatingSystem()
    {
        var expected = OperatingSystem.IsMacOS() ? AppLook.MacOS : OperatingSystem.IsWindows() ? AppLook.Windows : AppLook.Gnome;
        Assert.Equal(expected, PlatformLook.Resolve(AppLook.Automatic));
        Assert.Equal(AppLook.MacOS, PlatformLook.Resolve(AppLook.MacOS));
        Assert.Equal(AppLook.Gnome, PlatformLook.Resolve(AppLook.Gnome));
    }

    [Fact]
    public void LookSettingIsSaved()
    {
        var paths = new AppPaths(Path.Combine(_root, "config"), Path.Combine(_root, "data"));
        using (var services = new AppServices(paths))
        {
            services.Settings.Look = AppLook.Gnome;
            services.SaveSettings();
        }
        using var again = new AppServices(paths);
        Assert.Equal(AppLook.Gnome, again.Settings.Look);
    }

    [Theory]
    [InlineData("appmenu:close", new string[0], new[] { "close" })]
    [InlineData("icon:minimize,maximize,close", new string[0], new[] { "minimize", "maximize", "close" })]
    [InlineData("close,minimize,maximize:", new[] { "close", "minimize", "maximize" }, new string[0])]
    [InlineData("close:appmenu", new[] { "close" }, new string[0])]
    public void GnomeButtonLayoutIsFollowed(string layout, string[] start, string[] end)
    {
        var (s, e) = GnomeDesktop.ParseButtonLayout(layout);
        Assert.Equal(start, s);
        Assert.Equal(end, e);
    }

    [AvaloniaFact]
    public void GnomeLookHasAHeaderBarInsteadOfATitleBar() => In(AppLook.Gnome, () =>
    {
        var (window, vm, _) = Open(AppLook.Gnome);
        var shell = Assert.IsType<GnomeShell>(window.GnomeShell);
        Assert.Equal(SystemDecorations.None, window.SystemDecorations); // The header bar is the title bar.
        Assert.Null(window.FindControl<FluentAvalonia.UI.Controls.NavigationView>("Nav")!.Parent);
        Assert.Contains(window.GetVisualDescendants(), v => v is WindowButtons);
        Assert.NotEmpty(shell.StartButtonNames.Concat(shell.EndButtonNames));
        Screenshot(window, "gnome-01-wide");

        // Primary menu: the four standard items, grouped last, and no Quit/Close.
        var headers = shell.PrimaryMenu!.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToList();
        Assert.Equal(["Preferences", "Keyboard shortcuts", "Help", "About Scanwise"], headers[^4..]);
        Assert.DoesNotContain(headers, h => h.Contains("Quit") || h.Contains("Close"));

        // GNOME keys: Ctrl+, Preferences, Ctrl+? shortcuts, F1 Help, F5 refresh, Ctrl+W close.
        Assert.Contains(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.OemComma, KeyModifiers.Control)));
        Assert.Contains(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.OemQuestion, KeyModifiers.Control | KeyModifiers.Shift)));
        Assert.Contains(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.F1)));
        Assert.Contains(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.F5)));
        Assert.Contains(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.W, KeyModifiers.Control)));

        // Page actions stay with their panes (no menu bar to hold them); Scan lives in the header bar.
        Assert.True(PlatformLook.HasPaneActions);

        // Preferences and Keyboard Shortcuts open as their own windows; the workspace stays.
        window.OpenSettingsCommand.Execute(null);
        var prefs = Assert.IsType<SettingsWindow>(window.OpenSettingsWindow);
        Assert.Equal("Preferences", prefs.Title);
        Assert.IsType<SettingsView>(prefs.Content);
        Assert.Equal(Section.Scan, vm.Section);
        LookStyles.AddTo(prefs, AppLook.Gnome);
        Screenshot(prefs, "gnome-03-preferences");
        window.OpenShortcutsCommand.Execute(null);

        window.Width = 640;
        Pump();
        Assert.Equal(LayoutMode.Narrow, vm.Layout);
        vm.ToggleSidebarCommand.Execute(null);
        Screenshot(window, "gnome-02-narrow");
        window.Close();
    });

    [AvaloniaFact]
    public void WindowsLookKeepsTheFluentShell() => In(AppLook.Windows, () =>
    {
        var (window, _, _) = Open(AppLook.Windows);
        Assert.Null(window.MacShell);
        Assert.Null(window.GnomeShell);
        Assert.NotNull(window.FindControl<FluentAvalonia.UI.Controls.NavigationView>("Nav")!.Parent);
        Assert.Contains(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.F1)));
        Assert.DoesNotContain(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.OemComma, KeyModifiers.Control)));
        window.Close();
    });

    [AvaloniaFact]
    public void ChangingTheLookAsksForARestart() => In(AppLook.Windows, () =>
    {
        var (window, vm, services) = Open(AppLook.Windows);
        var settings = vm.SettingsPage;
        Assert.False(settings.LookNeedsRestart);
        Assert.StartsWith("Automatic (", settings.Looks[0].Label);
        settings.Look = settings.Looks.First(l => l.Value == AppLook.MacOS);
        Assert.True(settings.LookNeedsRestart);
        Assert.Equal(AppLook.MacOS, services.Settings.Look);
        window.Close();
    });

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }
}
