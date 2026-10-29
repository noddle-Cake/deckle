using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Scanwise.App.Services;
using Scanwise.App.ViewModels;

namespace Scanwise.App.Views;

/// <summary>Page grid. Code-behind handles drag-to-reorder, file drops and double-click to edit.</summary>
public partial class PageListView : UserControl
{
    private const string PageFormat = "application/x-scanwise-pages";
    private Point? _pressPoint;
    private PointerPressedEventArgs? _pressArgs;

    public PageListView()
    {
        InitializeComponent();
        List.AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        List.AddHandler(PointerMovedEvent, OnPointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        List.AddHandler(PointerReleasedEvent, (_, _) => _pressPoint = null, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        List.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        List.AddHandler(DragDrop.DropEvent, OnDrop);
        List.DoubleTapped += (_, e) =>
        {
            if (ViewModel is { } vm && ItemAt(e.GetPosition(List)) is { } item)
            {
                vm.OpenInEditor(item);
            }
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        List.KeyBindings.Clear();
        if (ViewModel is not { } vm)
        {
            return;
        }
        AppShortcuts.Bind(List, AppShortcuts.Delete, vm.DeleteSelectedCommand);
        AppShortcuts.Bind(List, AppShortcuts.SelectAll, vm.Pages.SelectAllCommand);
        AppShortcuts.Bind(List, AppShortcuts.RotateRight, vm.Pages.RotateRightCommand);
        AppShortcuts.Bind(List, AppShortcuts.RotateLeft, vm.Pages.RotateLeftCommand);
        AppShortcuts.Bind(List, AppShortcuts.MoveUp, vm.Pages.MoveUpCommand);
        AppShortcuts.Bind(List, AppShortcuts.MoveDown, vm.Pages.MoveDownCommand);
    }

    private PageItemViewModel? ItemAt(Point point) =>
        (List.InputHitTest(point) as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as PageItemViewModel;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(List).Properties.IsLeftButtonPressed && ItemAt(e.GetPosition(List)) is not null)
        {
            _pressPoint = e.GetPosition(List);
            _pressArgs = e;
        }
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressPoint is not { } start || _pressArgs is null || ViewModel is not { } vm)
        {
            return;
        }
        var delta = e.GetPosition(List) - start;
        if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8)
        {
            return;
        }
        _pressPoint = null;
        var item = ItemAt(start);
        if (item is null)
        {
            return;
        }
        // Drag the whole selection if the pressed page is part of it, otherwise just that page.
        var ids = vm.Pages.SelectedItems.Contains(item) ? vm.Pages.SelectedIds : [item.Id];
        var data = new DataObject();
        data.Set(PageFormat, string.Join(",", ids));
        await DragDrop.DoDragDrop(_pressArgs, data, DragDropEffects.Move);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(PageFormat) ? DragDropEffects.Move
            : e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }
        e.Handled = true;
        var index = DropIndex(e.GetPosition(List), vm);
        if (e.Data.Get(PageFormat) is string idList)
        {
            var ids = idList.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
            vm.Pages.MoveTo(ids, index);
        }
        else if (e.Data.GetFiles() is { } files)
        {
            await vm.ImportFilesAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList(), index);
        }
    }

    /// <summary>Insert position for a drop: before the hovered page, or after it when over its right/bottom half.</summary>
    private int DropIndex(Point point, MainViewModel vm)
    {
        var container = (List.InputHitTest(point) as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (container?.DataContext is not PageItemViewModel item)
        {
            return vm.Pages.Items.Count;
        }
        var index = vm.Pages.Items.IndexOf(item);
        var local = List.TranslatePoint(point, container) ?? default;
        var after = container.Bounds.Width > container.Bounds.Height * 1.5
            ? local.Y > container.Bounds.Height / 2
            : local.X > container.Bounds.Width / 2;
        return after ? index + 1 : index;
    }
}
