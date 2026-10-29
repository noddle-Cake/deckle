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
    ];

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
                // ⌃⌘F (full screen) is the one standard macOS shortcut that includes Control.
                if (action != AppShortcuts.FullScreen)
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
    public void MacWindowBindsCommandShortcutsAndHidesSettingsFromTheRail() => As(true, () =>
    {
        var (window, _) = Open();
        Assert.Contains(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.N, KeyModifiers.Meta)));
        Assert.Contains(window.KeyBindings, b => b.Gesture.Equals(new KeyGesture(Key.OemComma, KeyModifiers.Meta)));
        Assert.DoesNotContain(window.KeyBindings, b => b.Gesture.KeyModifiers.HasFlag(KeyModifiers.Control));
        Assert.False(window.FindControl<NavigationViewItem>("NavSettings")!.IsVisible);
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
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(settings.IsVisible);
        Assert.Null(window.OpenSettingsWindow);
    });

    [AvaloniaFact]
    public void MenuBarHasTheStandardMenusAndEveryToolbarCommand() => As(true, () =>
    {
        var (window, vm) = Open();
        var menu = MacMenuBar.WindowMenu(vm, window);
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

        Assert.All(items.Where(i => i.Gesture is not null && i.Header != "Enter Full Screen"),
            i => Assert.False(i.Gesture!.KeyModifiers.HasFlag(KeyModifiers.Control), i.Header));
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
        var menu = MacMenuBar.WindowMenu(vm, window);
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

    private (MainWindow Window, MainViewModel Vm) Open()
    {
        var services = new AppServices(new AppPaths(Path.Combine(_root, "config"), Path.Combine(_root, "data")));
        services.Settings.SaveFolder = Path.Combine(_root, "out");
        var window = new MainWindow { Width = 1200, Height = 800 };
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
