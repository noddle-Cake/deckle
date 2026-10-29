using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Scanwise.App.Services;
using Scanwise.App.ViewModels;
using Scanwise.App.Views;
using Scanwise.Core.Settings;

namespace Scanwise.App.Tests;

/// <summary>
/// macOS conventions from Apple's HIG (tasks.md 9.1), checked on any OS by switching AppShortcuts.IsMac.
/// Every test is a synchronous AvaloniaFact: they run on the single UI thread, so flipping the switch can't
/// leak into another test halfway through.
/// </summary>
public sealed class MacConventionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scanwise-mac-tests", Guid.NewGuid().ToString("N"));

    private static readonly string[] Actions =
    [
        AppShortcuts.Scan, AppShortcuts.ScanNew, AppShortcuts.Save, AppShortcuts.SaveAs, AppShortcuts.Import,
        AppShortcuts.Copy, AppShortcuts.Share, AppShortcuts.Undo, AppShortcuts.Redo, AppShortcuts.Delete,
        AppShortcuts.SelectAll, AppShortcuts.RotateLeft, AppShortcuts.RotateRight, AppShortcuts.MoveUp,
        AppShortcuts.MoveDown, AppShortcuts.ZoomIn, AppShortcuts.ZoomOut, AppShortcuts.ZoomFit,
        AppShortcuts.PreviousPage, AppShortcuts.NextPage, AppShortcuts.Settings, AppShortcuts.Cancel,
        AppShortcuts.Cut, AppShortcuts.Paste, AppShortcuts.Duplicate, AppShortcuts.ShowScan, AppShortcuts.ShowPages,
        AppShortcuts.ShowEditor, AppShortcuts.CloseWindow, AppShortcuts.Minimize, AppShortcuts.FullScreen,
        AppShortcuts.ToggleSidebar,
    ];

    /// <summary>The standard macOS shortcuts that do include Control: ⌃⌘F full screen and ⌃⌘S show/hide sidebar.</summary>
    private static readonly string[] ControlCommandShortcuts = [AppShortcuts.FullScreen, AppShortcuts.ToggleSidebar];

    private static void As(bool mac, Action body)
    {
        var was = AppShortcuts.IsMac;
        AppShortcuts.IsMac = mac;
        try
        {
            body();
        }
        finally
        {
            AppShortcuts.IsMac = was;
        }
    }

    [AvaloniaFact]
    public void MacShortcutsUseCommandNotControl() => As(true, () =>
    {
        foreach (var action in Actions)
        {
            foreach (var gesture in AppShortcuts.Gestures(action))
            {
                if (!ControlCommandShortcuts.Contains(action))
                {
                    Assert.False(gesture.KeyModifiers.HasFlag(KeyModifiers.Control), $"{action}: {gesture}");
                }
            }
        }
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Meta), AppShortcuts.Gesture(AppShortcuts.Save));
        Assert.Equal(new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift), AppShortcuts.Gesture(AppShortcuts.Redo));
        Assert.Equal(KeyModifiers.Meta, AppShortcuts.CommandModifier);
    });

    [AvaloniaFact]
    public void OtherPlatformsKeepCtrlShortcuts() => As(false, () =>
    {
        foreach (var action in Actions)
        {
            Assert.All(AppShortcuts.Gestures(action), g => Assert.False(g.KeyModifiers.HasFlag(KeyModifiers.Meta), action));
        }
        Assert.Equal("Ctrl+Shift+S", AppShortcuts.Label(AppShortcuts.SaveAs));
        Assert.Equal("Ctrl+Y", AppShortcuts.Label(AppShortcuts.Redo));
        Assert.Equal("Ctrl+E", AppShortcuts.Label(AppShortcuts.Share));
        Assert.Equal("Delete", AppShortcuts.Label(AppShortcuts.Delete));
        Assert.Null(AppShortcuts.Label(AppShortcuts.Settings));
        Assert.Equal("Save as… (Ctrl+Shift+S)", AppShortcuts.Tip("Save as…", AppShortcuts.SaveAs));
    });

    [AvaloniaFact]
    public void MacLabelsUseSymbolsInHigOrder() => As(true, () =>
    {
        Assert.Equal("⌘S", AppShortcuts.Label(AppShortcuts.Save));
        Assert.Equal("⇧⌘S", AppShortcuts.Label(AppShortcuts.SaveAs));
        Assert.Equal("⇧⌘Z", AppShortcuts.Label(AppShortcuts.Redo));
        Assert.Equal("⌘⌫", AppShortcuts.Label(AppShortcuts.Delete));
        Assert.Equal("⌥⌘↑", AppShortcuts.Label(AppShortcuts.MoveUp));
        Assert.Equal("⌘,", AppShortcuts.Label(AppShortcuts.Settings));
        Assert.Equal("⌃⌘F", AppShortcuts.Label(AppShortcuts.FullScreen));
        Assert.Equal("⌘2", AppShortcuts.Label(AppShortcuts.ShowPages));
        // ⌘E is "Use Selection for Find" on macOS, so Share has no shortcut.
        Assert.Null(AppShortcuts.Label(AppShortcuts.Share));
        Assert.Equal("Share", AppShortcuts.Tip("Share", AppShortcuts.Share));
    });

    [AvaloniaFact]
    public void MacWindowBindsCommandShortcutsAndDropsTheNavRail() => As(true, () =>
    {
        var (window, _) = Open();
        Assert.Contains(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.N, KeyModifiers.Meta)));
        Assert.Contains(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.OemComma, KeyModifiers.Meta)));
        Assert.DoesNotContain(window.KeyBindings, b => b.Gesture.KeyModifiers.HasFlag(KeyModifiers.Control));
        // Settings and Help are menu bar windows on macOS; the Fluent nav rail isn't in the window at all.
        Assert.IsType<MacShell>(window.Content);
        Assert.Null(window.FindControl<NavigationView>("Nav")!.Parent);
        window.Close();
    });

    [AvaloniaFact]
    public void SettingsOpensInItsOwnWindowOnMac() => As(true, () =>
    {
        var (window, vm) = Open();
        window.OpenSettingsCommand.Execute(null);
        window.OpenSettingsCommand.Execute(null); // A second ⌘, re-activates the same window.
        Dispatcher.UIThread.RunJobs();
        var settings = Assert.IsType<SettingsWindow>(window.OpenSettingsWindow);
        Assert.True(settings.IsVisible);
        Assert.Same(vm.SettingsPage, settings.DataContext);
        Assert.Equal(Section.Scan, vm.Section); // The workspace stays put.
        Assert.IsType<MacSettingsView>(settings.Content);
        Assert.False(settings.CanResize);
        Screenshot(settings, "mac-04-settings");
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(settings.IsVisible);
        Assert.Null(window.OpenSettingsWindow);
    });

    [AvaloniaFact]
    public void MenuBarHasTheStandardMenusAndEveryToolbarCommand() => As(true, () =>
    {
        var (window, vm) = Open();
        var menu = MacMenuBar.WindowMenu(vm, window, window.OpenHelpCommand);
        var top = menu.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList();
        // HIG order: (app menu) File, Edit, View, app-specific menus, Window, Help.
        Assert.Equal(["File", "Edit", "View", "Page", "Window", "Help"], top);

        var items = Flatten(menu).ToList();
        var commands = items.Select(i => i.Command).ToHashSet();
        // Page list and editor toolbars (Toolbars: "Make every toolbar item available as a command in the menu bar").
        Assert.Contains(vm.ImportCommand, commands);
        Assert.Contains(vm.Pages.RotateLeftCommand, commands);
        Assert.Contains(vm.Pages.RotateRightCommand, commands);
        Assert.Contains(vm.SaveCommand, commands);
        Assert.Contains(vm.SaveAsCommand, commands);
        Assert.Contains(vm.ShareCommand, commands);
        Assert.Contains(vm.ScanCommand, commands);
        Assert.Contains(vm.PreviewCommand, commands);
        Assert.Contains(vm.Editor.ZoomInCommand, commands);
        Assert.Contains(vm.Editor.PreviousCommand, commands);
        foreach (var header in new[] { "Copy", "Delete", "Undo", "Redo", "Select All", "Crop", "Adjust Colors", "Draw and Highlight" })
        {
            Assert.Contains(items, i => i.Header == header);
        }

        Assert.All(items.Where(i => i.Gesture is not null && i.Header is not ("Enter Full Screen" or "Hide Sidebar")),
            i => Assert.False(i.Gesture!.KeyModifiers.HasFlag(KeyModifiers.Control), i.Header));
        Assert.Contains(items, i => i.Header == "Hide Sidebar" && i.Command == vm.ToggleSidebarCommand);
        Assert.Equal(new KeyGesture(Key.Back, KeyModifiers.Meta), items.Single(i => i.Header == "Delete").Gesture);

        var app = Flatten(MacMenuBar.AppMenu(vm, window.OpenSettingsCommand)).ToList();
        Assert.Equal(["About Scanwise", "Settings…"], app.Select(i => i.Header));
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), app[1].Gesture);
        window.Close();
    });

    [AvaloniaFact]
    public void EditMenuActsOnTheFocusedTextField() => As(true, () =>
    {
        var (window, vm) = Open();
        var menu = MacMenuBar.WindowMenu(vm, window, window.OpenHelpCommand);
        var selectAll = Flatten(menu).Single(i => i.Header == "Select All").Command!;
        var box = new TextBox { Text = "Invoice" };
        window.Content = box;
        Dispatcher.UIThread.RunJobs();
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(selectAll.CanExecute(null));
        selectAll.Execute(null);
        Assert.Equal("Invoice", box.SelectedText);
        window.Close();
    });

    [AvaloniaFact]
    public void MacWindowUsesASidebarAndATitleBarToolbar() => As(true, () =>
    {
        var (window, vm) = Open(mac: true);
        var shell = Assert.IsType<MacShell>(window.Content);
        Assert.NotNull(window.MacShell);
        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.True(vm.SidebarVisible);
        Assert.Equal("Scanwise", window.Title); // No document yet; with pages it's the file name or "Untitled".
        Screenshot(window, "mac-01-wide");

        vm.ToggleSidebarCommand.Execute(null);
        Pump();
        Assert.False(vm.ShowScanPane);
        Assert.Equal("Show Sidebar", vm.SidebarMenuHeader);
        Screenshot(window, "mac-02-sidebar-hidden");
        vm.ToggleSidebarCommand.Execute(null);
        Assert.True(vm.ShowScanPane);

        // Narrow: the sidebar button swaps between the scan options and the pages; there is no top navigation.
        window.Width = 600;
        Pump();
        Assert.Equal(LayoutMode.Narrow, vm.Layout);
        Assert.True(vm.ShowScanPane);
        vm.ToggleSidebarCommand.Execute(null);
        Assert.Equal(Section.Pages, vm.Section);
        Assert.True(vm.ShowPagesPane);
        Assert.False(vm.ShowScanPane);
        Screenshot(window, "mac-03-narrow");

        window.OpenHelpCommand.Execute(null);
        Assert.NotNull(window.OpenHelpWindow);
        Assert.Equal(Section.Pages, vm.Section); // Help doesn't replace the workspace on macOS.
        window.Close();
        Pump();
        Assert.Null(window.OpenHelpWindow);
        _ = shell;
    });

    [Theory]
    [InlineData("Save as new profile…", "Save as New Profile…")]
    [InlineData("Save as…", "Save As…")]
    [InlineData("Open selected page in another app", "Open Selected Page in Another App")]
    [InlineData("Interleave (fronts, then backs reversed)", "Interleave (Fronts, Then Backs Reversed)")]
    [InlineData("Undo edits on page", "Undo Edits on Page")]
    [InlineData("Copy page to clipboard", "Copy Page to Clipboard")]
    [InlineData("Rotate 180°", "Rotate 180°")]
    [InlineData("Scan into New Document", "Scan into New Document")]
    [InlineData("Deinterleave (backs reversed)", "Deinterleave (Backs Reversed)")]
    public void MenuHeadersGetMacTitleCase(string sentence, string title) => Assert.Equal(title, TitleCase.Convert(sentence));

    [Theory]
    [InlineData("Save as… (⇧⌘S)", "Save as…")]
    [InlineData("Undo (Ctrl+Z)", "Undo")]
    [InlineData("Share", "Share")]
    public void ScreenReaderNamesDropTheShortcut(string tooltip, string name) => Assert.Equal(name, AccessibleName.Strip(tooltip));

    [AvaloniaFact]
    public void IconButtonsHaveScreenReaderNames()
    {
        var (window, _) = Open();
        var icons = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("icon") && b.IsEffectivelyVisible).ToList();
        Assert.NotEmpty(icons);
        Assert.All(icons, b => Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(b)), b.ToString()));
        Assert.Contains(icons, b => Avalonia.Automation.AutomationProperties.GetName(b) == "Import images or PDFs");
        window.Close();
    }

    [AvaloniaFact]
    public void MaximizedWindowRemembersItsNormalFrame()
    {
        var window = new Window { Width = 900, Height = 700 };
        window.Show();
        var normal = new WindowFrame { X = 100, Y = 80, Width = 900, Height = 700 };
        window.WindowState = WindowState.Maximized;
        var saved = WindowFrames.Capture(window, normal);
        Assert.True(saved.Maximized);
        Assert.Equal((100, 80, 900.0, 700.0), (saved.X, saved.Y, saved.Width, saved.Height));
        window.Close();

        // A frame on a screen that's gone is ignored, so the window can't open out of reach.
        var next = new Window { Width = 800, Height = 600 };
        WindowFrames.Restore(next, new WindowFrame { X = -50_000, Y = -50_000, Width = 1000, Height = 800 });
        Assert.Equal(800, next.Width);
        Assert.NotEqual(new PixelPoint(-50_000, -50_000), next.Position);
    }

    [AvaloniaFact]
    public void MacWindowKeepsTheToolbarUsableAtItsMinimumWidth() => As(true, () =>
    {
        var (window, vm) = Open(mac: true);
        Assert.Equal(560, window.MinWidth);
        Assert.False(window.FindControl<Border>("BusyBar")!.IsVisible); // Progress lives in the toolbar on macOS.
        window.Width = window.MinWidth;
        Pump();
        Assert.Equal(LayoutMode.Narrow, vm.Layout);
        Assert.True(vm.ShowScanPane);
        Assert.True(vm.ShowToolbarSidebarButton); // The only way back to the pages from here.
        Screenshot(window, "mac-05-minimum-width");
        window.Close();
    });

    [AvaloniaFact]
    public void MacDialogsPutCancelLeadingAndTheDefaultTrailing()
    {
        var (windows, primary, close) = ShowDialog(mac: false);
        Assert.True(Grid.GetColumn(primary) < Grid.GetColumn(close)); // Windows order, unchanged.
        windows.Close();

        (windows, primary, close) = ShowDialog(mac: true);
        Assert.True(Grid.GetColumn(close) < Grid.GetColumn(primary));
        windows.Close();
    }

    private static (Window Window, Button Primary, Button Close) ShowDialog(bool mac)
    {
        var window = new Window { Width = 600, Height = 400 };
        if (mac)
        {
            window.Styles.Add(new StyleInclude(new Uri("avares://Scanwise/")) { Source = new Uri("avares://Scanwise/MacStyles.axaml") });
        }
        window.Show();
        var dialog = new ContentDialog { Title = "Delete profile?", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        _ = dialog.ShowAsync(window);
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        var buttons = dialog.GetVisualDescendants().OfType<Button>().ToList();
        var primary = buttons.Single(b => b.Name == "PrimaryButton");
        var close = buttons.Single(b => b.Name == "CloseButton");
        Assert.True(primary.IsVisible && close.IsVisible);
        dialog.Hide();
        Dispatcher.UIThread.RunJobs();
        return (window, primary, close);
    }

    private static readonly string ScreenshotDir = Environment.GetEnvironmentVariable("SCANWISE_SCREENSHOTS") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetTempPath(), "scanwise-screenshots");

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

    private (MainWindow Window, MainViewModel Vm) Open(bool mac = false)
    {
        var services = new AppServices(new AppPaths(Path.Combine(_root, "config"), Path.Combine(_root, "data")));
        services.Settings.SaveFolder = Path.Combine(_root, "out");
        var window = new MainWindow { Width = 1200, Height = 800 };
        if (mac)
        {
            // App.Initialize ran without macOS styles in the test app; add them to this window.
            window.Styles.Add(new StyleInclude(new Uri("avares://Scanwise/")) { Source = new Uri("avares://Scanwise/MacStyles.axaml") });
        }
        var vm = new MainViewModel(services, new FakeUi());
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static IEnumerable<NativeMenuItem> Flatten(NativeMenu menu)
    {
        // Separators are NativeMenuItems too (header "-").
        foreach (var item in menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator))
        {
            yield return item;
            if (item.Menu is { } sub)
            {
                foreach (var child in Flatten(sub))
                {
                    yield return child;
                }
            }
        }
    }

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
