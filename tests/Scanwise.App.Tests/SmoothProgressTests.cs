using Avalonia.Headless.XUnit;
using Scanwise.App.ViewModels;

namespace Scanwise.App.Tests;

public sealed class SmoothProgressTests
{
    [AvaloniaFact]
    public void CreepsForwardWithoutScannerReports()
    {
        var shown = new List<double>();
        var bar = new SmoothProgress(shown.Add);
        bar.NextPage();
        for (var i = 0; i < 200; i++)
        {
            bar.Tick(0.05);
        }
        Assert.InRange(bar.Shown, 50, 95);
        Assert.Equal(shown.Order(), shown); // Never moves backwards.
    }

    [AvaloniaFact]
    public void FollowsTheScannerWhenItIsAheadAndResetsPerPage()
    {
        var bar = new SmoothProgress(_ => { });
        bar.NextPage();
        bar.Report(0.9);
        for (var i = 0; i < 40; i++)
        {
            bar.Tick(0.05);
        }
        Assert.InRange(bar.Shown, 85, 90);
        bar.Report(0.2); // A lower figure later in the page doesn't pull the bar back.
        bar.Tick(0.05);
        Assert.True(bar.Shown >= 85);

        bar.NextPage();
        Assert.Equal(0, bar.Shown);
    }
}
