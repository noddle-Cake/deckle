using NAPS2.Images;

namespace Deckle.Core.Export;

public enum OutputFormat { Pdf, Jpeg, Png, Tiff, Bmp }

public static class OutputFormats
{
    public static IReadOnlyList<OutputFormat> All { get; } = [OutputFormat.Pdf, OutputFormat.Jpeg, OutputFormat.Png, OutputFormat.Tiff, OutputFormat.Bmp];

    public static string Extension(this OutputFormat format) => format switch
    {
        OutputFormat.Pdf => ".pdf",
        OutputFormat.Jpeg => ".jpg",
        OutputFormat.Png => ".png",
        OutputFormat.Tiff => ".tif",
        OutputFormat.Bmp => ".bmp",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static string DisplayName(this OutputFormat format) => format switch
    {
        OutputFormat.Pdf => "PDF",
        OutputFormat.Jpeg => "JPG",
        OutputFormat.Png => "PNG",
        OutputFormat.Tiff => "TIF",
        OutputFormat.Bmp => "BMP",
        _ => format.ToString(),
    };

    public static string Description(this OutputFormat format) => format switch
    {
        OutputFormat.Pdf => "Document, all pages in one file",
        OutputFormat.Jpeg => "Photo, small files, one per page",
        OutputFormat.Png => "Lossless image, one per page",
        OutputFormat.Tiff => "Lossless, all pages in one file",
        OutputFormat.Bmp => "Uncompressed image, one per page",
        _ => "",
    };

    /// <summary>True when every page goes into a single file.</summary>
    public static bool IsMultiPage(this OutputFormat format) => format is OutputFormat.Pdf or OutputFormat.Tiff;

    public static IReadOnlyList<string> AllExtensions(this OutputFormat format) => format switch
    {
        OutputFormat.Jpeg => [".jpg", ".jpeg"],
        OutputFormat.Tiff => [".tif", ".tiff"],
        _ => [format.Extension()],
    };

    public static OutputFormat? FromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return All.Cast<OutputFormat?>().FirstOrDefault(f => f!.Value.AllExtensions().Contains(ext));
    }

    internal static ImageFileFormat ToImageFileFormat(this OutputFormat format) => format switch
    {
        OutputFormat.Jpeg => ImageFileFormat.Jpeg,
        OutputFormat.Png => ImageFileFormat.Png,
        OutputFormat.Bmp => ImageFileFormat.Bmp,
        OutputFormat.Tiff => ImageFileFormat.Tiff,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
