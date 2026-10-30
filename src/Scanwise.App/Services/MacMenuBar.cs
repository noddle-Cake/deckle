using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Scanwise.App.ViewModels;

namespace Scanwise.App.Services;

/// <summary>
/// The macOS menu bar. Apple's HIG asks for the standard menus in the standard order (app, File, Edit, View,
/// app menus, Window, Help) and for every command, including every toolbar button, to be in the menu bar.
/// Items use the existing commands, so they enable and disable with them; headers are title case as on macOS.
/// </summary>
public static class MacMenuBar
{
    /// <summary>Items for the app menu; macOS adds Services, Hide, Show All and Quit around them.</summary>
    public static NativeMenu AppMenu(MainViewModel vm, ICommand openSettings) => [.. AppMenuItems(vm, openSettings)];

    private static NativeMenuItemBase[] AppMenuItems(MainViewModel vm, ICommand openSettings) =>
    [
        Item("About Scanwise", vm.ShowAboutCommand),
        new NativeMenuItemSeparator(),
        Item("Settings…", openSettings, AppShortcuts.Settings),
    ];

    /// <param name="openSettings">
    /// Given for the macOS look on Windows or Linux: the in-window menu bar has no system app menu, so a
    /// Scanwise menu leads with About, Settings and Quit; otherwise Settings would be unreachable.
    /// </param>
    public static NativeMenu WindowMenu(MainViewModel vm, Window window, ICommand openHelp, ICommand? openSettings = null)
    {
        var text = new TextCommands(window);
        var pages = vm.Pages;
        var editor = vm.Editor;
        var sidebar = Item(vm.SidebarMenuHeader, vm.ToggleSidebarCommand, AppShortcuts.ToggleSidebar);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SidebarMenuHeader))
            {
                sidebar.Header = vm.SidebarMenuHeader;
            }
        };
        NativeMenu menu =
        [
            Menu("File",
                Item("Scan", vm.ScanCommand, AppShortcuts.Scan),
                Item("Scan into New Document", vm.ScanStartFreshCommand, AppShortcuts.ScanNew),
                Item("Scan and Add to Current Document", vm.ScanAddToDocumentCommand),
                Item("Scan and Insert Between Pages…", vm.ScanMergeCommand),
                Item("Batch Scan…", vm.BatchScanCommand),
                Item("Preview Scan Area…", vm.PreviewCommand),
                new NativeMenuItemSeparator(),
                Item("Import…", vm.ImportCommand, AppShortcuts.Import),
                new NativeMenuItemSeparator(),
                Item("Save", vm.SaveCommand, AppShortcuts.Save),
                Item("Save As…", vm.SaveAsCommand, AppShortcuts.SaveAs),
                Item("Save Selected Pages As…", vm.SaveSelectionAsCommand),
                new NativeMenuItemSeparator(),
                Item("Open in Another App", vm.OpenSelectedExternallyCommand),
                Item("Share…", vm.ShareCommand, AppShortcuts.Share),
                Item("Show Save Folder in Finder", vm.OpenSaveFolderCommand),
                new NativeMenuItemSeparator(),
                Item("Close Window", new RelayCommand(window.Close), AppShortcuts.CloseWindow)),
            Menu("Edit",
                Item("Undo", text.Or(t => t.Undo(), vm.UndoCommand), AppShortcuts.Undo),
                Item("Redo", text.Or(t => t.Redo(), vm.RedoCommand), AppShortcuts.Redo),
                new NativeMenuItemSeparator(),
                Item("Cut", text.Or(t => t.Cut(), null), AppShortcuts.Cut),
                Item("Copy", text.Or(t => t.Copy(), vm.CopyPageCommand), AppShortcuts.Copy),
                Item("Paste", text.Or(t => t.Paste(), null), AppShortcuts.Paste),
                Item("Delete", text.Or(DeleteToLineStart, vm.DeleteSelectedCommand), AppShortcuts.Delete),
                Item("Select All", text.Or(t => t.SelectAll(), pages.SelectAllCommand), AppShortcuts.SelectAll),
                new NativeMenuItemSeparator(),
                Item("Duplicate", pages.DuplicateCommand, AppShortcuts.Duplicate),
                Item("Clear All Pages…", vm.ClearAllCommand)),
            Menu("View",
                sidebar,
                new NativeMenuItemSeparator(),
                Item("Scan Options", vm.NavigateCommand, AppShortcuts.ShowScan, Section.Scan),
                Item("Pages", vm.NavigateCommand, AppShortcuts.ShowPages, Section.Pages),
                Item("Editor", vm.NavigateCommand, AppShortcuts.ShowEditor, Section.Editor),
                new NativeMenuItemSeparator(),
                Item("Zoom In", editor.ZoomInCommand, AppShortcuts.ZoomIn),
                Item("Zoom Out", editor.ZoomOutCommand, AppShortcuts.ZoomOut),
                Item("Zoom to Fit", editor.ZoomFitCommand, AppShortcuts.ZoomFit),
                Item("Actual Size", editor.ZoomActualCommand),
                new NativeMenuItemSeparator(),
                Item("Previous Page", editor.PreviousCommand, AppShortcuts.PreviousPage),
                Item("Next Page", editor.NextCommand, AppShortcuts.NextPage),
                new NativeMenuItemSeparator(),
                Item("Enter Full Screen", new RelayCommand(() => window.WindowState =
                    window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen), AppShortcuts.FullScreen)),
            Menu("Page",
                Item("Rotate Left", pages.RotateLeftCommand, AppShortcuts.RotateLeft),
                Item("Rotate Right", pages.RotateRightCommand, AppShortcuts.RotateRight),
                Item("Rotate 180°", pages.Rotate180Command),
                new NativeMenuItemSeparator(),
                Item("Move Up", pages.MoveUpCommand, AppShortcuts.MoveUp),
                Item("Move Down", pages.MoveDownCommand, AppShortcuts.MoveDown),
                new NativeMenuItemSeparator(),
                Item("Crop", vm.CropPageCommand),
                Item("Adjust Colors", vm.AdjustPageCommand),
                Item("Draw and Highlight", vm.DrawOnPageCommand),
                Item("Undo Edits on Page", pages.ResetEditsCommand),
                new NativeMenuItemSeparator(),
                Menu("Page Order",
                    Item("Reverse", pages.ReverseCommand),
                    Item("Interleave (Fronts, Then Backs)", pages.InterleaveCommand),
                    Item("Interleave (Fronts, Then Backs Reversed)", pages.AltInterleaveCommand),
                    Item("Deinterleave", pages.DeinterleaveCommand),
                    Item("Deinterleave (Backs Reversed)", pages.AltDeinterleaveCommand))),
            Menu("Window",
                Item("Minimize", new RelayCommand(() => window.WindowState = WindowState.Minimized), AppShortcuts.Minimize),
                Item("Zoom", new RelayCommand(() => window.WindowState =
                    window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized)),
                new NativeMenuItemSeparator(),
                Item("Bring All to Front", new RelayCommand(window.Activate))),
            Menu("Help",
                Item("Scanwise Help", openHelp)),
        ];
        if (openSettings is not null)
        {
            var app = Menu("Scanwise", [.. AppMenuItems(vm, openSettings), new NativeMenuItemSeparator(),
                Item("Quit Scanwise", new RelayCommand(window.Close))]);
            menu.Items.Insert(0, app);
        }
        return menu;
    }

    private static NativeMenuItem Menu(string header, params NativeMenuItemBase[] items)
    {
        var menu = new NativeMenu();
        foreach (var item in items)
        {
            menu.Items.Add(item);
        }
        return new NativeMenuItem(header) { Menu = menu };
    }

    private static NativeMenuItem Item(string header, ICommand command, string? shortcut = null, object? parameter = null) =>
        new(header) { Command = command, CommandParameter = parameter, Gesture = shortcut is null ? null : AppShortcuts.Gesture(shortcut) };

    /// <summary>⌘⌫ in a text field deletes the selection, or everything before the caret on its line.</summary>
    private static void DeleteToLineStart(TextBox box)
    {
        if (box.SelectionStart != box.SelectionEnd)
        {
            box.SelectedText = "";
            return;
        }
        var text = box.Text ?? "";
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        box.Text = text.Remove(lineStart, caret - lineStart);
        box.CaretIndex = lineStart;
    }

    /// <summary>
    /// Edit menu items act on the focused text field when there is one (⌘C copies its text, not the page), and on
    /// the pages otherwise. Whether the menu or the text field sees a key first differs between AppKit setups, so
    /// both paths end up doing the same thing.
    /// </summary>
    private sealed class TextCommands
    {
        private readonly Window _window;
        private readonly List<ForwardCommand> _commands = [];

        public TextCommands(Window window)
        {
            _window = window;
            window.AddHandler(InputElement.GotFocusEvent, (_, _) => Refresh(), RoutingStrategies.Bubble, handledEventsToo: true);
            window.AddHandler(InputElement.LostFocusEvent, (_, _) => Refresh(), RoutingStrategies.Bubble, handledEventsToo: true);
        }

        private TextBox? FocusedText => _window.FocusManager?.GetFocusedElement() as TextBox;

        public ICommand Or(Action<TextBox> onText, ICommand? otherwise)
        {
            var command = new ForwardCommand(
                () => FocusedText is not null || otherwise?.CanExecute(null) == true,
                () =>
                {
                    if (FocusedText is { } box)
                    {
                        onText(box);
                    }
                    else
                    {
                        otherwise?.Execute(null);
                    }
                },
                otherwise);
            _commands.Add(command);
            return command;
        }

        private void Refresh()
        {
            foreach (var command in _commands)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>A command with its own logic that re-checks whenever <paramref name="follows"/> does.</summary>
    private sealed class ForwardCommand : ICommand
    {
        private readonly Func<bool> _canExecute;
        private readonly Action _execute;

        public ForwardCommand(Func<bool> canExecute, Action execute, ICommand? follows)
        {
            _canExecute = canExecute;
            _execute = execute;
            if (follows is not null)
            {
                follows.CanExecuteChanged += (_, _) => RaiseCanExecuteChanged();
            }
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute();

        public void Execute(object? parameter)
        {
            if (_canExecute())
            {
                _execute();
            }
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
