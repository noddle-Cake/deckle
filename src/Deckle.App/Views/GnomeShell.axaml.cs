using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Deckle.App.Controls;
using Deckle.App.Services;
using Deckle.App.Services.Platform;
using Deckle.App.ViewModels;

namespace Deckle.App.Views;

/// <summary>The GNOME look's window: header bar as title bar, sidebar split view, primary menu.</summary>
public partial class GnomeShell : UserControl
{
    private readonly WindowButtons _startButtons;
    private readonly WindowButtons _endButtons;

    public GnomeShell()
    {
        InitializeComponent();
        var (start, end) = GnomeDesktop.ButtonLayout();
        _startButtons = new WindowButtons(WindowButtonStyle.Gnome, start);
        _endButtons = new WindowButtons(WindowButtonStyle.Gnome, end);
        HeaderEndButtons.Content = _endButtons;
        HeaderBar.PointerPressed += OnHeaderPressed;
        SidebarHeader.PointerPressed += OnHeaderPressed;
    }

    public object? Body
    {
        get => BodyHost.Content;
        set => BodyHost.Content = value;
    }

    public IEnumerable<string> StartButtonNames => _startButtons.ButtonNames;
    public IEnumerable<string> EndButtonNames => _endButtons.ButtonNames;

    /// <summary>
    /// The primary menu (GNOME HIG, Menus): app-wide actions, then "Preferences", "Keyboard Shortcuts", "Help" and
    /// "About Deckle" grouped at the end. No Quit or Close: the window has a close button.
    /// </summary>
    public void SetPrimaryMenu(MainViewModel vm, ICommand preferences, ICommand shortcuts, ICommand help)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Add(string header, ICommand command, string? shortcut = null) =>
            menu.Items.Add(new MenuItem { Header = header, Command = command, InputGesture = shortcut is null ? null : AppShortcuts.Gesture(shortcut) });
        Add("Save as…", vm.SaveAsCommand, AppShortcuts.SaveAs);
        Add("Open save folder", vm.OpenSaveFolderCommand);
        Add("Clear all pages…", vm.ClearAllCommand);
        menu.Items.Add(new Separator());
        Add("Preferences", preferences, AppShortcuts.Settings);
        Add("Keyboard shortcuts", shortcuts, AppShortcuts.KeyboardShortcuts);
        Add("Help", help, AppShortcuts.Help);
        Add("About Deckle", vm.ShowAboutCommand);
        PrimaryMenuButton.Flyout = menu;
    }

    internal MenuFlyout? PrimaryMenu => PrimaryMenuButton.Flyout as MenuFlyout;

    /// <summary>
    /// Follows the scan options column. The start-side window buttons sit in the sidebar's header while it shows,
    /// and move to the content header bar when it's hidden.
    /// </summary>
    public void SetSidebarWidth(double width)
    {
        Root.ColumnDefinitions[0].Width = new GridLength(width);
        SidebarHeader.IsVisible = SidebarBack.IsVisible = width > 0;
        var host = width > 0 ? SidebarStartButtons : HeaderStartButtons;
        var other = width > 0 ? HeaderStartButtons : SidebarStartButtons;
        if (host.Content != _startButtons)
        {
            other.Content = null;
            host.Content = _startButtons;
        }
    }

    /// <summary>Blank header bar space moves the window; double-click maximizes (GNOME's default action).</summary>
    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            window.BeginMoveDrag(e);
        }
        e.Handled = true;
    }
}
