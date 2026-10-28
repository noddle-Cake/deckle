using NAPS2.Images.ImageSharp;
using Scanwise.Core.Batch;
using Scanwise.Core.Export;
using Scanwise.Core.Pages;
using Scanwise.Core.Processing;
using Scanwise.Core.Settings;

namespace Scanwise.Core.Tests;

public class SaveFlowTests
{
    private static (SaveCoordinator Saver, AppSettings Settings) Create(TestEnv env, ScanAction action)
    {
        var exporter = new ExportService(env.Context.Scanning, (ImageSharpImageContext)env.Context.ImageContext, new TesseractLocator(env.Paths));
        return (new SaveCoordinator(exporter), new AppSettings { SaveFolder = env.OutputDir, ScanAction = action, FileNamePattern = "Doc {n}" });
    }

    private static NamingContext Naming => new(DateTime.Now, OutputFormat.Pdf);

    [Fact]
    public async Task AddToExistingExtendsTheSamePdf()
    {
        using var env = new TestEnv();
        using var doc = new PageDocument();
        var (saver, settings) = Create(env, ScanAction.AddToExisting);

        var first = new[] { env.CreatePage() };
        doc.AddRange(first);
        var r1 = await saver.SaveScanAsync(doc, first, OutputFormat.Pdf, settings, Naming);

        var second = new[] { env.CreatePage(), env.CreatePage() };
        doc.AddRange(second);
        var r2 = await saver.SaveScanAsync(doc, second, OutputFormat.Pdf, settings, Naming);

        Assert.Equal(r1!.Paths, r2!.Paths);
        Assert.Equal("Doc 1.pdf", Path.GetFileName(r2.Paths[0]));
        Assert.Single(Directory.GetFiles(env.OutputDir));
        Assert.False(saver.HasUnsavedChanges(doc));
        doc.Rotate([second[0].Id], 90);
        Assert.True(saver.HasUnsavedChanges(doc));
    }

    [Fact]
    public async Task StartFreshCreatesANewFilePerScan()
    {
        using var env = new TestEnv();
        using var doc = new PageDocument();
        var (saver, settings) = Create(env, ScanAction.StartFresh);
        for (var i = 0; i < 2; i++)
        {
            doc.Clear();
            saver.StartNewDocument();
            var pages = new[] { env.CreatePage() };
            doc.AddRange(pages);
            await saver.SaveScanAsync(doc, pages, OutputFormat.Pdf, settings, Naming);
        }
        Assert.Equal(["Doc 1.pdf", "Doc 2.pdf"], Directory.GetFiles(env.OutputDir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task ImageFormatsWriteOneFilePerNewPage()
    {
        using var env = new TestEnv();
        using var doc = new PageDocument();
        var (saver, settings) = Create(env, ScanAction.AddToExisting);
        settings.FileNamePattern = "Photo";
        var pages = new[] { env.CreatePage(), env.CreatePage() };
        doc.AddRange(pages);
        var result = await saver.SaveScanAsync(doc, pages, OutputFormat.Jpeg, settings, Naming);
        Assert.Equal(["Photo (page 01).jpg", "Photo (page 02).jpg"], result!.Paths.Select(Path.GetFileName));
    }

    [Fact]
    public async Task AskEveryTimeUsesTheChosenPathAndHonorsCancel()
    {
        using var env = new TestEnv();
        using var doc = new PageDocument();
        var (saver, settings) = Create(env, ScanAction.StartFresh);
        settings.SaveLocation = SaveLocationMode.AskEveryTime;
        string? suggested = null;
        saver.AskForPath = (name, _) => { suggested = name; return Task.FromResult<string?>(null); };
        var pages = new[] { env.CreatePage() };
        doc.AddRange(pages);

        Assert.Null(await saver.SaveScanAsync(doc, pages, OutputFormat.Pdf, settings, Naming));
        Assert.Equal("Doc 1.pdf", suggested);

        var chosen = Path.Combine(env.OutputDir, "picked.pdf");
        saver.AskForPath = (_, _) => Task.FromResult<string?>(chosen);
        var result = await saver.SaveScanAsync(doc, pages, OutputFormat.Pdf, settings, Naming);
        Assert.Equal(chosen, result!.Paths.Single());
        Assert.True(File.Exists(chosen));
    }

    [Fact]
    public async Task BatchRunsEveryScanAndSavesPerScan()
    {
        var scans = 0;
        var saves = 0;
        var batch = new BatchScanner
        {
            ScanOnce = _ => { scans++; return Task.FromResult(2); },
            Save = _ => { saves++; return Task.CompletedTask; },
        };
        var pages = await batch.RunAsync(new BatchOptions(3, TimeSpan.Zero, false, BatchOutput.FilePerScan), null, CancellationToken.None);
        Assert.Equal((3, 3, 6), (scans, saves, pages));
    }

    [Fact]
    public async Task BatchStopsWhenPromptDeclinesAndStillSaves()
    {
        var saves = 0;
        var batch = new BatchScanner
        {
            ScanOnce = _ => Task.FromResult(1),
            Save = _ => { saves++; return Task.CompletedTask; },
            Prompt = n => Task.FromResult(n < 3),
        };
        var pages = await batch.RunAsync(new BatchOptions(5, TimeSpan.Zero, true, BatchOutput.SingleFile), null, CancellationToken.None);
        Assert.Equal(2, pages);
        Assert.Equal(1, saves);
    }
}
