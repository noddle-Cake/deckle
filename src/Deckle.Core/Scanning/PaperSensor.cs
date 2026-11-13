using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAPS2.Escl;
using NAPS2.Escl.Client;
using NAPS2.Scan;
using NAPS2.Wia;

namespace Deckle.Core.Scanning;

/// <summary>What a scanner's feeder sensor says right now.</summary>
public enum PaperState
{
    /// <summary>The scanner (or its driver) has no sensor that can be asked.</summary>
    Unsupported,
    /// <summary>The sensor could not be read this time: scanner busy, asleep or out of reach.</summary>
    Unavailable,
    Empty,
    Loaded,
}

/// <summary>
/// Decides when "scan when paper is loaded" should start a scan. Paper has to be seen on consecutive readings
/// (so a stack that is still being pushed in isn't grabbed), and after a scan the feeder has to be seen empty
/// before the next one, so a jam, a cancelled scan or a sensor stuck on "loaded" can't start scan after scan.
/// </summary>
public sealed class PaperTrigger
{
    private readonly int _settleReadings;
    private bool _armed = true;
    private int _loaded;

    public PaperTrigger(int settleReadings = 2)
    {
        _settleReadings = Math.Max(1, settleReadings);
    }

    /// <summary>Feeds one reading; true means "start a scan now".</summary>
    public bool Observe(PaperState state)
    {
        switch (state)
        {
            case PaperState.Empty:
                _armed = true;
                _loaded = 0;
                return false;
            case PaperState.Loaded when _armed:
                if (++_loaded < _settleReadings)
                {
                    return false;
                }
                _armed = false;
                _loaded = 0;
                return true;
            default:
                _loaded = 0;
                return false;
        }
    }

    /// <summary>Starts over: paper that is already in the feeder counts as newly loaded.</summary>
    public void Reset()
    {
        _armed = true;
        _loaded = 0;
    }
}

/// <summary>Network scanners: eSCL's ScannerStatus reports the feeder state.</summary>
internal static class EsclPaperSensor
{
    private static readonly ConcurrentDictionary<string, EsclService> Located = new();

    public static async Task<PaperState> ReadAsync(ScanDevice device, CancellationToken cancelToken)
    {
        EsclClient client;
        if (Uri.TryCreate(device.ConnectionUri, UriKind.Absolute, out var uri))
        {
            client = new EsclClient(uri);
        }
        else
        {
            var service = await LocateAsync(device.ID, cancelToken).ConfigureAwait(false);
            if (service is null)
            {
                return PaperState.Unavailable;
            }
            client = new EsclClient(service);
        }
        client.CancelToken = cancelToken;
        return Map(await client.GetStatus().ConfigureAwait(false));
    }

    public static PaperState Map(EsclScannerStatus status) => status.AdfState switch
    {
        // No AdfState element: no feeder, or a scanner that doesn't report it.
        EsclAdfState.Unknown => PaperState.Unsupported,
        // Paper is in, but someone is scanning at the device itself: leave it alone.
        EsclAdfState.ScannerAdfLoaded => status.State is EsclScannerState.Idle or EsclScannerState.Unknown ? PaperState.Loaded : PaperState.Empty,
        // Empty, jammed, hatch open, mid-feed: nothing to start on.
        _ => PaperState.Empty,
    };

    /// <summary>Finds the scanner on the network again when discovery gave no address to reuse.</summary>
    private static async Task<EsclService?> LocateAsync(string id, CancellationToken cancelToken)
    {
        if (Located.TryGetValue(id, out var known))
        {
            return known;
        }
        var found = new TaskCompletionSource<EsclService?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var locator = new EsclServiceLocator(service =>
        {
            if (!string.IsNullOrEmpty(service.Uuid) && id.Contains(service.Uuid, StringComparison.OrdinalIgnoreCase))
            {
                found.TrySetResult(service);
            }
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var registration = timeout.Token.Register(() => found.TrySetResult(null));
        locator.Start();
        var result = await found.Task.ConfigureAwait(false);
        cancelToken.ThrowIfCancellationRequested();
        if (result is not null)
        {
            Located[id] = result;
        }
        return result;
    }
}

/// <summary>Windows: WIA's document handling status has a "feeder ready" flag.</summary>
[SupportedOSPlatform("windows")]
internal static class WiaPaperSensor
{
    public static Task<PaperState> ReadAsync(string deviceId)
    {
        // WIA is COM; like NAPS2's own driver it is used from a single-threaded apartment.
        var result = new TaskCompletionSource<PaperState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var manager = new WiaDeviceManager();
                using var device = manager.FindDevice(deviceId);
                result.TrySetResult(!device.SupportsFeeder() ? PaperState.Unsupported
                    : device.FeederReady() ? PaperState.Loaded : PaperState.Empty);
            }
            catch (Exception)
            {
                result.TrySetResult(PaperState.Unavailable);
            }
        })
        {
            IsBackground = true,
            Name = "WIA paper sensor",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return result.Task;
    }
}

/// <summary>
/// Linux: document scanners' SANE backends (fujitsu/ScanSnap, epjitsu, canon_dr, genesys sheet-fed, hp5590…)
/// expose the hopper sensor as a read-only boolean option. NAPS2 keeps its SANE bindings internal, so this reads
/// the option through libsane directly. The caller must make sure nothing else is using SANE meanwhile
/// (<see cref="ScannerService"/> does): the library is shared with NAPS2 and is not safe to use from two sides.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class SanePaperSensor
{
    private const string Lib = "libsane.so.1";
    private const int StatusGood = 0;
    private const int ActionGetValue = 0;
    private const int TypeBool = 0;
    private const int CapSoftDetect = 4;
    private const int CapInactive = 32;

    /// <summary>SANE_NAME_PAGE_LOADED and the names individual backends use for the same sensor.</summary>
    public static readonly IReadOnlyList<string> SensorOptions = ["page-loaded", "adf-loaded", "doc-in-adf"];

    [DllImport(Lib)] private static extern int sane_init(out int version, IntPtr authorize);
    [DllImport(Lib)] private static extern int sane_open([MarshalAs(UnmanagedType.LPUTF8Str)] string name, out IntPtr handle);
    [DllImport(Lib)] private static extern void sane_close(IntPtr handle);
    [DllImport(Lib)] private static extern IntPtr sane_get_option_descriptor(IntPtr handle, int option);
    [DllImport(Lib)] private static extern int sane_control_option(IntPtr handle, int option, int action, ref int value, IntPtr info);

    private static bool _initialized;

    public static PaperState Read(string deviceName, IReadOnlyList<string>? optionNames = null)
    {
        optionNames ??= SensorOptions;
        // sane_exit is never called here: NAPS2 keeps the library initialized between its own calls, and a
        // search for scanners after an exit it didn't make comes back empty. sane_init is safe to repeat, so it
        // is called once, and once more if the device won't open (NAPS2 may have ended the session meanwhile).
        if (!_initialized)
        {
            if (sane_init(out _, IntPtr.Zero) != StatusGood)
            {
                return PaperState.Unavailable;
            }
            _initialized = true;
        }
        if (sane_open(deviceName, out var handle) != StatusGood)
        {
            if (sane_init(out _, IntPtr.Zero) != StatusGood || sane_open(deviceName, out handle) != StatusGood)
            {
                return PaperState.Unavailable;
            }
        }
        try
        {
            // Option 0 is the number of options.
            var count = 0;
            if (sane_control_option(handle, 0, ActionGetValue, ref count, IntPtr.Zero) != StatusGood)
            {
                return PaperState.Unavailable;
            }
            for (var index = 1; index < count; index++)
            {
                var descriptor = sane_get_option_descriptor(handle, index);
                if (descriptor == IntPtr.Zero)
                {
                    continue;
                }
                // SANE_Option_Descriptor: name, title, desc (pointers), then type, unit, size, cap (ints).
                var name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(descriptor));
                var type = Marshal.ReadInt32(descriptor, 3 * IntPtr.Size);
                var size = Marshal.ReadInt32(descriptor, 3 * IntPtr.Size + 8);
                var cap = Marshal.ReadInt32(descriptor, 3 * IntPtr.Size + 12);
                if (name is null || !optionNames.Contains(name) || type != TypeBool || size != sizeof(int)
                    || (cap & CapSoftDetect) == 0 || (cap & CapInactive) != 0)
                {
                    continue;
                }
                var value = 0;
                return sane_control_option(handle, index, ActionGetValue, ref value, IntPtr.Zero) != StatusGood
                    ? PaperState.Unavailable
                    : value != 0 ? PaperState.Loaded : PaperState.Empty;
            }
            return PaperState.Unsupported;
        }
        finally
        {
            sane_close(handle);
        }
    }
}
