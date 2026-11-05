namespace Deckle.Core.Batch;

public enum BatchOutput
{
    /// <summary>All pages from all scans go into one file.</summary>
    SingleFile,
    /// <summary>Each scan is saved to its own file.</summary>
    FilePerScan,
}

public sealed record BatchOptions(int ScanCount, TimeSpan Delay, bool PromptBetweenScans, BatchOutput Output)
{
    public BatchOptions Normalize() => this with
    {
        ScanCount = Math.Clamp(ScanCount, 1, 999),
        Delay = Delay < TimeSpan.Zero ? TimeSpan.Zero : Delay,
    };
}

public sealed record BatchStatus(int ScanNumber, int ScanCount, int PagesSoFar, string Message);

/// <summary>
/// NAPS2's batch scan: run the same scan several times, waiting (or asking) between scans. The actual scanning
/// and saving is supplied by the caller so the loop can be tested without a device.
/// </summary>
public sealed class BatchScanner
{
    /// <summary>Runs one scan; returns the number of pages produced.</summary>
    public required Func<CancellationToken, Task<int>> ScanOnce { get; init; }

    /// <summary>Called after each scan when output is per-scan, or once at the end for a single file.</summary>
    public required Func<CancellationToken, Task> Save { get; init; }

    /// <summary>Asked before scan N (N ≥ 2) when prompting is on; return false to stop.</summary>
    public Func<int, Task<bool>>? Prompt { get; init; }

    public async Task<int> RunAsync(BatchOptions options, IProgress<BatchStatus>? progress, CancellationToken cancelToken)
    {
        options = options.Normalize();
        var pages = 0;
        var unsaved = false;
        try
        {
            for (var scan = 1; scan <= options.ScanCount; scan++)
            {
                if (scan > 1)
                {
                    if (options.PromptBetweenScans && Prompt is not null)
                    {
                        progress?.Report(new BatchStatus(scan, options.ScanCount, pages, "Waiting for the next scan"));
                        if (!await Prompt(scan).ConfigureAwait(false))
                        {
                            break;
                        }
                    }
                    else if (options.Delay > TimeSpan.Zero)
                    {
                        progress?.Report(new BatchStatus(scan, options.ScanCount, pages, $"Next scan in {options.Delay.TotalSeconds:0} s"));
                        await Task.Delay(options.Delay, cancelToken).ConfigureAwait(false);
                    }
                }

                progress?.Report(new BatchStatus(scan, options.ScanCount, pages, $"Scan {scan} of {options.ScanCount}"));
                var produced = await ScanOnce(cancelToken).ConfigureAwait(false);
                pages += produced;
                unsaved |= produced > 0;

                if (options.Output == BatchOutput.FilePerScan && produced > 0)
                {
                    await Save(cancelToken).ConfigureAwait(false);
                    unsaved = false;
                }
            }
        }
        finally
        {
            // Even when stopped early, keep what was scanned.
            if (unsaved && !cancelToken.IsCancellationRequested)
            {
                await Save(CancellationToken.None).ConfigureAwait(false);
            }
        }
        progress?.Report(new BatchStatus(options.ScanCount, options.ScanCount, pages, $"Batch finished, {pages} page(s)"));
        return pages;
    }
}
