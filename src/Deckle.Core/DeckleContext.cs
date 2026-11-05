using NAPS2.Images;
using NAPS2.Images.ImageSharp;
using NAPS2.Scan;
using Deckle.Core.Settings;

namespace Deckle.Core;

/// <summary>Owns the NAPS2 scanning context: image backend, on-disk page storage and temp folder.</summary>
public sealed class DeckleContext : IDisposable
{
    public DeckleContext(AppPaths paths)
    {
        Paths = paths;
        paths.EnsureCreated();
        ClearDirectory(paths.ImageStoreDir);
        ClearDirectory(paths.TempDir);

        ImageContext = new ImageSharpImageContext();
        Scanning = new ScanningContext(ImageContext)
        {
            // Pages are kept on disk rather than in memory: a 300 dpi color A4 page is ~25 MB raw.
            FileStorageManager = new FileStorageManager(paths.ImageStoreDir),
            TempFolderPath = paths.TempDir,
        };
        if (OperatingSystem.IsWindows())
        {
            // TWAIN drivers are mostly 32-bit; NAPS2 runs them in a separate worker process.
            Scanning.SetUpWin32Worker();
        }
    }

    public AppPaths Paths { get; }
    public ImageContext ImageContext { get; }
    public ScanningContext Scanning { get; }

    public void Dispose()
    {
        Scanning.Dispose();
    }

    private static void ClearDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Another instance may still hold it; leave it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
