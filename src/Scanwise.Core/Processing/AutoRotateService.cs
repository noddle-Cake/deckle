using System.Globalization;
using System.Text.RegularExpressions;
using NAPS2.Images;
using NAPS2.Images.ImageSharp;
using NAPS2.Images.Transforms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Scanwise.Core.Processing;

public enum RotationMethod { None, OrientationDetection, TextComparison }

public sealed record RotationResult(int Degrees, RotationMethod Method, double Confidence);

/// <summary>
/// Fixes pages that were scanned sideways or upside down.
/// <list type="number">
/// <item>Tesseract orientation detection (<c>--psm 0</c>, needs osd.traineddata): one pass with a confidence value.
/// Very confident answers are used as they are.</item>
/// <item>OSD gets the axis right but often the direction wrong (small print turned 90° came back as 270°: landscape,
/// but upside down). So a less confident answer is checked by reading the page at OSD's angle and at the opposite
/// one, and the one with more confidently read text wins.</item>
/// <item>Without a usable OSD answer, Scanner's approach: OCR the page at 0/90/180/270° and keep the angle that
/// reads clearly the most text. Unlike Scanner, only confidently recognized words count: upside-down text OCRs into
/// letter-like garbage, and Tesseract's automatic layout mode quietly reads sideways text, so page mode 6
/// (horizontal lines) is used.</item>
/// </list>
/// AutoRotateAccuracyTests measures all of this on realistic pages (SCANWISE_ROTATE_EVAL=1).
/// Small skew is handled separately by NAPS2's auto-deskew during the scan.
/// </summary>
public sealed partial class AutoRotateService
{
    /// <summary>Minimum OSD confidence; Tesseract reports ~0-20, values under 2 are mostly guesses.</summary>
    public const double MinOsdConfidence = 2.0;

    /// <summary>
    /// OSD answers this sure are taken as they are. Below it, the text is read both ways round to check the
    /// direction (wrong OSD answers on small print came with confidence 2.9-5.6; right ones on clean pages 10+).
    /// </summary>
    public const double ConfidentOsd = 10.0;

    /// <summary>The winning angle must read this many times more confident text than the next best.</summary>
    private const double ClearWinner = 1.2;

    /// <summary>
    /// The angles are read in parallel, one Tesseract each. By default every Tesseract uses all cores (OpenMP), and
    /// several at once thrash until they time out, which used to mean "don't rotate" (Tesseract's docs recommend
    /// this setting for parallel runs).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SingleThreaded = new Dictionary<string, string> { ["OMP_THREAD_LIMIT"] = "1" };

    /// <summary>Scanner ignored pages with fewer than 50 recognized characters; so do we.</summary>
    public const int MinTextCharacters = 50;

    private const int MaxSide = 2400;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly ImageSharpImageContext _imageContext;
    private readonly TesseractLocator _locator;
    private readonly string _tempDir;

    public AutoRotateService(ImageSharpImageContext imageContext, TesseractLocator locator, string tempDir)
    {
        _imageContext = imageContext;
        _locator = locator;
        _tempDir = tempDir;
    }

    public bool IsAvailable => _locator.Find() is not null;

    /// <summary>Returns the image rotated upright. The input is disposed when a new image is returned.</summary>
    public async Task<ProcessedImage> AutoRotateAsync(ProcessedImage image, string language, CancellationToken cancelToken = default)
    {
        var result = await DetectAsync(image, language, cancelToken).ConfigureAwait(false);
        return result.Degrees == 0 ? image : image.WithTransform(new RotationTransform(result.Degrees), disposeSelf: true);
    }

    /// <summary>Clockwise rotation (0, 90, 180, 270) that makes the page upright. Never throws except on cancel.</summary>
    public async Task<RotationResult> DetectAsync(ProcessedImage image, string language, CancellationToken cancelToken = default)
    {
        var install = _locator.Find();
        if (install is null)
        {
            return new RotationResult(0, RotationMethod.None, 0);
        }

        Directory.CreateDirectory(_tempDir);
        var file = Path.Combine(_tempDir, $"rotate-{Guid.NewGuid():N}.png");
        try
        {
            using (var rendered = _imageContext.Render(image))
            {
                var source = ((ImageSharpImage)rendered).Image;
                using var copy = source.Clone(ctx =>
                {
                    var scale = Math.Min(1.0, (double)MaxSide / Math.Max(source.Width, source.Height));
                    if (scale < 1.0)
                    {
                        ctx.Resize((int)(source.Width * scale), (int)(source.Height * scale));
                    }
                });
                await copy.SaveAsPngAsync(file, cancelToken).ConfigureAwait(false);
            }

            var osd = install.HasOsd ? await RunOsdAsync(install, file, cancelToken).ConfigureAwait(false) : null;
            if (osd is { Confidence: >= ConfidentOsd })
            {
                return osd;
            }
            // OSD finds the axis (upright or sideways) reliably but often picks the wrong way round: small print
            // turned 90° comes back as 270°, i.e. landscape but upside down. So unless it is very sure, read the
            // page both ways round. Without a usable OSD answer, try all four angles.
            int[] candidates = osd is { Confidence: >= MinOsdConfidence }
                ? [osd.Degrees, (osd.Degrees + 180) % 360]
                : [0, 90, 180, 270];
            RotationResult? text = null;
            try
            {
                text = await CompareTextAsync(install, file, language, candidates, cancelToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fall back to OSD's answer below.
            }
            if (text is not null)
            {
                return text;
            }
            // The text comparison couldn't decide (too little text): go with OSD if it had an opinion.
            return osd is { Confidence: >= MinOsdConfidence } ? osd : new RotationResult(0, RotationMethod.None, osd?.Confidence ?? 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new RotationResult(0, RotationMethod.None, 0);
        }
        finally
        {
            TryDelete(file);
        }
    }

    [GeneratedRegex(@"^Rotate:\s*(\d+)", RegexOptions.Multiline)]
    private static partial Regex RotateRegex();

    [GeneratedRegex(@"^Orientation confidence:\s*([\d.]+)", RegexOptions.Multiline)]
    private static partial Regex ConfidenceRegex();

    private static async Task<RotationResult?> RunOsdAsync(TesseractInstall install, string file, CancellationToken cancelToken)
    {
        var dataDir = install.FindDataDir("osd")!;
        var result = await ProcessRunner.RunAsync(
            install.ExePath,
            [file, "stdout", "--psm", "0", "--tessdata-dir", dataDir],
            null, Timeout, cancelToken).ConfigureAwait(false);
        var rotate = RotateRegex().Match(result.StdOut);
        var confidence = ConfidenceRegex().Match(result.StdOut);
        if (result.ExitCode != 0 || !rotate.Success || !confidence.Success)
        {
            // "Too few characters" and similar: let the text comparison try.
            return null;
        }
        var degrees = int.Parse(rotate.Groups[1].Value, CultureInfo.InvariantCulture) % 360;
        var conf = double.Parse(confidence.Groups[1].Value, CultureInfo.InvariantCulture);
        return new RotationResult(degrees, RotationMethod.OrientationDetection, conf);
    }

    /// <summary>
    /// OCRs the page at each candidate angle and returns the one that reads clearly more confident text, or null
    /// when none does (too little text, or a tie).
    /// </summary>
    private async Task<RotationResult?> CompareTextAsync(TesseractInstall install, string file, string language, int[] candidates,
        CancellationToken cancelToken)
    {
        var lang = install.HasLanguage(language) ? language : install.Languages.FirstOrDefault();
        if (lang is null)
        {
            return null;
        }
        var dataDir = install.FindDataDir(lang)!;

        var variants = new List<(int Degrees, string Path)>();
        using (var original = await Image.LoadAsync(file, cancelToken).ConfigureAwait(false))
        {
            foreach (var degrees in candidates)
            {
                if (degrees == 0)
                {
                    variants.Add((0, file));
                    continue;
                }
                var path = Path.Combine(_tempDir, $"rotate-{Guid.NewGuid():N}-{degrees}.png");
                using var rotated = original.Clone(ctx => ctx.Rotate(degrees));
                await rotated.SaveAsPngAsync(path, cancelToken).ConfigureAwait(false);
                variants.Add((degrees, path));
            }
        }

        try
        {
            var scores = await Task.WhenAll(variants.Select(async v =>
            {
                try
                {
                    var result = await ProcessRunner.RunAsync(
                        install.ExePath,
                        [v.Path, "stdout", "-l", lang, "--psm", "6", "--tessdata-dir", dataDir, "tsv"],
                        SingleThreaded, Timeout, cancelToken).ConfigureAwait(false);
                    return (v.Degrees, Score: result.ExitCode == 0 ? ScoreTsv(result.StdOut) : 0);
                }
                catch (TimeoutException)
                {
                    return (v.Degrees, Score: 0); // One slow angle shouldn't sink the others.
                }
            })).ConfigureAwait(false);

            var ranked = scores.OrderByDescending(s => s.Score).ToList();
            var best = ranked[0];
            var runnerUp = ranked.Count > 1 ? ranked[1].Score : 0;
            // Only a clear winner counts: enough text, and clearly more than any other angle.
            if (best.Score < MinTextCharacters || best.Score < runnerUp * ClearWinner)
            {
                return null;
            }
            return new RotationResult(best.Degrees, RotationMethod.TextComparison, best.Score);
        }
        finally
        {
            foreach (var v in variants.Where(v => v.Path != file))
            {
                TryDelete(v.Path);
            }
        }
    }

    /// <summary>Minimum Tesseract word confidence (0-100) for a word to count.</summary>
    private const double MinWordConfidence = 60;

    /// <summary>
    /// Scores Tesseract TSV output: letters and digits of words with 2+ such characters that were recognized with
    /// at least <see cref="MinWordConfidence"/> confidence.
    /// </summary>
    internal static int ScoreTsv(string tsv)
    {
        var score = 0;
        foreach (var line in tsv.Split('\n').Skip(1))
        {
            var columns = line.TrimEnd('\r').Split('\t');
            if (columns.Length < 12
                || !double.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence)
                || confidence < MinWordConfidence)
            {
                continue;
            }
            var alnum = columns[11].Count(char.IsLetterOrDigit);
            if (alnum >= 2)
            {
                score += alnum;
            }
        }
        return score;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
