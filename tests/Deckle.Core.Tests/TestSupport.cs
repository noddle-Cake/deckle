using NAPS2.Images;
using NAPS2.ImportExport;
using Deckle.Core;
using Deckle.Core.Pages;
using Deckle.Core.Settings;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Deckle.Core.Tests;

/// <summary>A throwaway app home (config, data, output) plus a NAPS2 context, deleted after the test.</summary>
public sealed class TestEnv : IDisposable
{
    public TestEnv()
    {
        Root = Path.Combine(Path.GetTempPath(), "deckle-tests", Guid.NewGuid().ToString("N"));
        Paths = new AppPaths(Path.Combine(Root, "config"), Path.Combine(Root, "data"));
        Context = new DeckleContext(Paths);
        OutputDir = Path.Combine(Root, "out");
        Directory.CreateDirectory(OutputDir);
    }

    public string Root { get; }
    public AppPaths Paths { get; }
    public DeckleContext Context { get; }
    public string OutputDir { get; }

    public ProcessedImage CreateImage(int width = 200, int height = 300, Rgb24? color = null)
    {
        var file = Path.Combine(Root, $"src-{Guid.NewGuid():N}.png");
        using (var image = new Image<Rgb24>(width, height, color ?? new Rgb24(240, 240, 240)))
        {
            image.Mutate(c => c.Fill(Color.DarkBlue, new RectangleF(10, 10, width / 3f, height / 4f)));
            image.Metadata.HorizontalResolution = 100;
            image.Metadata.VerticalResolution = 100;
            image.SaveAsPng(file);
        }
        return ImportFirst(file);
    }

    public ProcessedImage ImportFirst(string file)
    {
        var importer = new ImageImporter(Context.Scanning);
        var enumerator = importer.Import(file, new ImportParams()).GetAsyncEnumerator();
        try
        {
            if (!enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                throw new InvalidOperationException("No image imported");
            }
            return enumerator.Current;
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public Page CreatePage(int width = 200, int height = 300) => new(CreateImage(width, height), new PageInfo(new DateTime(2026, 10, 28, 15, 4, 9)));

    /// <summary>Renders a page of English text; used for orientation tests.</summary>
    public string CreateTextImage(int rotateClockwise)
    {
        var family = SystemFonts.Families.FirstOrDefault(f => f.Name is "DejaVu Sans" or "Liberation Sans" or "Arial");
        if (family.Name is null)
        {
            family = SystemFonts.Families.First();
        }
        var font = family.CreateFont(30);
        const string paragraph = "The quick brown fox jumps over the lazy dog while the scanner reads every line of this page. "
            + "Documents are often fed upside down or sideways, and the software should notice and fix that. ";
        var file = Path.Combine(Root, $"text-{rotateClockwise}-{Guid.NewGuid():N}.png");
        using var image = new Image<Rgb24>(1700, 2200, Color.White);
        image.Mutate(c =>
        {
            for (var line = 0; line < 30; line++)
            {
                var text = paragraph.Substring(line * 7 % 60, 70);
                c.DrawText(text, font, Color.Black, new PointF(80, 80 + line * 66));
            }
            if (rotateClockwise != 0)
            {
                c.Rotate(rotateClockwise);
            }
        });
        image.Metadata.HorizontalResolution = 200;
        image.Metadata.VerticalResolution = 200;
        image.SaveAsPng(file);
        return file;
    }

    public void Dispose()
    {
        Context.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
