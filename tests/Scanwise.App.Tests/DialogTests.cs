using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Scanwise.App.Views.Dialogs;
using Scanwise.Core.Batch;
using Scanwise.Core.Scanning;

namespace Scanwise.App.Tests;

public class DialogTests
{
    private static readonly string ScreenshotDir = Environment.GetEnvironmentVariable("SCANWISE_SCREENSHOTS") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Path.GetTempPath(), "scanwise-screenshots");

    private static Window Host(Control content, double width, double height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = new Border { Padding = new Thickness(24), Child = content },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window;
    }

    private static void Save(Window window, string name)
    {
        Directory.CreateDirectory(ScreenshotDir);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.CaptureRenderedFrame()?.Save(Path.Combine(ScreenshotDir, name + ".png"));
    }

    [AvaloniaFact]
    public void MergeDialogPreviewsTheResultingOrder()
    {
        var dialog = new ScanMergeDialog(3);
        var window = Host(dialog, 480, 420);
        Assert.Equal(0, dialog.AfterIndex);
        Assert.True(dialog.Reverse);
        Save(window, "10-merge-dialog");
        window.Close();
    }

    [AvaloniaFact]
    public void BatchDialogDefaults()
    {
        var dialog = new BatchScanDialog();
        var window = Host(dialog, 460, 420);
        var options = dialog.Options;
        Assert.Equal(3, options.ScanCount);
        Assert.True(options.PromptBetweenScans);
        Assert.Equal(BatchOutput.SingleFile, options.Output);
        Save(window, "11-batch-dialog");
        window.Close();
    }

    [AvaloniaFact]
    public void RegionDialogReturnsSelection()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(400, 560));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.White, new Rect(0, 0, 400, 560));
            ctx.FillRectangle(Brushes.SteelBlue, new Rect(40, 60, 200, 120));
        }
        var dialog = new RegionDialog(bitmap, new ScanRegion(0.1, 0.1, 0.6, 0.35));
        var window = Host(dialog, 640, 700);
        Assert.Equal(new ScanRegion(0.1, 0.1, 0.6, 0.35), dialog.Region);
        Save(window, "12-region-dialog");
        window.Close();

        var whole = new RegionDialog(bitmap, null);
        Assert.Null(whole.Region);
    }
}
