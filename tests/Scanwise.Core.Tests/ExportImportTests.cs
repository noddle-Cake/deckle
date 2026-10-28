using BitMiracle.LibTiff.Classic;
using NAPS2.Images.ImageSharp;
using NAPS2.Images.Transforms;
using Scanwise.Core.Export;
using Scanwise.Core.Import;
using Scanwise.Core.Pages;
using Scanwise.Core.Processing;
using Scanwise.Core.Settings;
using SixLabors.ImageSharp;

namespace Scanwise.Core.Tests;

public class ExportImportTests
{
    private static ExportService CreateExporter(TestEnv env) =>
        new(env.Context.Scanning, (ImageSharpImageContext)env.Context.ImageContext, new TesseractLocator(env.Paths));

    [Fact]
    public async Task PdfContainsEveryPageAndImportsBack()
    {
        using var env = new TestEnv();
        var exporter = CreateExporter(env);
        var pages = new[] { env.CreateImage(), env.CreateImage(300, 200), env.CreateImage() };
        var path = Path.Combine(env.OutputDir, "doc.pdf");

        await exporter.ExportDocumentAsync(pages, path, OutputFormat.Pdf, new AppSettings { Pdf = { Title = "Test" } });

        Assert.StartsWith("%PDF", File.ReadAllText(path)[..4]);
        Assert.Empty(Directory.GetFiles(env.OutputDir, "*.tmp"));
        var imported = await new ImportService(env.Context.Scanning, env.Paths.TempDir).ImportAsync(path).ToListAsync();
        Assert.Equal(3, imported.Count);
    }

    [Fact]
    public async Task TiffKeepsPagesOfDifferentSizesAndImportsBack()
    {
        using var env = new TestEnv();
        var exporter = CreateExporter(env);
        var rotated = env.CreateImage(200, 300).WithTransform(new RotationTransform(90), disposeSelf: true);
        var bw = env.CreateImage(120, 80).WithTransform(new BlackWhiteTransform(0), disposeSelf: true);
        var pages = new[] { env.CreateImage(200, 300), rotated, bw };
        var path = Path.Combine(env.OutputDir, "doc.tif");

        await exporter.ExportDocumentAsync(pages, path, OutputFormat.Tiff, new AppSettings());

        using (var tiff = Tiff.Open(path, "r"))
        {
            Assert.Equal(3, tiff.NumberOfDirectories());
            tiff.SetDirectory(1);
            Assert.Equal(300, tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt());
            tiff.SetDirectory(2);
            Assert.Equal(1, tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt());
            Assert.Equal(Compression.CCITTFAX4, (Compression)tiff.GetField(TiffTag.COMPRESSION)[0].ToInt());
        }

        var imported = await new ImportService(env.Context.Scanning, env.Paths.TempDir).ImportAsync(path).ToListAsync();
        Assert.Equal(3, imported.Count);
        Assert.Empty(Directory.GetFiles(env.Paths.TempDir, "tiff-*"));
        // The page must still render after the temp PNGs are gone.
        using var rendered = env.Context.ImageContext.Render(imported[1].Image);
        Assert.Equal(300, rendered.Width);
        Assert.Equal(200, rendered.Height);
    }

    [Theory]
    [InlineData(OutputFormat.Jpeg)]
    [InlineData(OutputFormat.Png)]
    [InlineData(OutputFormat.Bmp)]
    [InlineData(OutputFormat.Tiff)]
    public async Task SinglePageFormatsWriteDecodableFiles(OutputFormat format)
    {
        using var env = new TestEnv();
        var exporter = CreateExporter(env);
        var pages = new[] { env.CreateImage(200, 300), env.CreateImage(300, 200) };
        var paths = new[] { Path.Combine(env.OutputDir, "a" + format.Extension()), Path.Combine(env.OutputDir, "b" + format.Extension()) };

        await exporter.ExportPagesAsync(pages, paths, format, new AppSettings());

        if (format == OutputFormat.Tiff)
        {
            using var tiff = Tiff.Open(paths[1], "r");
            Assert.Equal(300, tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt());
            return;
        }
        var info = Image.Identify(paths[1]);
        Assert.Equal(300, info.Width);
        Assert.Equal(200, info.Height);
    }

    [Fact]
    public async Task EncryptedPdfIsWritten()
    {
        using var env = new TestEnv();
        var exporter = CreateExporter(env);
        var path = Path.Combine(env.OutputDir, "secret.pdf");
        await exporter.ExportDocumentAsync([env.CreateImage()], path, OutputFormat.Pdf,
            new AppSettings { Pdf = { Encrypt = true, UserPassword = "pw", AllowCopying = false } });
        Assert.Contains("/Encrypt", File.ReadAllText(path));
    }

    [SkippableFact]
    public async Task SearchablePdfContainsRecognizedText()
    {
        using var env = new TestEnv();
        var locator = new TesseractLocator(env.Paths);
        Skip.IfNot(locator.Find()?.HasLanguage("eng") == true, "Tesseract with English data is not installed");
        var exporter = new ExportService(env.Context.Scanning, (ImageSharpImageContext)env.Context.ImageContext, locator);
        var page = env.ImportFirst(env.CreateTextImage(0));
        var path = Path.Combine(env.OutputDir, "ocr.pdf");

        await exporter.ExportDocumentAsync([page], path, OutputFormat.Pdf, new AppSettings { Pdf = { Ocr = true }, OcrLanguage = "eng" });

        // NAPS2 writes the OCR layer as invisible text; the font resource is a reliable marker.
        var raw = File.ReadAllText(path);
        Assert.True(raw.Contains("/Font") || raw.Contains("/F1"), "PDF has no text layer");
    }

    [Fact]
    public async Task SessionRoundTripsPagesAndTransforms()
    {
        using var env = new TestEnv();
        var store = new SessionStore(env.Paths.SessionDir, env.Context.Scanning);
        var page = env.CreatePage(200, 300);
        var rotated = page.WithImage(page.Image.WithTransform(new RotationTransform(90), false)
            .WithTransform(new CropTransform(10, 0, 0, 20, 300, 200), true));

        await store.SaveAsync([rotated, env.CreatePage()]);
        Assert.True(store.HasSavedSession);

        var restored = await store.RestoreAsync();
        Assert.Equal(2, restored.Count);
        Assert.Equal(rotated.Id, restored[0].Id);
        Assert.Equal(2, restored[0].Image.TransformState.Transforms.Count);
        using var rendered = env.Context.ImageContext.Render(restored[0].Image);
        Assert.Equal(290, rendered.Width);
        Assert.Equal(180, rendered.Height);

        await store.SaveAsync([restored[1]]);
        Assert.Equal(2, Directory.GetFiles(env.Paths.SessionDir).Length); // index + one image
        store.Clear();
        Assert.False(store.HasSavedSession);
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }
        return list;
    }
}

public class AnnotatorTests
{
    [Fact]
    public async Task StrokesAreBurnedIntoTheRenderedPage()
    {
        using var env = new TestEnv();
        var page = env.CreateImage(200, 300, new SixLabors.ImageSharp.PixelFormats.Rgb24(255, 255, 255))
            .WithTransform(new NAPS2.Images.Transforms.RotationTransform(90), disposeSelf: true);
        var strokes = new[]
        {
            new Scanwise.Core.Processing.InkStroke([(0.5, 0.9), (0.9, 0.9)], 0xFFFF0000, 0.05, false),
        };

        using var burned = await Scanwise.Core.Processing.Annotator.BurnAsync(env.Context.Scanning, page, strokes, env.Paths.TempDir);

        Assert.True(burned.TransformState.IsEmpty);
        using var rendered = env.Context.ImageContext.Render(burned);
        Assert.Equal((300, 200), (rendered.Width, rendered.Height));
        var pixels = ((NAPS2.Images.ImageSharp.ImageSharpImage)rendered).Image.CloneAs<SixLabors.ImageSharp.PixelFormats.Rgb24>();
        var red = pixels[210, 180];
        Assert.True(red.R > 200 && red.G < 60, $"expected red ink, got {red}");
        page.Dispose();
    }
}
