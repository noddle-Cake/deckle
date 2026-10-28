using NAPS2.Images;
using NAPS2.Scan;
using Scanwise.Core.Export;

namespace Scanwise.Core.Scanning;

public enum ScanSource { Auto, Flatbed, Feeder, Duplex }

public enum ColorMode { Color, Grayscale, BlackAndWhite }

/// <summary>Named page size presets; <see cref="Auto"/> uses the device's full scan area.</summary>
public enum PageSizePreset { Auto, A4, Letter, Legal, A5, A3, B5, B4, Custom }

/// <summary>
/// One scan profile: everything needed to run a scan. The scan options pane edits the active profile directly,
/// so "remember my last options" and NAPS2-style named profiles are the same thing.
/// </summary>
public sealed class ScanSettings
{
    public string Name { get; set; } = "Default";

    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public Driver? DeviceDriver { get; set; }

    public ScanSource Source { get; set; } = ScanSource.Auto;
    public ColorMode Color { get; set; } = ColorMode.Color;
    public int Dpi { get; set; } = 300;
    public OutputFormat Format { get; set; } = OutputFormat.Pdf;

    public PageSizePreset PageSize { get; set; } = PageSizePreset.Auto;
    /// <summary>Custom page size in millimetres, used when <see cref="PageSize"/> is Custom.</summary>
    public decimal CustomWidthMm { get; set; } = 210;
    public decimal CustomHeightMm { get; set; } = 297;

    /// <summary>-100..100, applied by the driver where supported, otherwise after the scan.</summary>
    public int Brightness { get; set; }
    /// <summary>-100..100.</summary>
    public int Contrast { get; set; }

    public bool AutoDeskew { get; set; } = true;
    public bool ExcludeBlankPages { get; set; }
    /// <summary>0..100, how white a pixel must be to count as blank (NAPS2 default 70).</summary>
    public int BlankWhiteThreshold { get; set; } = 70;
    /// <summary>0..100, how much non-white coverage still counts as blank (NAPS2 default 15).</summary>
    public int BlankCoverageThreshold { get; set; } = 15;

    public bool MaxQuality { get; set; }
    public bool FlipDuplexedPages { get; set; }

    /// <summary>Flatbed region chosen in the preview, as fractions of the scanned page (null = whole page).</summary>
    public ScanRegion? Region { get; set; }

    public ScanSettings Clone()
    {
        var copy = (ScanSettings)MemberwiseClone();
        copy.Region = Region is null ? null : Region with { };
        return copy;
    }

    public NAPS2.Images.PageSize? ResolvePageSize(ScannerCapabilities? caps)
    {
        return PageSize switch
        {
            PageSizePreset.A4 => NAPS2.Images.PageSize.A4,
            PageSizePreset.Letter => NAPS2.Images.PageSize.Letter,
            PageSizePreset.Legal => NAPS2.Images.PageSize.Legal,
            PageSizePreset.A5 => NAPS2.Images.PageSize.A5,
            PageSizePreset.A3 => NAPS2.Images.PageSize.A3,
            PageSizePreset.B5 => NAPS2.Images.PageSize.B5,
            PageSizePreset.B4 => NAPS2.Images.PageSize.B4,
            PageSizePreset.Custom => new NAPS2.Images.PageSize(CustomWidthMm, CustomHeightMm, PageSizeUnit.Millimetre),
            _ => caps?.ForSource(Source)?.ScanArea,
        };
    }

    public ScanOptions ToScanOptions(ScanDevice device, ScannerCapabilities? caps)
    {
        return new ScanOptions
        {
            Device = device,
            Driver = device.Driver,
            PaperSource = Source switch
            {
                ScanSource.Flatbed => PaperSource.Flatbed,
                ScanSource.Feeder => PaperSource.Feeder,
                ScanSource.Duplex => PaperSource.Duplex,
                _ => PaperSource.Auto,
            },
            BitDepth = Color switch
            {
                ColorMode.Grayscale => BitDepth.Grayscale,
                ColorMode.BlackAndWhite => BitDepth.BlackAndWhite,
                _ => BitDepth.Color,
            },
            Dpi = Dpi,
            PageSize = ResolvePageSize(caps),
            // NAPS2 uses -1000..1000.
            Brightness = Math.Clamp(Brightness, -100, 100) * 10,
            Contrast = Math.Clamp(Contrast, -100, 100) * 10,
            AutoDeskew = AutoDeskew,
            ExcludeBlankPages = ExcludeBlankPages,
            BlankPageWhiteThreshold = Math.Clamp(BlankWhiteThreshold, 0, 100),
            BlankPageCoverageThreshold = Math.Clamp(BlankCoverageThreshold, 0, 100),
            MaxQuality = MaxQuality,
            FlipDuplexedPages = FlipDuplexedPages,
        };
    }
}

/// <summary>A rectangle in 0..1 page coordinates.</summary>
public sealed record ScanRegion(double Left, double Top, double Right, double Bottom)
{
    public bool IsValid => Left >= 0 && Top >= 0 && Right <= 1 && Bottom <= 1 && Right - Left > 0.01 && Bottom - Top > 0.01;
}
