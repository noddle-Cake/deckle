using Avalonia;
using Scanwise.App.Services;

namespace Scanwise.App;

internal static class Program
{
    private const string SelfCheckArgument = "--self-check";

    [STAThread]
    public static void Main(string[] args)
    {
        var scan = args.Contains(ScanShortcuts.ScanArgument);
        // macOS verification (tasks.md 9.5): --self-check[=report.txt] [--keep-open=seconds]
        if (args.FirstOrDefault(a => a.StartsWith(SelfCheckArgument, StringComparison.Ordinal)) is { } selfCheck)
        {
            App.SelfCheckReport = selfCheck.Length > SelfCheckArgument.Length + 1
                ? selfCheck[(SelfCheckArgument.Length + 1)..]
                : Path.Combine(Path.GetTempPath(), "scanwise-self-check.txt");
            var keepOpen = args.FirstOrDefault(a => a.StartsWith("--keep-open=", StringComparison.Ordinal));
            App.SelfCheckKeepOpenSeconds = keepOpen is not null && int.TryParse(keepOpen["--keep-open=".Length..], out var s) ? s : 0;
        }
        // Already running: hand over (a shortcut's "scan", or just bring the window up) and quit.
        else if (SingleInstance.TrySend(scan ? SingleInstance.ScanRequest : SingleInstance.ShowRequest))
        {
            return;
        }
        App.ScanOnStart = scan;
        var rest = args.Where(a => a != ScanShortcuts.ScanArgument && !a.StartsWith(SelfCheckArgument, StringComparison.Ordinal)
            && !a.StartsWith("--keep-open=", StringComparison.Ordinal)).ToArray();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(rest);
        Environment.ExitCode = App.ExitCode;
    }

    // Also used by the XAML previewer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        // macOS keeps its system font (San Francisco), so text matches the menu bar and system dialogs.
        if (!OperatingSystem.IsMacOS())
        {
            builder = builder.WithInterFont();
        }
        return builder.LogToTrace();
    }
}
