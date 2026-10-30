using Avalonia.Threading;

namespace Scanwise.App.ViewModels;

/// <summary>
/// A steady progress bar for scanning. Scanners report progress in uneven bursts (or not at all), which made the
/// bar jump, stall and flip to indeterminate. This bar creeps forward on its own, easing toward 95% over a
/// typical page time, takes the scanner's figure whenever that is further along, and never moves backwards
/// within a page. <see cref="NextPage"/> starts the next page from zero.
/// </summary>
public sealed class SmoothProgress
{
    /// <summary>Seconds for the estimate to reach about 63% of the way to <see cref="Ceiling"/>.</summary>
    private const double TimeConstant = 6;
    private const double Ceiling = 95;
    /// <summary>Share of the remaining gap the shown value closes each tick, so changes glide instead of jump.</summary>
    private const double Ease = 0.18;

    private readonly Action<double> _show;
    private readonly DispatcherTimer _timer;
    private double _elapsed;
    private double _reported;
    private double _shown;

    public SmoothProgress(Action<double> show)
    {
        _show = show;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Tick(0.05));
    }

    public double Shown => _shown;

    public void Start()
    {
        NextPage();
        _timer.Start();
    }

    /// <summary>The scanner's own progress for the current page, 0–1.</summary>
    public void Report(double pageFraction) => _reported = Math.Max(_reported, Math.Clamp(pageFraction, 0, 1) * 100);

    public void NextPage()
    {
        _elapsed = 0;
        _reported = 0;
        _shown = 0;
        _show(0);
    }

    public void Stop() => _timer.Stop();

    internal void Tick(double seconds)
    {
        _elapsed += seconds;
        var estimate = Ceiling * (1 - Math.Exp(-_elapsed / TimeConstant));
        var target = Math.Max(estimate, _reported);
        var next = _shown + (target - _shown) * Ease;
        if (next > _shown)
        {
            _shown = next;
            _show(_shown);
        }
    }
}
