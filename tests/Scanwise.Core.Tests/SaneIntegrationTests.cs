using Scanwise.Core.Scanning;

namespace Scanwise.Core.Tests;

/// <summary>
/// Runs against SANE's built-in "test" backend. Enable it without root by running the tests with
/// <c>SANE_CONFIG_DIR</c> pointing at a folder whose dll.conf contains "test" (see scripts/test.sh).
/// </summary>
public class SaneIntegrationTests
{
    private static async Task<(TestEnv Env, ScannerService Service, DiscoveredScanner Device)?> Setup()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SANE_CONFIG_DIR")))
        {
            return null;
        }
        var env = new TestEnv();
        var service = new ScannerService(env.Context.Scanning);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await foreach (var device in service.DiscoverAsync(cts.Token))
        {
            if (device.Id.StartsWith("test:", StringComparison.Ordinal))
            {
                return (env, service, device);
            }
        }
        env.Dispose();
        return null;
    }

    [SkippableFact]
    public async Task ScansFlatbedAndFeeder()
    {
        var setup = await Setup();
        Skip.If(setup is null, "SANE test backend not enabled (set SANE_CONFIG_DIR)");
        var (env, service, device) = setup!.Value;
        using (env)
        {
            var caps = await service.GetCapabilitiesAsync(device);
            Assert.True(caps.Flatbed);
            Assert.True(caps.Feeder);
            Assert.Contains(300, caps.ResolutionsFor(ScanSource.Flatbed));

            var progress = new List<ScanProgress>();
            var flatbed = new ScanSettings { Source = ScanSource.Flatbed, Dpi = 75, PageSize = PageSizePreset.A5 };
            var pages = new List<NAPS2.Images.ProcessedImage>();
            await foreach (var page in service.ScanAsync(device, flatbed, caps, new Progress<ScanProgress>(progress.Add)))
            {
                pages.Add(page);
            }
            Assert.Single(pages);

            // The test backend's feeder delivers 10 sheets by default.
            var feeder = new ScanSettings { Source = ScanSource.Feeder, Dpi = 50, Color = ColorMode.Grayscale, AutoDeskew = false };
            var feederPages = 0;
            await foreach (var page in service.ScanAsync(device, feeder, caps))
            {
                feederPages++;
                page.Dispose();
            }
            Assert.True(feederPages > 1, $"expected several feeder pages, got {feederPages}");

            foreach (var page in pages)
            {
                page.Dispose();
            }
        }
    }

    [SkippableFact]
    public async Task RegionCropsFlatbedScans()
    {
        var setup = await Setup();
        Skip.If(setup is null, "SANE test backend not enabled (set SANE_CONFIG_DIR)");
        var (env, service, device) = setup!.Value;
        using (env)
        {
            var full = new ScanSettings { Source = ScanSource.Flatbed, Dpi = 50, PageSize = PageSizePreset.A5, AutoDeskew = false };
            var half = full.Clone();
            half.Region = new ScanRegion(0, 0, 0.5, 0.5);

            int Width(NAPS2.Images.ProcessedImage image)
            {
                using var rendered = env.Context.ImageContext.Render(image);
                return rendered.Width;
            }

            var widths = new List<int>();
            foreach (var settings in new[] { full, half })
            {
                await foreach (var page in service.ScanAsync(device, settings, null))
                {
                    widths.Add(Width(page));
                    page.Dispose();
                }
            }
            Assert.InRange(widths[1], widths[0] / 2 - 2, widths[0] / 2 + 2);
        }
    }
}
