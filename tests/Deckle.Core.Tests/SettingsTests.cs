using Deckle.Core.Export;
using Deckle.Core.Scanning;
using Deckle.Core.Settings;

namespace Deckle.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void CarriesOverFolderFromOldName()
    {
        using var env = new TestEnv();
        var root = Path.Combine(env.Paths.DataDir, "rename");
        var old = Path.Combine(root, "scanwise");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "settings.json"), "{}");

        AppPaths.MoveFromOldName(Path.Combine(root, "deckle"), "scanwise");

        Assert.True(File.Exists(Path.Combine(root, "deckle", "settings.json")));
        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void KeepsExistingFolderOverOldName()
    {
        using var env = new TestEnv();
        var root = Path.Combine(env.Paths.DataDir, "rename");
        Directory.CreateDirectory(Path.Combine(root, "scanwise"));
        Directory.CreateDirectory(Path.Combine(root, "deckle"));

        AppPaths.MoveFromOldName(Path.Combine(root, "deckle"), "scanwise");

        Assert.True(Directory.Exists(Path.Combine(root, "scanwise")));
    }

    [Fact]
    public void RoundTripsSettings()
    {
        using var env = new TestEnv();
        var settings = new AppSettings
        {
            Theme = AppTheme.Dark,
            SaveLocation = SaveLocationMode.AskEveryTime,
            FileNamePattern = "Invoice {n}",
            Pdf = { Ocr = true, Standard = PdfStandard.PdfA2B },
            Images = { JpegQuality = 70, TiffCompression = TiffCompression.Ccitt4 },
        };
        JsonStore.Save(env.Paths.SettingsFile, settings);
        Assert.Contains("\"theme\": \"Dark\"", File.ReadAllText(env.Paths.SettingsFile));

        var loaded = JsonStore.Load(env.Paths.SettingsFile, () => new AppSettings());
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal(SaveLocationMode.AskEveryTime, loaded.SaveLocation);
        Assert.Equal("Invoice {n}", loaded.FileNamePattern);
        Assert.True(loaded.Pdf.Ocr);
        Assert.Equal(PdfStandard.PdfA2B, loaded.Pdf.Standard);
        Assert.Equal(70, loaded.Images.JpegQuality);
        Assert.Equal(TiffCompression.Ccitt4, loaded.Images.TiffCompression);
    }

    [Fact]
    public void CorruptFileFallsBackToDefaultsAndIsKept()
    {
        using var env = new TestEnv();
        Directory.CreateDirectory(env.Paths.ConfigDir);
        File.WriteAllText(env.Paths.SettingsFile, "{ not json");
        var loaded = JsonStore.Load(env.Paths.SettingsFile, () => new AppSettings());
        Assert.Equal(AppTheme.System, loaded.Theme);
        Assert.True(File.Exists(env.Paths.SettingsFile + ".corrupt"));
    }

    [Fact]
    public void ProfilesPersistAndAlwaysKeepOne()
    {
        using var env = new TestEnv();
        var store = new ProfileStore(env.Paths.ProfilesFile);
        Assert.Single(store.Profiles);
        Assert.False(store.Delete("Default"));

        store.Save(new ScanSettings { Name = "Receipts", Dpi = 200, Color = ColorMode.Grayscale, Format = OutputFormat.Jpeg });
        Assert.Equal("Receipts 2", store.UniqueName("Receipts"));
        Assert.True(store.Rename("Receipts", "Bills"));
        Assert.False(store.Rename("Bills", "default"));

        var reloaded = new ProfileStore(env.Paths.ProfilesFile);
        var bills = reloaded.Get("bills");
        Assert.Equal(200, bills.Dpi);
        Assert.Equal(ColorMode.Grayscale, bills.Color);
        Assert.Equal(OutputFormat.Jpeg, bills.Format);
        Assert.True(reloaded.Delete("Bills"));
        Assert.Single(new ProfileStore(env.Paths.ProfilesFile).Profiles);
    }

    [Fact]
    public void ScanSettingsMapToNaps2Options()
    {
        var settings = new ScanSettings
        {
            Source = ScanSource.Duplex,
            Color = ColorMode.BlackAndWhite,
            Dpi = 200,
            Brightness = 150,
            Contrast = -20,
            PageSize = PageSizePreset.Custom,
            CustomWidthMm = 100,
            CustomHeightMm = 150,
        };
        var options = settings.ToScanOptions(new NAPS2.Scan.ScanDevice(NAPS2.Scan.Driver.Sane, "test:0", "Test"), null);
        Assert.Equal(NAPS2.Scan.PaperSource.Duplex, options.PaperSource);
        Assert.Equal(NAPS2.Images.BitDepth.BlackAndWhite, options.BitDepth);
        Assert.Equal(1000, options.Brightness);
        Assert.Equal(-200, options.Contrast);
        Assert.Equal(100m, options.PageSize!.WidthInMm);
        Assert.Equal(NAPS2.Scan.Driver.Sane, options.Driver);
    }

    [Fact]
    public void NearestResolutionPicksClosestSupported()
    {
        var caps = new ScannerCapabilities
        {
            FlatbedCaps = new SourceCapabilities([75, 150, 600], true, true, true, null),
        };
        Assert.Equal(150, caps.NearestResolution(ScanSource.Flatbed, 300 - 100));
        Assert.Equal(600, caps.NearestResolution(ScanSource.Flatbed, 500));
        Assert.Equal(ScannerCapabilities.FallbackResolutions, caps.ResolutionsFor(ScanSource.Feeder));
    }
}
