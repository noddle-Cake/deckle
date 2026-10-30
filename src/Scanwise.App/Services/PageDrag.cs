using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Scanwise.App.ViewModels;

namespace Scanwise.App.Services;

/// <summary>
/// Drags pages out of the page grid or the editor. The drag carries the page ids, so dropping back on
/// the grid reorders, and an exported file, so another app or a folder receives the pages as a file.
/// </summary>
public static class PageDrag
{
    /// <summary>Comma-separated page ids; never leaves the process.</summary>
    public static readonly DataFormat<string> PageFormat = DataFormat.CreateInProcessFormat<string>("scanwise-pages");

    public static async Task StartAsync(PointerPressedEventArgs trigger, Visual source, MainViewModel vm, IReadOnlyList<PageItemViewModel> items)
    {
        var item = new DataTransferItem();
        item.Set(PageFormat, string.Join(",", items.Select(i => i.Id)));
        if (await vm.ExportForDragAsync(items) is { } path
            && TopLevel.GetTopLevel(source)?.StorageProvider is { } storage
            && await storage.TryGetFileFromPathAsync(new Uri(path)) is { } file)
        {
            item.SetFile(file);
        }
        var data = new DataTransfer();
        data.Add(item);
        await DragDrop.DoDragDropAsync(trigger, data, DragDropEffects.Copy | DragDropEffects.Move);
    }
}
