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
    public const string PageFormat = "application/x-scanwise-pages";

    public static async Task StartAsync(PointerEventArgs trigger, Visual source, MainViewModel vm, IReadOnlyList<PageItemViewModel> items)
    {
        var data = new DataObject();
        data.Set(PageFormat, string.Join(",", items.Select(i => i.Id)));
        if (await vm.ExportForDragAsync(items) is { } path
            && TopLevel.GetTopLevel(source)?.StorageProvider is { } storage
            && await storage.TryGetFileFromPathAsync(new Uri(path)) is { } file)
        {
            data.Set(DataFormats.Files, new[] { file });
#pragma warning disable CS0618 // Some targets still read the plain path list.
            data.Set(DataFormats.FileNames, new[] { path });
#pragma warning restore CS0618
        }
        await DragDrop.DoDragDrop(trigger, data, DragDropEffects.Copy | DragDropEffects.Move);
    }
}
