using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using Scanwise.App.Services;
using Scanwise.App.ViewModels;

namespace Scanwise.App.Views;

public partial class MainWindow : Window
{
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        // macOS opens Settings in its own window from the menu bar (⌘,), so the nav rail doesn't list it.
        NavSettings.IsVisible = !AppShortcuts.IsMac;
        Closed += (_, _) => _settingsWindow?.Close();
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

    internal SettingsWindow? OpenSettingsWindow => _settingsWindow;

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
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow { DataContext = vm.SettingsPage };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
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
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }
        if (e.PropertyName is nameof(MainViewModel.Layout) or nameof(MainViewModel.Section))
        {
            ApplyLayout(vm);
            SyncNavSelection(vm);
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
