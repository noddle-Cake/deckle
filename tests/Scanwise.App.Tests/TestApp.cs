using Avalonia;
using Avalonia.Headless;
using Scanwise.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Scanwise.App.Tests;

public static class TestAppBuilder
{
    // Real Skia rendering so screenshots show what users would see.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
