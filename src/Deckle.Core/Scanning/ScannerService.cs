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

    // SANE is one library shared by the whole process, and the paper sensor ends its session with sane_exit.
    // So the sensor never runs while a search, capability query or scan is using SANE, and those wait for a
    // sensor reading that is under way (well under a second).
    // Static because the library is: two services in one process share it too.
    private static readonly SemaphoreSlim SaneSensorGate = new(1, 1);
    private static int _saneOperations;

    public ScannerService(ScanningContext context)
    {
        _context = context;
    }

    /// <summary>Replaces the hardware paper sensor (tests).</summary>
    public Func<DiscoveredScanner, CancellationToken, Task<PaperState>>? PaperSensorOverride { get; set; }

    /// <summary>
    /// Asks the scanner whether paper is in its feeder. Supported for network (eSCL) scanners, WIA on Windows
    /// and SANE backends with a hopper sensor on Linux; TWAIN and the rest report <see cref="PaperState.Unsupported"/>.
    /// Never throws for device trouble: that is <see cref="PaperState.Unavailable"/>.
    /// </summary>
    public async Task<PaperState> GetPaperStateAsync(DiscoveredScanner scanner, CancellationToken cancelToken = default)
    {
        if (PaperSensorOverride is { } sensor)
        {
            return await sensor(scanner, cancelToken).ConfigureAwait(false);
        }
        try
        {
            if (scanner.Driver == Driver.Escl)
            {
                return await EsclPaperSensor.ReadAsync(scanner.Device, cancelToken).ConfigureAwait(false);
            }
            if (scanner.Driver == Driver.Wia && OperatingSystem.IsWindows())
            {
                return await WiaPaperSensor.ReadAsync(scanner.Id).ConfigureAwait(false);
            }
            if (scanner.Driver == Driver.Sane && OperatingSystem.IsLinux())
            {
                return await ReadSaneSensorAsync(scanner.Id, cancelToken).ConfigureAwait(false);
            }
            return PaperState.Unsupported;
        }
        catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Scanner asleep or unplugged, no libsane, HTTP error…
            return PaperState.Unavailable;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private async Task<PaperState> ReadSaneSensorAsync(string deviceName, CancellationToken cancelToken)
    {
        await SaneSensorGate.WaitAsync(cancelToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _saneOperations) > 0)
            {
                return PaperState.Unavailable;
            }
            // Not cancellable once started: the gate must stay closed until libsane is done.
            return await Task.Run([System.Runtime.Versioning.SupportedOSPlatform("linux")] () => SanePaperSensor.Read(deviceName)).ConfigureAwait(false);
        }
        finally
        {
            SaneSensorGate.Release();
        }
    }

    /// <summary>Marks SANE as in use until the result is disposed; null for other drivers.</summary>
    private async Task<IDisposable?> UseDriverAsync(Driver driver, CancellationToken cancelToken)
    {
        if (driver != Driver.Sane)
        {
            return null;
        }
        Interlocked.Increment(ref _saneOperations);
        try
        {
            await SaneSensorGate.WaitAsync(cancelToken).ConfigureAwait(false);
            SaneSensorGate.Release();
        }
        catch
        {
            Interlocked.Decrement(ref _saneOperations);
            throw;
        }
        return new SaneUse();
    }

    private sealed class SaneUse : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _saneOperations);
            }
        }
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
                using var use = await UseDriverAsync(driver, cancelToken).ConfigureAwait(false);
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
            using var use = await UseDriverAsync(scanner.Driver, cancelToken).ConfigureAwait(false);
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

        using var use = await UseDriverAsync(scanner.Driver, cancelToken).ConfigureAwait(false);
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
