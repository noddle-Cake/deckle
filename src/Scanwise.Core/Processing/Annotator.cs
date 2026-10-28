using NAPS2.Images;
using NAPS2.Images.ImageSharp;
using NAPS2.ImportExport;
using NAPS2.Scan;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Scanwise.Core.Processing;

/// <summary>
/// One pen or highlighter stroke. Points are fractions (0..1) of the page; <see cref="Width"/> is a fraction of the
/// page width, so strokes look the same at any preview size.
/// </summary>
public sealed record InkStroke(IReadOnlyList<(double X, double Y)> Points, uint Argb, double Width, bool Highlighter);

/// <summary>Burns ink strokes into a page (Scanner's draw and highlight).</summary>
public static class Annotator
{
    /// <summary>
    /// Renders the page with its current transforms, draws the strokes and returns a new page image. The result has
    /// no transforms of its own, because they are baked in.
    /// </summary>
    public static async Task<ProcessedImage> BurnAsync(
        ScanningContext scanning, ProcessedImage page, IReadOnlyList<InkStroke> strokes, string tempDir, CancellationToken cancelToken = default)
    {
        Directory.CreateDirectory(tempDir);
        var file = System.IO.Path.Combine(tempDir, $"ink-{Guid.NewGuid():N}.png");
        try
        {
            await Task.Run(() =>
            {
                using var rendered = scanning.ImageContext.Render(page);
                var dpiX = rendered.HorizontalResolution;
                var dpiY = rendered.VerticalResolution;
                using var image = ((ImageSharpImage)rendered).Image.CloneAs<Rgba32>();
                Draw(image, strokes);
                if (dpiX > 0 && dpiY > 0)
                {
                    image.Metadata.ResolutionUnits = SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerInch;
                    image.Metadata.HorizontalResolution = dpiX;
                    image.Metadata.VerticalResolution = dpiY;
                }
                using var rgb = image.CloneAs<Rgb24>();
                rgb.SaveAsPng(file);
            }, cancelToken).ConfigureAwait(false);

            await foreach (var result in new ImageImporter(scanning).Import(file, new ImportParams(), cancelToken).ConfigureAwait(false))
            {
                return result;
            }
            throw new InvalidOperationException("The annotated page could not be loaded.");
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    internal static void Draw(Image<Rgba32> image, IReadOnlyList<InkStroke> strokes)
    {
        var options = new DrawingOptions { GraphicsOptions = new GraphicsOptions { Antialias = true } };
        image.Mutate(ctx =>
        {
            foreach (var stroke in strokes.Where(s => s.Points.Count > 0))
            {
                var color = Color.FromRgba((byte)(stroke.Argb >> 16), (byte)(stroke.Argb >> 8), (byte)stroke.Argb, (byte)(stroke.Argb >> 24));
                var width = (float)Math.Max(1, stroke.Width * image.Width);
                var points = stroke.Points.Select(p => new PointF((float)(p.X * image.Width), (float)(p.Y * image.Height))).ToArray();
                if (points.Length == 1)
                {
                    ctx.Fill(options, color, new EllipsePolygon(points[0], width / 2));
                    continue;
                }
                // A highlighter keeps flat ends and blends with the page (multiply), so text stays readable.
                var pen = stroke.Highlighter
                    ? Pens.Solid(color, width)
                    : new SolidPen(new PenOptions(color, width) { JointStyle = JointStyle.Round, EndCapStyle = EndCapStyle.Round });
                var drawOptions = stroke.Highlighter
                    ? new DrawingOptions { GraphicsOptions = new GraphicsOptions { Antialias = true, ColorBlendingMode = PixelColorBlendingMode.Multiply } }
                    : options;
                ctx.DrawLine(drawOptions, pen, points);
            }
        });
    }
}
