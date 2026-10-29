using Avalonia;
using Avalonia.Headless;
using Scanwise.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Scanwise.App.Tests;

public static class TestAppBuilder
{
    // Real Skia rendering so screenshots show what users would see.
    public static AppBuilder BuildAvaloniaApp()
    {
        // The same results on every OS: tests get the Windows/Linux conventions unless they switch to macOS
        // themselves (MacConventionTests), also when the suite runs on a Mac (scripts/mac-check.sh).
        Scanwise.App.Services.AppShortcuts.IsMac = false;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
