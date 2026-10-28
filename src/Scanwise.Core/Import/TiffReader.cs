using BitMiracle.LibTiff.Classic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Scanwise.Core.Import;

/// <summary>
/// Decodes TIFF files with LibTiff.NET and re-encodes each page as PNG. ImageSharp 3.1's TIFF decoder has known
/// memory-safety bugs (GHSA-v76p-62qx-wwq2, GHSA-wmxv-xphr-5c9g), so imported TIFFs never reach it.
/// </summary>
internal static class TiffReader
{
    public static bool IsTiff(string path)
    {
        Span<byte> header = stackalloc byte[4];
        using var stream = File.OpenRead(path);
        if (stream.Read(header) < 4)
        {
            return false;
        }
        // "II*\0" little-endian, "MM\0*" big-endian, plus BigTIFF (43 instead of 42).
        return (header[0] == 'I' && header[1] == 'I' && (header[2] == 42 || header[2] == 43) && header[3] == 0)
            || (header[0] == 'M' && header[1] == 'M' && header[2] == 0 && (header[3] == 42 || header[3] == 43));
    }

    /// <summary>Writes one PNG per TIFF page into <paramref name="tempDir"/> and returns the paths in page order.</summary>
    public static List<string> SplitToPng(string path, string tempDir, CancellationToken cancelToken)
    {
        Directory.CreateDirectory(tempDir);
        var output = new List<string>();
        Tiff.SetErrorHandler(new SilentErrorHandler());
        using var tiff = Tiff.Open(path, "r") ?? throw new InvalidDataException($"{Path.GetFileName(path)} is not a readable TIFF file.");
        var pages = tiff.NumberOfDirectories();
        for (short page = 0; page < pages; page++)
        {
            cancelToken.ThrowIfCancellationRequested();
            if (!tiff.SetDirectory(page))
            {
                break;
            }
            var width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            var height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            if (width <= 0 || height <= 0 || (long)width * height > 400_000_000)
            {
                throw new InvalidDataException("TIFF page dimensions are invalid or too large.");
            }
            var raster = new int[width * height];
            if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
            {
                throw new InvalidDataException($"Page {page + 1} of {Path.GetFileName(path)} could not be decoded.");
            }

            var bits = tiff.GetField(TiffTag.BITSPERSAMPLE)?[0].ToInt() ?? 8;
            var samples = tiff.GetField(TiffTag.SAMPLESPERPIXEL)?[0].ToInt() ?? 1;
            var grayscale = samples == 1 && bits <= 8;

            using var image = new Image<Rgba32>(width, height);
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < width; x++)
                    {
                        // LibTiff packs ABGR into an int: R in the low byte.
                        var abgr = raster[y * width + x];
                        row[x] = new Rgba32((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16), (byte)(abgr >> 24));
                    }
                }
            });
            SetResolution(tiff, image);

            var file = Path.Combine(tempDir, $"tiff-{Guid.NewGuid():N}-{page:D4}.png");
            if (grayscale)
            {
                using var gray = image.CloneAs<L8>();
                gray.SaveAsPng(file);
            }
            else
            {
                using var rgb = image.CloneAs<Rgb24>();
                rgb.SaveAsPng(file);
            }
            output.Add(file);
        }
        return output;
    }

    private static void SetResolution(Tiff tiff, Image image)
    {
        var x = tiff.GetField(TiffTag.XRESOLUTION)?[0].ToFloat() ?? 0;
        var y = tiff.GetField(TiffTag.YRESOLUTION)?[0].ToFloat() ?? 0;
        var unit = tiff.GetField(TiffTag.RESOLUTIONUNIT)?[0].ToInt() ?? (int)ResUnit.INCH;
        if (x <= 0 || y <= 0)
        {
            return;
        }
        if (unit == (int)ResUnit.CENTIMETER)
        {
            x *= 2.54f;
            y *= 2.54f;
        }
        image.Metadata.ResolutionUnits = SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerInch;
        image.Metadata.HorizontalResolution = x;
        image.Metadata.VerticalResolution = y;
    }

    private sealed class SilentErrorHandler : TiffErrorHandler
    {
        public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
        {
        }

        public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
        {
        }
    }
}
