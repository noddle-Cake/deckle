using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Deckle.App.Imaging;
using Deckle.Core.Pages;

namespace Deckle.App.ViewModels;

public sealed partial class PageItemViewModel : ObservableObject
{
    public const int ThumbnailSize = 220;

    private readonly PageRenderer _renderer;
    private CancellationTokenSource? _renderCts;

    public PageItemViewModel(Page page, PageRenderer renderer)
    {
        _page = page;
        _renderer = renderer;
        _ = RenderAsync();
    }

    public Guid Id => Page.Id;

    [ObservableProperty] private Page _page;
    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private int _number;

    public string Label => $"Page {Number}";

    public string? Tooltip => Page.Info.ImportedFrom is { } file
        ? $"Imported from {Path.GetFileName(file)}"
        : Page.Info.ScannerName is { } scanner ? $"Scanned with {scanner} at {Page.Info.Dpi} dpi, {Page.Info.Time:g}" : null;

    partial void OnNumberChanged(int value) => OnPropertyChanged(nameof(Label));

    partial void OnPageChanged(Page? oldValue, Page newValue)
    {
        if (!ReferenceEquals(oldValue?.Image, newValue.Image))
        {
            _ = RenderAsync();
        }
        OnPropertyChanged(nameof(Tooltip));
    }

    private async Task RenderAsync()
    {
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        var bitmap = await _renderer.RenderAsync(Page.Image, ThumbnailSize, cts.Token);
        if (bitmap is not null && !cts.IsCancellationRequested)
        {
            var old = Thumbnail;
            Thumbnail = bitmap;
            old?.Dispose();
        }
    }

    public void Release()
    {
        _renderCts?.Cancel();
        Thumbnail?.Dispose();
        Thumbnail = null;
    }
}
