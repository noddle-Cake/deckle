using NAPS2.Images.ImageSharp;
using Scanwise.Core.Processing;

namespace Scanwise.Core.Tests;

public class AutoRotateTests
{
    /// <summary>Folder with osd.traineddata for the orientation-detection path (optional).</summary>
    private static readonly string? OsdDir = Environment.GetEnvironmentVariable("SCANWISE_TEST_OSD_DIR");

    [Fact]
    public void ScoreCountsOnlyConfidentWords()
    {
        const string header = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n";
        var tsv = header
            + "5\t1\t1\t1\t1\t1\t0\t0\t10\t10\t96.5\tHello\n"
            + "5\t1\t1\t1\t1\t2\t0\t0\t10\t10\t91\tworld,\n"
            + "5\t1\t1\t1\t1\t3\t0\t0\t10\t10\t30\tSduunf\n"
            + "5\t1\t1\t1\t1\t4\t0\t0\t10\t10\t95\ta\n"
            + "4\t1\t1\t1\t1\t0\t0\t0\t10\t10\t-1\t\n";
        Assert.Equal(10, AutoRotateService.ScoreTsv(tsv));
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task TextComparisonFindsTheUprightAngle(int scannedRotation)
    {
        using var env = new TestEnv();
        var locator = new TesseractLocator(env.Paths);
        Skip.IfNot(locator.Find()?.HasLanguage("eng") == true, "Tesseract with English data is not installed");
        Skip.If(locator.Find()!.HasOsd, "OSD data is installed system-wide; the text-comparison path is not reachable");
        await AssertCorrects(env, locator, scannedRotation, RotationMethod.TextComparison);
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task OrientationDetectionFindsTheUprightAngle(int scannedRotation)
    {
        Skip.If(string.IsNullOrEmpty(OsdDir) || !File.Exists(Path.Combine(OsdDir, "osd.traineddata")), "Set SCANWISE_TEST_OSD_DIR to a folder with osd.traineddata");
        using var env = new TestEnv();
        Directory.CreateDirectory(env.Paths.TessdataDir);
        File.Copy(Path.Combine(OsdDir!, "osd.traineddata"), Path.Combine(env.Paths.TessdataDir, "osd.traineddata"));
        var locator = new TesseractLocator(env.Paths);
        Skip.If(locator.Find() is null, "Tesseract is not installed");
        await AssertCorrects(env, locator, scannedRotation, RotationMethod.OrientationDetection);
    }

    private static async Task AssertCorrects(TestEnv env, TesseractLocator locator, int scannedRotation, RotationMethod expectedMethod)
    {
        var service = new AutoRotateService((ImageSharpImageContext)env.Context.ImageContext, locator, env.Paths.TempDir);
        var image = env.ImportFirst(env.CreateTextImage(scannedRotation));

        var result = await service.DetectAsync(image, "eng");

        Assert.Equal(expectedMethod, result.Method);
        // A page turned clockwise by d needs (360 - d) more clockwise degrees to be upright.
        Assert.Equal((360 - scannedRotation) % 360, result.Degrees);

        var fixedImage = await service.AutoRotateAsync(image, "eng");
        var second = await service.DetectAsync(fixedImage, "eng");
        Assert.Equal(0, second.Degrees);
    }
}
