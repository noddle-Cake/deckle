using NAPS2.Images;
using NAPS2.Images.ImageSharp;
using NAPS2.Ocr;
using NAPS2.Pdf;
using NAPS2.Scan;
using Scanwise.Core.Processing;
using Scanwise.Core.Settings;

namespace Scanwise.Core.Export;

public sealed record ExportProgress(int Done, int Total, string Stage);

public sealed class ExportService
{
    private readonly ScanningContext _scanning;
    private readonly ImageSharpImageContext _imageContext;
    private readonly TesseractLocator _tesseract;

    public ExportService(ScanningContext scanning, ImageSharpImageContext imageContext, TesseractLocator tesseract)
    {
        _scanning = scanning;
        _imageContext = imageContext;
        _tesseract = tesseract;
    }

    /// <summary>
    /// Writes a multi-page document (PDF or TIFF) to <paramref name="path"/>. The file is written to a temp name
    /// first and moved into place, so a failed or cancelled save never leaves a half-written file behind.
    /// </summary>
    public async Task ExportDocumentAsync(
        IReadOnlyList<ProcessedImage> pages,
        string path,
        OutputFormat format,
        AppSettings settings,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancelToken = default)
    {
        if (!format.IsMultiPage())
        {
            throw new ArgumentException($"{format} is a single-page format; use {nameof(ExportPagesAsync)}.", nameof(format));
        }
        if (pages.Count == 0)
        {
            throw new ArgumentException("Nothing to save.", nameof(pages));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = TempPathNextTo(path);
        try
        {
            if (format == OutputFormat.Pdf)
            {
                await ExportPdfAsync(pages, temp, settings, progress, cancelToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Run(() => TiffWriter.Write(
                    temp, pages, _imageContext, settings.Images.TiffCompression,
                    done => progress?.Report(new ExportProgress(done, pages.Count, "Saving TIFF")),
                    cancelToken), cancelToken).ConfigureAwait(false);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>Writes one image file per page. <paramref name="paths"/> must have one entry per page.</summary>
    public async Task ExportPagesAsync(
        IReadOnlyList<ProcessedImage> pages,
        IReadOnlyList<string> paths,
        OutputFormat format,
        AppSettings settings,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancelToken = default)
    {
        if (paths.Count != pages.Count)
        {
            throw new ArgumentException("One path per page is required.", nameof(paths));
        }
        if (format == OutputFormat.Pdf)
        {
            throw new ArgumentException("PDF pages must be exported with ExportDocumentAsync.", nameof(format));
        }
        var options = new ImageSaveOptions { Quality = Math.Clamp(settings.Images.JpegQuality, 1, 100) };
        for (var i = 0; i < pages.Count; i++)
        {
            cancelToken.ThrowIfCancellationRequested();
            var path = paths[i];
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var page = pages[i];
            var temp = TempPathNextTo(path);
            try
            {
                if (format == OutputFormat.Tiff)
                {
                    await Task.Run(() => TiffWriter.Write(temp, [page], _imageContext, settings.Images.TiffCompression, null, cancelToken), cancelToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Run(() => page.Save(temp, format.ToImageFileFormat(), options), cancelToken).ConfigureAwait(false);
                }
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                TryDelete(temp);
            }
            progress?.Report(new ExportProgress(i + 1, pages.Count, $"Saving {format.DisplayName()}"));
        }
    }

    /// <summary>True when searchable PDFs can be produced for <paramref name="language"/>.</summary>
    public bool CanOcr(string language) => _tesseract.Find()?.HasLanguage(language) == true;

    private async Task ExportPdfAsync(
        IReadOnlyList<ProcessedImage> pages,
        string path,
        AppSettings settings,
        IProgress<ExportProgress>? progress,
        CancellationToken cancelToken)
    {
        var pdf = settings.Pdf;
        var exportParams = new PdfExportParams
        {
            Metadata = new PdfMetadata
            {
                Title = pdf.Title ?? "",
                Author = pdf.Author ?? "",
                Subject = pdf.Subject ?? "",
                Keywords = pdf.Keywords ?? "",
                Creator = "Scanwise",
            },
            Compat = pdf.Standard switch
            {
                PdfStandard.PdfA1B => PdfCompat.PdfA1B,
                PdfStandard.PdfA2B => PdfCompat.PdfA2B,
                PdfStandard.PdfA3B => PdfCompat.PdfA3B,
                PdfStandard.PdfA3U => PdfCompat.PdfA3U,
                _ => PdfCompat.Default,
            },
        };
        // PDF/A forbids encryption.
        if (pdf.Encrypt && pdf.Standard == PdfStandard.Default && !string.IsNullOrEmpty(pdf.UserPassword + pdf.OwnerPassword))
        {
            exportParams = exportParams with
            {
                Encryption = new PdfEncryption
                {
                    EncryptPdf = true,
                    UserPassword = pdf.UserPassword,
                    OwnerPassword = string.IsNullOrEmpty(pdf.OwnerPassword) ? pdf.UserPassword : pdf.OwnerPassword,
                    AllowPrinting = pdf.AllowPrinting,
                    AllowFullQualityPrinting = pdf.AllowPrinting,
                    AllowContentCopying = pdf.AllowCopying,
                    AllowContentCopyingForAccessibility = true,
                    AllowDocumentModification = pdf.AllowModification,
                    AllowDocumentAssembly = pdf.AllowModification,
                    AllowAnnotations = pdf.AllowModification,
                    AllowFormFilling = pdf.AllowModification,
                },
            };
        }

        OcrParams? ocrParams = null;
        var install = _tesseract.Find();
        if (pdf.Ocr && install?.FindDataDir(settings.OcrLanguage) is { } dataDir)
        {
            _scanning.OcrEngine = TesseractOcrEngine.Custom(install.ExePath, dataDir);
            ocrParams = new OcrParams(settings.OcrLanguage, OcrMode.Default, 0);
        }

        var stage = ocrParams is null ? "Saving PDF" : "Recognizing text and saving PDF";
        var images = pages.ToList();
        var ok = await new PdfExporter(_scanning).Export(
            path,
            images,
            exportParams,
            ocrParams,
            new NAPS2.Util.ProgressHandler((done, total) => progress?.Report(new ExportProgress(done, total, stage)), cancelToken)).ConfigureAwait(false);
        cancelToken.ThrowIfCancellationRequested();
        if (!ok)
        {
            throw new IOException("The PDF could not be written.");
        }
    }

    private static string TempPathNextTo(string path) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
