using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NAPS2.Images;
using NAPS2.Images.ImageSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Scanwise.App.Imaging;

/// <summary>Renders pages (with their transforms) into Avalonia bitmaps on a background thread.</summary>
public sealed class PageRenderer
{
    private readonly ImageSharpImageContext _imageContext;
    private readonly ThumbnailRenderer _thumbnails;
    // Rendering is CPU-heavy; don't let a long page list saturate every core.
    private readonly SemaphoreSlim _throttle = new(Math.Max(1, Environment.ProcessorCount / 2));

    public PageRenderer(ImageSharpImageContext imageContext)
    {
        _imageContext = imageContext;
        _thumbnails = new ThumbnailRenderer(imageContext);
    }

    /// <summary>
    /// Renders <paramref name="image"/> scaled to fit <paramref name="maxSide"/>. The image is cloned first so it
    /// can't be disposed underneath us (e.g. by undo) while the render runs.
    /// </summary>
    public async Task<Bitmap?> RenderAsync(ProcessedImage image, int maxSide, CancellationToken cancelToken = default)
    {
        var copy = image.Clone();
        await _throttle.WaitAsync(cancelToken);
        try
        {
            return await Task.Run(async () =>
            {
                cancelToken.ThrowIfCancellationRequested();
                using var rendered = await _thumbnails.Render(copy, maxSide);
                return ToBitmap(rendered);
            }, cancelToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _throttle.Release();
            copy.Dispose();
        }
    }

    /// <summary>Full-size pixel dimensions of a page after its transforms.</summary>
    public async Task<PixelSize> MeasureAsync(ProcessedImage image)
    {
        var copy = image.Clone();
        try
        {
            return await Task.Run(() =>
            {
                using var rendered = _imageContext.Render(copy);
                return new PixelSize(rendered.Width, rendered.Height);
            });
        }
        finally
        {
            copy.Dispose();
        }
    }

    public static unsafe Bitmap ToBitmap(IMemoryImage image)
    {
        using var bgra = ((ImageSharpImage)image).Image.CloneAs<Bgra32>();
        var bitmap = new WriteableBitmap(new PixelSize(bgra.Width, bgra.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var buffer = bitmap.Lock();
        var width = bgra.Width;
        bgra.ProcessPixelRows(accessor =>
        {
            var rowBytes = width * 4;
            for (var y = 0; y < accessor.Height; y++)
            {
                var source = MemoryMarshal.AsBytes(accessor.GetRowSpan(y));
                source.CopyTo(new Span<byte>((byte*)buffer.Address + (long)y * buffer.RowBytes, rowBytes));
            }
        });
        return bitmap;
    }
}
