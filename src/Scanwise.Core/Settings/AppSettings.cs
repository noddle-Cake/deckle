using Scanwise.Core.Export;

namespace Scanwise.Core.Settings;

public enum AppTheme { System, Light, Dark }

public enum SaveLocationMode
{
    /// <summary>Scans are written to <see cref="AppSettings.SaveFolder"/> without asking.</summary>
    Folder,
    /// <summary>A save dialog opens for every new file.</summary>
    AskEveryTime,
}

public enum ScanAction
{
    /// <summary>New scans are appended to the pages already in the list (and to the current PDF/TIFF).</summary>
    AddToExisting,
    /// <summary>Each scan clears the list and starts a new file.</summary>
    StartFresh,
}

public enum MeasurementUnits { Metric, Imperial }

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // Appearance
    public AppTheme Theme { get; set; } = AppTheme.System;
    public MeasurementUnits Units { get; set; } = MeasurementUnits.Metric;

    // Saving
    public SaveLocationMode SaveLocation { get; set; } = SaveLocationMode.Folder;
    public string SaveFolder { get; set; } = AppPaths.DefaultScanFolder();
    public bool AutoSave { get; set; } = true;
    public ScanAction ScanAction { get; set; } = ScanAction.AddToExisting;
    public string FileNamePattern { get; set; } = FileNamer.DateTimePattern;

    // Processing
    public bool AutoRotate { get; set; } = true;
    public string OcrLanguage { get; set; } = "eng";

    // Output
    public PdfSettings Pdf { get; set; } = new();
    public ImageSettings Images { get; set; } = new();

    // UI state
    public string? LastProfile { get; set; }
    public bool RestoreSession { get; set; } = true;
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
}

public sealed class PdfSettings
{
    /// <summary>Embed a text layer with Tesseract so the PDF is searchable (skipped when no language data is found).</summary>
    public bool Ocr { get; set; } = true;
    public PdfStandard Standard { get; set; } = PdfStandard.Default;
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? Subject { get; set; }
    public string? Keywords { get; set; }
    public bool Encrypt { get; set; }
    public string? UserPassword { get; set; }
    public string? OwnerPassword { get; set; }
    public bool AllowPrinting { get; set; } = true;
    public bool AllowCopying { get; set; } = true;
    public bool AllowModification { get; set; } = true;
}

public enum PdfStandard { Default, PdfA1B, PdfA2B, PdfA3B, PdfA3U }

public sealed class ImageSettings
{
    /// <summary>JPEG quality 1-100.</summary>
    public int JpegQuality { get; set; } = 90;
    public TiffCompression TiffCompression { get; set; } = TiffCompression.Auto;
}

public enum TiffCompression { Auto, Lzw, Ccitt4, None }
