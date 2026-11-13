using System.Net;
using System.Net.Sockets;
using System.Text;
using NAPS2.Escl;
using NAPS2.Scan;
using Deckle.Core.Scanning;

namespace Deckle.Core.Tests;

public class PaperSensorTests
{
    [Fact]
    public void TriggerWaitsForPaperToSettleAndFiresOnce()
    {
        var trigger = new PaperTrigger();
        Assert.False(trigger.Observe(PaperState.Empty));
        Assert.False(trigger.Observe(PaperState.Loaded));
        Assert.True(trigger.Observe(PaperState.Loaded));
        // Paper still in after the scan (jam, cancelled): no second scan.
        Assert.False(trigger.Observe(PaperState.Loaded));
        Assert.False(trigger.Observe(PaperState.Loaded));
        Assert.False(trigger.Observe(PaperState.Loaded));
    }

    [Fact]
    public void TriggerNeedsAnEmptyFeederBeforeTheNextScan()
    {
        var trigger = new PaperTrigger();
        trigger.Observe(PaperState.Loaded);
        Assert.True(trigger.Observe(PaperState.Loaded));
        // A failed reading says nothing about the feeder.
        Assert.False(trigger.Observe(PaperState.Unavailable));
        Assert.False(trigger.Observe(PaperState.Loaded));
        Assert.False(trigger.Observe(PaperState.Empty));
        Assert.False(trigger.Observe(PaperState.Loaded));
        Assert.True(trigger.Observe(PaperState.Loaded));
    }

    [Fact]
    public void TriggerStartsCountingAgainAfterAnInterruptedReading()
    {
        var trigger = new PaperTrigger();
        Assert.False(trigger.Observe(PaperState.Loaded));
        Assert.False(trigger.Observe(PaperState.Unavailable));
        Assert.False(trigger.Observe(PaperState.Loaded));
        Assert.True(trigger.Observe(PaperState.Loaded));
    }

    [Fact]
    public void TriggerResetCountsPaperAlreadyInTheFeeder()
    {
        var trigger = new PaperTrigger();
        trigger.Observe(PaperState.Loaded);
        Assert.True(trigger.Observe(PaperState.Loaded));
        trigger.Reset();
        Assert.False(trigger.Observe(PaperState.Loaded));
        Assert.True(trigger.Observe(PaperState.Loaded));
    }

    [Theory]
    [InlineData(EsclAdfState.ScannerAdfLoaded, EsclScannerState.Idle, PaperState.Loaded)]
    [InlineData(EsclAdfState.ScannerAdfLoaded, EsclScannerState.Processing, PaperState.Empty)]
    [InlineData(EsclAdfState.ScannerAdfEmpty, EsclScannerState.Idle, PaperState.Empty)]
    [InlineData(EsclAdfState.ScannerAdfJam, EsclScannerState.Stopped, PaperState.Empty)]
    [InlineData(EsclAdfState.Unknown, EsclScannerState.Idle, PaperState.Unsupported)]
    public void EsclStatusMapsToPaperState(EsclAdfState adf, EsclScannerState state, PaperState expected)
    {
        Assert.Equal(expected, EsclPaperSensor.Map(new EsclScannerStatus { AdfState = adf, State = state }));
    }

    [Theory]
    [InlineData("<scan:AdfState>ScannerAdfLoaded</scan:AdfState>", PaperState.Loaded)]
    [InlineData("<scan:AdfState>ScannerAdfEmpty</scan:AdfState>", PaperState.Empty)]
    [InlineData("", PaperState.Unsupported)]
    public async Task ReadsTheFeederStateFromAnEsclScanner(string adfElement, PaperState expected)
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var paths = new List<string>();
        var server = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }
                paths.Add(context.Request.Url!.AbsolutePath);
                var body = Encoding.UTF8.GetBytes(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
                    + "<scan:ScannerStatus xmlns:scan=\"http://schemas.hp.com/imaging/escl/2011/05/03\" xmlns:pwg=\"http://www.pwg.org/schemas/2010/12/sm\">"
                    + "<pwg:Version>2.6</pwg:Version><pwg:State>Idle</pwg:State>" + adfElement + "</scan:ScannerStatus>");
                context.Response.ContentType = "text/xml";
                context.Response.OutputStream.Write(body);
                context.Response.Close();
            }
        });

        using var env = new TestEnv();
        var service = new ScannerService(env.Context.Scanning);
        var device = new DiscoveredScanner(new ScanDevice(Driver.Escl, "11111111-2222-3333-4444-555555555555", "Network scanner", null, $"http://127.0.0.1:{port}/eSCL"));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var state = await service.GetPaperStateAsync(device, cts.Token);
        listener.Stop();
        await server;

        Assert.Equal(expected, state);
        Assert.Contains("/eSCL/ScannerStatus", paths);
    }

    [Fact]
    public async Task AnUnreachableNetworkScannerIsUnavailableNotAnError()
    {
        using var env = new TestEnv();
        var service = new ScannerService(env.Context.Scanning);
        var device = new DiscoveredScanner(new ScanDevice(Driver.Escl, "id", "Gone", null, $"http://127.0.0.1:{FreePort()}/eSCL"));
        Assert.Equal(PaperState.Unavailable, await service.GetPaperStateAsync(device));
    }

    [Fact]
    public async Task DriversWithoutASensorAreUnsupported()
    {
        using var env = new TestEnv();
        var service = new ScannerService(env.Context.Scanning);
        var twain = new DiscoveredScanner(new ScanDevice(Driver.Twain, "x", "TWAIN scanner"));
        Assert.Equal(PaperState.Unsupported, await service.GetPaperStateAsync(twain));
    }

    [SkippableFact]
    public async Task ReadsSaneOptionsWithoutDisturbingScans()
    {
        Skip.If(!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SANE_CONFIG_DIR")),
            "SANE test backend not enabled (set SANE_CONFIG_DIR)");
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        using var env = new TestEnv();
        var service = new ScannerService(env.Context.Scanning);
        DiscoveredScanner? device = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await foreach (var found in service.DiscoverAsync(cts.Token))
        {
            if (found.Id == "test:0")
            {
                device = found;
            }
        }
        Skip.If(device is null, "SANE test device not found");

        // The test backend has no hopper sensor…
        Assert.Equal(PaperState.Unsupported, await service.GetPaperStateAsync(device!));
        // …but its plain boolean options prove the option walk and the read: "hand-scanner" is off by default.
        Assert.Equal(PaperState.Empty, SanePaperSensor.Read("test:0", ["hand-scanner"]));
        Assert.Equal(PaperState.Unavailable, SanePaperSensor.Read("test:no-such-device"));

        // The sensor shares libsane with NAPS2; a scan afterwards (and readings around it) must still work.
        var settings = new ScanSettings { Source = ScanSource.Flatbed, Dpi = 50, PageSize = PageSizePreset.A5, AutoDeskew = false };
        var pages = 0;
        await foreach (var page in service.ScanAsync(device!, settings, null))
        {
            // While SANE is scanning the sensor stays out of the way.
            Assert.Equal(PaperState.Unavailable, await service.GetPaperStateAsync(device!));
            pages++;
            page.Dispose();
        }
        Assert.Equal(1, pages);
        Assert.Equal(PaperState.Unsupported, await service.GetPaperStateAsync(device!));

        // And so must the next search for scanners and the next capability query.
        var again = new List<string>();
        await foreach (var found in service.DiscoverAsync(cts.Token))
        {
            again.Add(found.Id);
        }
        Assert.Contains("test:0", again);
        Assert.True((await service.GetCapabilitiesAsync(device!)).Feeder);
        Assert.Equal(PaperState.Unsupported, await service.GetPaperStateAsync(device!));
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
