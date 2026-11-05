using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using NAPS2.Images;
using NAPS2.Images.ImageSharp;
using Deckle.Core.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Deckle.Core.Export;

/// <summary>
/// Multi-page TIFF writer. NAPS2's ImageSharp backend has no TIFF writer and ImageSharp requires every frame to
/// have the same size, so pages are written with LibTiff.NET instead.
/// </summary>
internal static class TiffWriter
{
    public static void Write(
        string path,
        IReadOnlyList<ProcessedImage> pages,
        ImageSharpImageContext imageContext,
        TiffCompression compression,
        Action<int>? pageDone,
        CancellationToken cancelToken)
    {
        using var tiff = Tiff.Open(path, "w") ?? throw new IOException($"Could not create {path}");
        for (var i = 0; i < pages.Count; i++)
        {
            cancelToken.ThrowIfCancellationRequested();
            using var memoryImage = imageContext.Render(pages[i]);
            var logical = memoryImage.LogicalPixelFormat;
            var image = ((ImageSharpImage)memoryImage).Image;
            var dpiX = memoryImage.HorizontalResolution > 0 ? memoryImage.HorizontalResolution : 300;
            var dpiY = memoryImage.VerticalResolution > 0 ? memoryImage.VerticalResolution : 300;

            tiff.SetField(TiffTag.IMAGEWIDTH, image.Width);
            tiff.SetField(TiffTag.IMAGELENGTH, image.Height);
            tiff.SetField(TiffTag.XRESOLUTION, dpiX);
            tiff.SetField(TiffTag.YRESOLUTION, dpiY);
            tiff.SetField(TiffTag.RESOLUTIONUNIT, ResUnit.INCH);
            tiff.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
            tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);
            tiff.SetField(TiffTag.SUBFILETYPE, FileType.PAGE);
            tiff.SetField(TiffTag.PAGENUMBER, i, pages.Count);

            switch (logical)
            {
                case ImagePixelFormat.BW1:
                    WriteBilevel(tiff, image, compression);
                    break;
                case ImagePixelFormat.Gray8:
                    WriteGray(tiff, image, compression);
                    break;
                default:
                    WriteRgb(tiff, image, compression);
                    break;
            }
            tiff.WriteDirectory();
            pageDone?.Invoke(i + 1);
        }
    }

    private static Compression Pick(TiffCompression wanted, bool bilevel) => wanted switch
    {
        TiffCompression.None => Compression.NONE,
        TiffCompression.Lzw => Compression.LZW,
        TiffCompression.Ccitt4 when bilevel => Compression.CCITTFAX4,
        // CCITT only works for 1-bit images; fall back to LZW for gray/color.
        TiffCompression.Ccitt4 => Compression.LZW,
        _ => bilevel ? Compression.CCITTFAX4 : Compression.LZW,
    };

    private static void WriteRgb(Tiff tiff, Image image, TiffCompression compression)
    {
        using var rgb = image.CloneAs<Rgb24>();
        tiff.SetField(TiffTag.SAMPLESPERPIXEL, 3);
        tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
        tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
        tiff.SetField(TiffTag.COMPRESSION, Pick(compression, false));
        tiff.SetField(TiffTag.ROWSPERSTRIP, tiff.DefaultStripSize(0));
        var row = new byte[rgb.Width * 3];
        rgb.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                MemoryMarshal.AsBytes(accessor.GetRowSpan(y)).CopyTo(row);
                tiff.WriteScanline(row, y);
            }
        });
    }

    private static void WriteGray(Tiff tiff, Image image, TiffCompression compression)
    {
        using var gray = image.CloneAs<L8>();
        tiff.SetField(TiffTag.SAMPLESPERPIXEL, 1);
        tiff.SetField(TiffTag.BITSPERSAMPLE, 8);
        tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISBLACK);
        tiff.SetField(TiffTag.COMPRESSION, Pick(compression, false));
        tiff.SetField(TiffTag.ROWSPERSTRIP, tiff.DefaultStripSize(0));
        var row = new byte[gray.Width];
        gray.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                MemoryMarshal.AsBytes(accessor.GetRowSpan(y)).CopyTo(row);
                tiff.WriteScanline(row, y);
            }
        });
    }

    private static void WriteBilevel(Tiff tiff, Image image, TiffCompression compression)
    {
        using var gray = image.CloneAs<L8>();
        tiff.SetField(TiffTag.SAMPLESPERPIXEL, 1);
        tiff.SetField(TiffTag.BITSPERSAMPLE, 1);
        tiff.SetField(TiffTag.PHOTOMETRIC, Photometric.MINISWHITE);
        tiff.SetField(TiffTag.COMPRESSION, Pick(compression, true));
        tiff.SetField(TiffTag.ROWSPERSTRIP, gray.Height);
        var row = new byte[(gray.Width + 7) / 8];
        gray.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                Array.Clear(row);
                var span = accessor.GetRowSpan(y);
                for (var x = 0; x < span.Length; x++)
                {
                    // MINISWHITE: a set bit is black.
                    if (span[x].PackedValue < 128)
                    {
                        row[x >> 3] |= (byte)(0x80 >> (x & 7));
                    }
                }
                tiff.WriteScanline(row, y);
            }
        });
    }
}
