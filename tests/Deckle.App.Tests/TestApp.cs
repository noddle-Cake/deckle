using Avalonia;
using Avalonia.Headless;
using Deckle.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Deckle.App.Tests;

public static class TestAppBuilder
{
    // Real Skia rendering so screenshots show what users would see.
    public static AppBuilder BuildAvaloniaApp()
    {
        // The same results on every OS: tests get the Windows/Linux conventions unless they switch to macOS
        // themselves (MacConventionTests), also when the suite runs on a Mac (scripts/mac-check.sh).
        Deckle.App.Services.AppShortcuts.IsMac = false;
        Deckle.App.Services.PlatformLook.Current = Deckle.Core.Settings.AppLook.Windows;
        return AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
