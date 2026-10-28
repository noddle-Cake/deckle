using NAPS2.Images;
using Scanwise.Core.Scanning;

namespace Scanwise.Core.Pages;

/// <summary>Where a page came from; used for file naming and shown in the page info.</summary>
public sealed record PageInfo(
    DateTime Time,
    string? ScannerName = null,
    int? Dpi = null,
    ColorMode? Color = null,
    ScanSource? Source = null,
    int? Brightness = null,
    int? Contrast = null,
    string? ImportedFrom = null)
{
    public static PageInfo FromScan(ScanSettings settings, string? scannerName, DateTime time) =>
        new(time, scannerName, settings.Dpi, settings.Color, settings.Source, settings.Brightness, settings.Contrast);

    public static PageInfo FromImport(string path) => new(DateTime.Now, ImportedFrom: path);
}

/// <summary>
/// One page. Immutable: edits produce a new <see cref="Page"/> with the same <see cref="Id"/> and a new image,
/// which is what lets undo keep old versions around cheaply.
/// </summary>
public sealed class Page
{
    public Page(ProcessedImage image, PageInfo info)
        : this(Guid.NewGuid(), image, info)
    {
    }

    public Page(Guid id, ProcessedImage image, PageInfo info)
    {
        Id = id;
        Image = image;
        Info = info;
    }

    public Guid Id { get; }
    public ProcessedImage Image { get; }
    public PageInfo Info { get; }

    public Page WithImage(ProcessedImage image) => new(Id, image, Info);
}
