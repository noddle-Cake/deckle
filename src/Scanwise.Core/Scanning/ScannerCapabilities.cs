using NAPS2.Images;
using NAPS2.Scan;

namespace Scanwise.Core.Scanning;

/// <summary>What one input source of a device supports, flattened for the UI.</summary>
public sealed record SourceCapabilities(
    IReadOnlyList<int> Resolutions,
    bool Color,
    bool Grayscale,
    bool BlackAndWhite,
    PageSize? ScanArea);

/// <summary>Device capabilities in UI terms. Unknown capabilities are reported permissively.</summary>
public sealed class ScannerCapabilities
{
    public static readonly IReadOnlyList<int> FallbackResolutions = [100, 150, 200, 300, 400, 600];

    public bool Flatbed { get; init; } = true;
    public bool Feeder { get; init; }
    public bool Duplex { get; init; }
    public SourceCapabilities? FlatbedCaps { get; init; }
    public SourceCapabilities? FeederCaps { get; init; }
    public SourceCapabilities? DuplexCaps { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }

    /// <summary>True when the driver could not report capabilities and everything is offered.</summary>
    public bool IsGuess { get; init; }

    public static ScannerCapabilities Unknown { get; } = new()
    {
        Flatbed = true,
        Feeder = true,
        Duplex = true,
        IsGuess = true,
    };

    public SourceCapabilities? ForSource(ScanSource source) => source switch
    {
        ScanSource.Flatbed => FlatbedCaps,
        ScanSource.Feeder => FeederCaps ?? DuplexCaps,
        ScanSource.Duplex => DuplexCaps ?? FeederCaps,
        _ => FlatbedCaps ?? FeederCaps,
    };

    public bool Supports(ScanSource source) => source switch
    {
        ScanSource.Auto => true,
        ScanSource.Flatbed => Flatbed,
        ScanSource.Feeder => Feeder,
        ScanSource.Duplex => Duplex,
        _ => false,
    };

    public IReadOnlyList<int> ResolutionsFor(ScanSource source)
    {
        var values = ForSource(source)?.Resolutions;
        return values is { Count: > 0 } ? values : FallbackResolutions;
    }

    public bool Supports(ScanSource source, ColorMode mode)
    {
        var caps = ForSource(source);
        if (caps is null || (!caps.Color && !caps.Grayscale && !caps.BlackAndWhite))
        {
            return true;
        }
        return mode switch
        {
            ColorMode.Color => caps.Color,
            ColorMode.Grayscale => caps.Grayscale,
            ColorMode.BlackAndWhite => caps.BlackAndWhite,
            _ => false,
        };
    }

    /// <summary>Picks the supported resolution closest to <paramref name="wanted"/>.</summary>
    public int NearestResolution(ScanSource source, int wanted)
    {
        var values = ResolutionsFor(source);
        return values.OrderBy(v => Math.Abs(v - wanted)).ThenByDescending(v => v).First();
    }

    public static ScannerCapabilities From(ScanCaps caps)
    {
        var paper = caps.PaperSourceCaps;
        var flatbed = Convert(caps.FlatbedCaps);
        var feeder = Convert(caps.FeederCaps);
        var duplex = Convert(caps.DuplexCaps);
        return new ScannerCapabilities
        {
            Flatbed = paper?.SupportsFlatbed ?? flatbed is not null,
            Feeder = paper?.SupportsFeeder ?? feeder is not null,
            Duplex = paper?.SupportsDuplex ?? duplex is not null,
            FlatbedCaps = flatbed,
            FeederCaps = feeder,
            DuplexCaps = duplex,
            Manufacturer = caps.MetadataCaps?.Manufacturer,
            Model = caps.MetadataCaps?.Model,
            IsGuess = paper is null,
        };
    }

    private static SourceCapabilities? Convert(PerSourceCaps? caps)
    {
        if (caps is null)
        {
            return null;
        }
        // Prefer the driver's "common" list; a full range (e.g. 1..1200 step 1) is useless in a dropdown.
        IEnumerable<int> dpi = caps.DpiCaps?.CommonValues is { Count: > 0 } common
            ? common
            : caps.DpiCaps?.Values ?? [];
        var resolutions = dpi.Where(v => v >= 50).Distinct().OrderBy(v => v).ToList();
        var bits = caps.BitDepthCaps;
        return new SourceCapabilities(
            resolutions,
            bits?.SupportsColor ?? true,
            bits?.SupportsGrayscale ?? true,
            bits?.SupportsBlackAndWhite ?? true,
            caps.PageSizeCaps?.ScanArea);
    }
}
