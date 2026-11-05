using System.Runtime.CompilerServices;
using System.Threading.Channels;
using NAPS2.Images;
using NAPS2.Images.Transforms;
using NAPS2.Scan;

namespace Deckle.Core.Scanning;

public sealed record ScanProgress(int PageNumber, double PageFraction);

/// <summary>Discovers devices on every driver the OS supports and runs scans.</summary>
public sealed class ScannerService
{
    private const int RegionBasis = 10_000;

    private readonly ScanningContext _context;

    public ScannerService(ScanningContext context)
    {
        _context = context;
    }

    /// <summary>Drivers to query on this OS, in the order their devices should be listed.</summary>
    public static IReadOnlyList<Driver> PlatformDrivers
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return [Driver.Wia, Driver.Twain, Driver.Escl];
            }
            // Apple ICA needs a net*-macos build; SANE (bundled binaries) and eSCL cover macOS for now.
            return [Driver.Sane, Driver.Escl];
        }
    }

    /// <summary>
    /// Streams devices as each driver finds them. Drivers are queried in parallel; a failing driver is skipped.
    /// A device reachable through two drivers (e.g. WIA and TWAIN) is listed once per driver.
    /// </summary>
    public async IAsyncEnumerable<DiscoveredScanner> DiscoverAsync([EnumeratorCancellation] CancellationToken cancelToken = default)
    {
        var channel = Channel.CreateUnbounded<DiscoveredScanner>();
        var seen = new HashSet<(Driver, string)>();
        var tasks = PlatformDrivers.Select(async driver =>
        {
            try
            {
                var controller = new ScanController(_context);
                await foreach (var device in controller.GetDevices(driver, cancelToken).ConfigureAwait(false))
                {
                    await channel.Writer.WriteAsync(new DiscoveredScanner(device), cancelToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // Missing libsane, no WIA service, etc. Other drivers still work.
            }
        }).ToList();
        _ = Task.WhenAll(tasks).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        await foreach (var scanner in channel.Reader.ReadAllAsync(cancelToken).ConfigureAwait(false))
        {
            if (seen.Add((scanner.Driver, scanner.Id)))
            {
                yield return scanner;
            }
        }
    }

    public async Task<ScannerCapabilities> GetCapabilitiesAsync(DiscoveredScanner scanner, CancellationToken cancelToken = default)
    {
        try
        {
            var caps = await new ScanController(_context).GetCaps(scanner.Device, cancelToken).ConfigureAwait(false);
            return ScannerCapabilities.From(caps);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ScannerCapabilities.Unknown;
        }
    }

    /// <summary>Scans with <paramref name="settings"/>; pages are yielded as soon as each one is finished.</summary>
    public async IAsyncEnumerable<ProcessedImage> ScanAsync(
        DiscoveredScanner scanner,
        ScanSettings settings,
        ScannerCapabilities? caps,
        IProgress<ScanProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancelToken = default)
    {
        var controller = new ScanController(_context) { PropagateErrors = true };
        if (progress is not null)
        {
            controller.PageStart += (_, e) => progress.Report(new ScanProgress(e.PageNumber, 0));
            controller.PageProgress += (_, e) => progress.Report(new ScanProgress(e.PageNumber, e.Progress));
        }

        var options = settings.ToScanOptions(scanner.Device, caps);
        var region = settings.Source == ScanSource.Flatbed && settings.Region is { IsValid: true } r ? r : null;

        await foreach (var image in controller.Scan(options, cancelToken).ConfigureAwait(false))
        {
            yield return region is null ? image : ApplyRegion(image, region);
        }
    }

    /// <summary>Crops a page to a 0..1 region. The crop is expressed on a virtual 10000² grid that NAPS2 rescales.</summary>
    public static ProcessedImage ApplyRegion(ProcessedImage image, ScanRegion region)
    {
        var crop = new CropTransform(
            (int)Math.Round(region.Left * RegionBasis),
            (int)Math.Round((1 - region.Right) * RegionBasis),
            (int)Math.Round(region.Top * RegionBasis),
            (int)Math.Round((1 - region.Bottom) * RegionBasis),
            RegionBasis,
            RegionBasis);
        return image.WithTransform(crop, disposeSelf: true);
    }
}
