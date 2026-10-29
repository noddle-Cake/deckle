using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Scanwise.App.Services;
using Scanwise.App.Services.Platform;
using Scanwise.App.ViewModels;

namespace Scanwise.App.Views;

public partial class MainWindow : Window
{
    private SettingsWindow? _settingsWindow;
    private HelpWindow? _helpWindow;
    private readonly MacShell? _macShell;

    public MainWindow()
    {
        InitializeComponent();
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        OpenHelpCommand = new RelayCommand(OpenHelp);
        if (AppShortcuts.IsMac)
        {
            _macShell = UseMacShell();
        }
        Closed += (_, _) =>
        {
            _settingsWindow?.Close();
            _helpWindow?.Close();
        };
        Opened += (_, _) => UpdateMacDocumentState();
        Nav.ItemInvoked += OnNavItemInvoked;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                BindKeys(vm);
                vm.PropertyChanged += OnViewModelChanged;
                vm.UpdateLayout(Bounds.Width);
                ApplyLayout(vm);
                SyncNavSelection(vm);
            }
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>A Settings window on macOS (one at a time), the Settings page elsewhere.</summary>
    public ICommand OpenSettingsCommand { get; }

    /// <summary>A Help window on macOS (Help ▸ Scanwise Help), the Help page elsewhere.</summary>
    public ICommand OpenHelpCommand { get; }

    internal SettingsWindow? OpenSettingsWindow => _settingsWindow;
    internal HelpWindow? OpenHelpWindow => _helpWindow;
    internal MacShell? MacShell => _macShell;

    /// <summary>
    /// The macOS window (Toolbars, Sidebars and Materials HIG): no Fluent nav rail; the title bar and toolbar are
    /// one row; the scan options are a sidebar over the window's blurred, vibrant background.
    /// </summary>
    private MacShell UseMacShell()
    {
        var body = (Control)Nav.Content!;
        Nav.Content = null;
        var shell = new MacShell { Body = body };
        Content = shell;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaTitleBarHeightHint = 52;
        TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];
        Background = Brushes.Transparent;
        return shell;
    }

    private void OpenSettings()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }
        if (!AppShortcuts.IsMac)
        {
            vm.Section = Section.Settings;
            return;
        }
        _settingsWindow = ShowSingle(_settingsWindow, () => new SettingsWindow { DataContext = vm.SettingsPage }, () => _settingsWindow = null);
    }

    private void OpenHelp()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }
        if (!AppShortcuts.IsMac)
        {
            vm.Section = Section.Help;
            return;
        }
        _helpWindow = ShowSingle(_helpWindow, () => new HelpWindow(), () => _helpWindow = null);
    }

    /// <summary>Opens a window once; asking again brings the open one to the front.</summary>
    private static T ShowSingle<T>(T? open, Func<T> create, Action onClosed) where T : Window
    {
        if (open is null)
        {
            open = create();
            open.Closed += (_, _) => onClosed();
            open.Show();
        }
        open.Activate();
        return open;
    }

    /// <summary>macOS shows unsaved changes as the dot in the close button and the saved file as a proxy icon.</summary>
    private void UpdateMacDocumentState()
    {
        if (!OperatingSystem.IsMacOS() || !AppShortcuts.IsMac || ViewModel is not { } vm
            || TryGetPlatformHandle() is not { HandleDescriptor: "NSView", Handle: var view } || view == IntPtr.Zero)
        {
            return;
        }
        try
        {
            MacIntegration.SetDocumentState(view, vm.HasUnsavedChanges, vm.DocumentPath);
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            System.Diagnostics.Trace.WriteLine($"Scanwise: {ex.Message}");
        }
    }

    private void BindKeys(MainViewModel vm)
    {
        KeyBindings.Clear();
        AppShortcuts.Bind(this, AppShortcuts.Settings, OpenSettingsCommand);
        AppShortcuts.Bind(this, AppShortcuts.Scan, vm.ScanCommand);
        AppShortcuts.Bind(this, AppShortcuts.ScanNew, vm.ScanStartFreshCommand);
        AppShortcuts.Bind(this, AppShortcuts.Save, vm.SaveCommand);
        AppShortcuts.Bind(this, AppShortcuts.SaveAs, vm.SaveAsCommand);
        AppShortcuts.Bind(this, AppShortcuts.Import, vm.ImportCommand);
        AppShortcuts.Bind(this, AppShortcuts.Copy, vm.CopyPageCommand);
        AppShortcuts.Bind(this, AppShortcuts.Share, vm.ShareCommand);
        AppShortcuts.Bind(this, AppShortcuts.Undo, vm.UndoCommand);
        AppShortcuts.Bind(this, AppShortcuts.Redo, vm.RedoCommand);
        AppShortcuts.Bind(this, AppShortcuts.Cancel, vm.CancelOperationCommand);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && ViewModel is { } vm)
        {
            vm.UpdateLayout(Bounds.Width);
        }
        else if (change.Property == ActualTransparencyLevelProperty && _macShell is not null)
        {
            // Without blur (Reduce Transparency, or not supported) the sidebar gets a solid background instead.
            if (ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                this[!BackgroundProperty] = this.GetResourceObservable("PaneBackground").ToBinding();
            }
            else
            {
                Background = Brushes.Transparent;
            }
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }
        if (e.PropertyName is nameof(MainViewModel.Layout) or nameof(MainViewModel.Section) or nameof(MainViewModel.SidebarHidden))
        {
            ApplyLayout(vm);
            SyncNavSelection(vm);
        }
        else if (e.PropertyName is nameof(MainViewModel.HasUnsavedChanges) or nameof(MainViewModel.DocumentPath))
        {
            UpdateMacDocumentState();
        }
    }

    /// <summary>Wide: three columns. Medium: options + pages or editor. Narrow: one pane at a time.</summary>
    private void ApplyLayout(MainViewModel vm)
    {
        var columns = Workspace.ColumnDefinitions;
        var options = vm.ShowScanPane ? (vm.Layout == LayoutMode.Narrow ? GridLength.Star : new GridLength(340)) : new GridLength(0);
        GridLength pages;
        if (!vm.ShowPagesPane)
        {
            pages = new GridLength(0);
        }
        else
        {
            pages = vm.PagesPaneIsColumn ? new GridLength(260) : GridLength.Star;
        }
        var editor = vm.ShowEditorPane ? GridLength.Star : new GridLength(0);
        columns[0].Width = options;
        columns[1].Width = pages;
        columns[2].Width = editor;
        // A narrow window shows the scan options as the only pane, on the content background.
        _macShell?.SetSidebarWidth(vm.ShowScanPane && vm.Layout != LayoutMode.Narrow ? 340 : 0);
    }

    private void SyncNavSelection(MainViewModel vm)
    {
        Nav.SelectedItem = vm.Section switch
        {
            Section.Pages => NavPages,
            Section.Editor => NavEditor,
            Section.Settings => NavSettings,
            Section.Help => NavHelp,
            _ => NavScan,
        };
    }

    private void OnNavItemInvoked(object? sender, NavigationViewItemInvokedEventArgs e)
    {
        if (ViewModel is not { } vm || e.InvokedItemContainer is not NavigationViewItem item)
        {
            return;
        }
        switch (item.Tag as string)
        {
            case "Folder":
                vm.OpenSaveFolderCommand.Execute(null);
                SyncNavSelection(vm);
                break;
            case { } tag when Enum.TryParse<Section>(tag, out var section):
                vm.Section = section;
                break;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : e.DragEffects;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Handled || ViewModel is not { } vm || e.Data.GetFiles() is not { } items)
        {
            return;
        }
        var files = items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList();
        e.Handled = true;
        await vm.ImportFilesAsync(files);
    }
}
