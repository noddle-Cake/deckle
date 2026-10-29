using NAPS2.Images.ImageSharp;
using Scanwise.Core.Processing;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using EllipsePolygon = SixLabors.ImageSharp.Drawing.EllipsePolygon;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit.Abstractions;

namespace Scanwise.Core.Tests;

/// <summary>
/// Measures auto-rotate on realistic pages instead of one clean paragraph: landscape documents (letters, tables,
/// slides, a photo with a caption, small print, columns) fed through the scanner in portrait, so they arrive turned
/// 90° or 270°, both clean and as a rough scan (grey paper, noise, blur, slight skew); plus portrait pages at 0° and
/// 180° so a fix for sideways pages can't break upright ones. Slow (hundreds of OCR runs), so it only runs with
/// SCANWISE_ROTATE_EVAL=1; SCANWISE_TEST_OSD_DIR adds Tesseract's orientation data.
/// </summary>
public class AutoRotateAccuracyTests(ITestOutputHelper output)
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("SCANWISE_ROTATE_EVAL") == "1";
    private static readonly string? OsdDir = Environment.GetEnvironmentVariable("SCANWISE_TEST_OSD_DIR");

    /// <summary>What the improved detection must reach on this set (it scored 100% when written).</summary>
    private const double RequiredAccuracy = 0.95;

    private static readonly string[] Words =
    [
        "invoice", "total", "amount", "customer", "account", "number", "date", "payment", "balance", "service",
        "quarter", "report", "summary", "project", "budget", "revenue", "expenses", "office", "meeting", "schedule",
        "the", "and", "for", "with", "from", "this", "that", "please", "contact", "address", "phone", "order",
        "delivery", "shipping", "product", "quantity", "price", "discount", "tax", "subtotal", "signature", "approved",
    ];

    [SkippableFact]
    public async Task DetectsSidewaysAndUpsideDownPages()
    {
        Skip.IfNot(Enabled, "Set SCANWISE_ROTATE_EVAL=1 to measure auto-rotate accuracy (slow)");
        using var env = new TestEnv();
        if (!string.IsNullOrEmpty(OsdDir) && File.Exists(Path.Combine(OsdDir, "osd.traineddata")))
        {
            Directory.CreateDirectory(env.Paths.TessdataDir);
            File.Copy(Path.Combine(OsdDir, "osd.traineddata"), Path.Combine(env.Paths.TessdataDir, "osd.traineddata"));
        }
        var locator = new TesseractLocator(env.Paths);
        Skip.IfNot(locator.Find()?.HasLanguage("eng") == true, "Tesseract with English data is not installed");
        output.WriteLine($"Orientation data: {(locator.Find()!.HasOsd ? "yes" : "no")}");
        var service = new AutoRotateService((ImageSharpImageContext)env.Context.ImageContext, locator, env.Paths.TempDir);

        var cases = new List<(string Name, string File, int Expected)>();
        foreach (var style in Enum.GetValues<Style>())
        {
            foreach (var rough in new[] { false, true })
            {
                var landscape = style != Style.PortraitLetter;
                var rotations = landscape ? new[] { 90, 270 } : new[] { 0, 180 };
                foreach (var rotation in rotations)
                {
                    var name = $"{style}{(rough ? " rough" : "")} {rotation}°";
                    cases.Add((name, Render(env.Root, style, rough, rotation, seed: cases.Count), (360 - rotation) % 360));
                }
            }
        }

        // A few pages at a time: the app does one page at a time, and running all of them at once makes Tesseract hit
        // its timeout, which reads as "don't rotate" and would measure the machine instead of the detection.
        using var gate = new SemaphoreSlim(3);
        var results = await Task.WhenAll(cases.Select(async c =>
        {
            await gate.WaitAsync();
            try
            {
                var image = env.ImportFirst(c.File);
                var result = await service.DetectAsync(image, "eng");
                return (c.Name, c.Expected, Result: result);
            }
            finally
            {
                gate.Release();
            }
        }));

        var correct = 0;
        var report = new System.Text.StringBuilder();
        foreach (var (name, expected, result) in results)
        {
            var ok = result.Degrees == expected;
            correct += ok ? 1 : 0;
            report.AppendLine($"{(ok ? "ok  " : "MISS")} {name,-28} expected {expected,3}, got {result.Degrees,3} ({result.Method}, {result.Confidence:0.0})");
        }
        var accuracy = (double)correct / results.Length;
        report.AppendLine($"Accuracy: {correct}/{results.Length} = {accuracy:P0}");
        output.WriteLine(report.ToString());
        // Also as a file, since test output only shows for failures: SCANWISE_ROTATE_EVAL_REPORT, or the temp folder.
        File.WriteAllText(Environment.GetEnvironmentVariable("SCANWISE_ROTATE_EVAL_REPORT")
            ?? Path.Combine(Path.GetTempPath(), "scanwise-rotate-eval.txt"), report.ToString());
        Assert.True(accuracy >= RequiredAccuracy, $"Auto-rotate got {correct} of {results.Length} pages right ({accuracy:P0})");
    }

    private enum Style { Letter, Table, Slide, PhotoWithCaption, SmallPrint, Columns, PortraitLetter }

    /// <summary>Draws an upright page, then turns it the way the scanner would deliver it.</summary>
    private static string Render(string folder, Style style, bool rough, int rotateClockwise, int seed)
    {
        var random = new Random(seed);
        var family = SystemFonts.Families.FirstOrDefault(f => f.Name is "DejaVu Sans" or "Liberation Sans" or "Arial");
        if (family.Name is null)
        {
            family = SystemFonts.Families.First();
        }
        string Line(int words) => string.Join(' ', Enumerable.Range(0, words).Select(_ => Words[random.Next(Words.Length)]));
        var (width, height) = style == Style.PortraitLetter ? (1700, 2200) : (2200, 1700); // Letter at 200 dpi.
        using var image = new Image<Rgb24>(width, height, Color.White);
        image.Mutate(c =>
        {
            switch (style)
            {
                case Style.Letter:
                case Style.PortraitLetter:
                    var body = family.CreateFont(28);
                    c.DrawText("Quarterly service report", family.CreateFont(44, FontStyle.Bold), Color.Black, new PointF(120, 100));
                    for (var i = 0; i < (style == Style.Letter ? 18 : 26); i++)
                    {
                        c.DrawText(Line(style == Style.Letter ? 14 : 10), body, Color.Black, new PointF(120, 220 + i * 62));
                    }
                    break;
                case Style.Table:
                    var cell = family.CreateFont(20);
                    for (var row = 0; row < 26; row++)
                    {
                        c.DrawLine(Color.Gray, 2, new PointF(100, 140 + row * 52), new PointF(2100, 140 + row * 52));
                        for (var col = 0; col < 7; col++)
                        {
                            var text = col == 0 ? Words[random.Next(Words.Length)] : (random.Next(100, 99999) / 100.0).ToString("0.00");
                            c.DrawText(text, cell, Color.Black, new PointF(120 + col * 280, 152 + row * 52));
                        }
                    }
                    break;
                case Style.Slide:
                    c.DrawText("Project schedule", family.CreateFont(80, FontStyle.Bold), Color.Black, new PointF(160, 220));
                    for (var i = 0; i < 4; i++)
                    {
                        c.DrawText("• " + Line(4), family.CreateFont(52), Color.Black, new PointF(220, 520 + i * 160));
                    }
                    break;
                case Style.PhotoWithCaption:
                    c.Fill(Color.FromRgb(70, 80, 90), new RectangleF(140, 140, 1300, 1150));
                    c.Fill(Color.FromRgb(160, 170, 150), new EllipsePolygon(700, 600, 300));
                    for (var i = 0; i < 6; i++)
                    {
                        c.DrawText(Line(5), family.CreateFont(30), Color.Black, new PointF(1520, 200 + i * 70));
                    }
                    c.DrawText(Line(12), family.CreateFont(30), Color.Black, new PointF(140, 1360));
                    break;
                case Style.SmallPrint:
                    var small = family.CreateFont(15);
                    for (var i = 0; i < 60; i++)
                    {
                        c.DrawText(Line(26), small, Color.Black, new PointF(100, 100 + i * 25));
                    }
                    break;
                case Style.Columns:
                    var colFont = family.CreateFont(22);
                    for (var col = 0; col < 3; col++)
                    {
                        for (var i = 0; i < 34; i++)
                        {
                            c.DrawText(Line(5), colFont, Color.Black, new PointF(110 + col * 690, 120 + i * 42));
                        }
                    }
                    break;
            }
            if (rough)
            {
                // Grey paper, slight blur and a small skew, like a cheap flatbed.
                c.GaussianBlur(0.8f);
                c.Rotate(1.5f + (float)random.NextDouble(), KnownResamplers.Bicubic);
                c.BackgroundColor(Color.White);
                c.Brightness(0.92f);
            }
            if (rotateClockwise != 0)
            {
                c.Rotate(rotateClockwise);
            }
        });
        if (rough)
        {
            AddNoise(image, random);
        }
        image.Metadata.HorizontalResolution = 200;
        image.Metadata.VerticalResolution = 200;
        var file = Path.Combine(folder, $"eval-{seed}.png");
        image.SaveAsPng(file);
        return file;
    }

    private static void AddNoise(Image<Rgb24> image, Random random)
    {
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var n = random.Next(-18, 19);
                    var p = row[x];
                    row[x] = new Rgb24(Clamp(p.R + n), Clamp(p.G + n), Clamp(p.B + n));
                }
            }
        });
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
}
