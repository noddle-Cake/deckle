using Avalonia;
using Scanwise.App.Services;

namespace Scanwise.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var scan = args.Contains(ScanShortcuts.ScanArgument);
        // Already running: hand over (a shortcut's "scan", or just bring the window up) and quit.
        if (SingleInstance.TrySend(scan ? SingleInstance.ScanRequest : SingleInstance.ShowRequest))
        {
            return;
        }
        App.ScanOnStart = scan;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args.Where(a => a != ScanShortcuts.ScanArgument).ToArray());
    }

    // Also used by the XAML previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
