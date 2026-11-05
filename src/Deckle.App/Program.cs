using Avalonia;
using Deckle.App.Services;

namespace Deckle.App;

internal static class Program
{
    private const string SelfCheckArgument = "--self-check";
    public const string WaitForArgument = "--wait-for=";

    [STAThread]
    public static void Main(string[] args)
    {
        var scan = args.Contains(ScanShortcuts.ScanArgument);
        // Settings ▸ Look ▸ Restart Now: let the old copy finish (and save its pages) before taking over.
        if (args.FirstOrDefault(a => a.StartsWith(WaitForArgument, StringComparison.Ordinal)) is { } wait
            && int.TryParse(wait[WaitForArgument.Length..], out var pid))
        {
            try
            {
                using var previous = System.Diagnostics.Process.GetProcessById(pid);
                previous.WaitForExit(TimeSpan.FromSeconds(10));
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
        }
        // macOS verification (tasks.md 9.5): --self-check[=report.txt] [--keep-open=seconds]
        if (args.FirstOrDefault(a => a.StartsWith(SelfCheckArgument, StringComparison.Ordinal)) is { } selfCheck)
        {
            App.SelfCheckReport = selfCheck.Length > SelfCheckArgument.Length + 1
                ? selfCheck[(SelfCheckArgument.Length + 1)..]
                : Path.Combine(Path.GetTempPath(), "deckle-self-check.txt");
            var keepOpen = args.FirstOrDefault(a => a.StartsWith("--keep-open=", StringComparison.Ordinal));
            App.SelfCheckKeepOpenSeconds = keepOpen is not null && int.TryParse(keepOpen["--keep-open=".Length..], out var s) ? s : 0;
        }
        // Already running: hand over (a shortcut's "scan", or just bring the window up) and quit.
        else if (SingleInstance.TrySend(scan ? SingleInstance.ScanRequest : SingleInstance.ShowRequest))
        {
            return;
        }
        App.ScanOnStart = scan;
        // --look=windows|macos|gnome: this run only, without changing the setting.
        if (args.FirstOrDefault(a => a.StartsWith("--look=", StringComparison.Ordinal)) is { } look
            && Enum.TryParse<Deckle.Core.Settings.AppLook>(look["--look=".Length..], ignoreCase: true, out var chosen))
        {
            App.LookOverride = chosen;
        }
        var rest = args.Where(a => a != ScanShortcuts.ScanArgument && !a.StartsWith(SelfCheckArgument, StringComparison.Ordinal)
            && !a.StartsWith("--keep-open=", StringComparison.Ordinal) && !a.StartsWith("--look=", StringComparison.Ordinal)
            && !a.StartsWith(WaitForArgument, StringComparison.Ordinal)).ToArray();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(rest);
        Environment.ExitCode = App.ExitCode;
    }

    // Also used by the XAML previewer.
    public static AppBuilder BuildAvaloniaApp()
    {
        // No bundled font: each OS's own UI font (Segoe UI Variable on Windows, San Francisco on macOS, the
        // fontconfig default such as Cantarell or Adwaita Sans on GNOME), as each platform's guidelines ask.
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    }
}
