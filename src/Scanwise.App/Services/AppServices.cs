using NAPS2.Images.ImageSharp;
using Scanwise.Core;
using Scanwise.Core.Export;
using Scanwise.Core.Import;
using Scanwise.Core.Pages;
using Scanwise.Core.Processing;
using Scanwise.Core.Scanning;
using Scanwise.Core.Settings;

namespace Scanwise.App.Services;

/// <summary>Composition root: one instance of every core service for the lifetime of the app.</summary>
public sealed class AppServices : IDisposable
{
    public AppServices(AppPaths paths)
    {
        Paths = paths;
        Context = new ScanwiseContext(paths);
        Settings = JsonStore.Load(paths.SettingsFile, () => new AppSettings());
        Profiles = new ProfileStore(paths.ProfilesFile);
        Tesseract = new TesseractLocator(paths);
        var imageContext = (ImageSharpImageContext)Context.ImageContext;
        Scanner = new ScannerService(Context.Scanning);
        Export = new ExportService(Context.Scanning, imageContext, Tesseract);
        Saver = new SaveCoordinator(Export);
        Import = new ImportService(Context.Scanning, paths.TempDir);
        AutoRotate = new AutoRotateService(imageContext, Tesseract, paths.TempDir);
        Session = new SessionStore(paths.SessionDir, Context.Scanning);
        Renderer = new Imaging.PageRenderer(imageContext);
    }

    public AppPaths Paths { get; }
    public ScanwiseContext Context { get; }
    public AppSettings Settings { get; }
    public ProfileStore Profiles { get; }
    public TesseractLocator Tesseract { get; }
    public ScannerService Scanner { get; }
    public ExportService Export { get; }
    public SaveCoordinator Saver { get; }
    public ImportService Import { get; }
    public AutoRotateService AutoRotate { get; }
    public SessionStore Session { get; }
    public Imaging.PageRenderer Renderer { get; }
    public PageDocument Document { get; } = new();

    public void SaveSettings() => JsonStore.Save(Paths.SettingsFile, Settings);

    public void Dispose()
    {
        Document.Dispose();
        Context.Dispose();
    }
}
