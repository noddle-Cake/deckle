using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Deckle.Core.Scanning;

namespace Deckle.Core.Export;

/// <summary>Values a file name pattern can refer to.</summary>
public sealed record NamingContext(
    DateTime Time,
    OutputFormat Format,
    string? ScannerName = null,
    int? Dpi = null,
    ColorMode? Color = null,
    ScanSource? Source = null,
    int? Brightness = null,
    int? Contrast = null);

public sealed record NamingToken(string Token, string Label, string Example);

/// <summary>
/// Turns patterns like <c>Scan {year}-{month}-{day}</c> into file names. This combines Scanner's building
/// blocks with NAPS2's auto-numbering: <c>{n}</c> / <c>{nnn}</c> pick the first number that is not taken yet.
/// </summary>
public static partial class FileNamer
{
    public const string DateTimePattern = "Scan {year}-{month}-{day} {hour}.{minute}.{second}";
    public const string DatePattern = "Scan {year}-{month}-{day}";

    public static IReadOnlyList<NamingToken> Tokens { get; } =
    [
        new("{year}", "Year", "2026"),
        new("{month}", "Month", "10"),
        new("{monthname}", "Month name", "October"),
        new("{day}", "Day", "28"),
        new("{weekday}", "Weekday", "Wednesday"),
        new("{hour}", "Hour (24 h)", "15"),
        new("{hour12}", "Hour (12 h)", "03"),
        new("{ampm}", "AM/PM", "PM"),
        new("{minute}", "Minute", "04"),
        new("{second}", "Second", "09"),
        new("{n}", "Number", "1"),
        new("{nnn}", "Number (3 digits)", "001"),
        new("{scanner}", "Scanner name", "Canon LiDE 300"),
        new("{dpi}", "Resolution", "300"),
        new("{color}", "Color mode", "Color"),
        new("{source}", "Source", "Flatbed"),
        new("{brightness}", "Brightness", "0"),
        new("{contrast}", "Contrast", "0"),
        new("{format}", "File type", "PDF"),
    ];

    [GeneratedRegex(@"\{(n+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex CounterRegex();

    [GeneratedRegex(@"\{([a-z0-9]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    public static bool HasCounter(string pattern) => CounterRegex().IsMatch(pattern);

    /// <summary>Renders every token except the counter, which is replaced with <paramref name="counter"/>.</summary>
    public static string Render(string pattern, NamingContext ctx, int counter = 1)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            pattern = DateTimePattern;
        }
        var culture = CultureInfo.CurrentCulture;
        var t = ctx.Time;
        var result = TokenRegex().Replace(pattern, m =>
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            if (key.All(c => c == 'n'))
            {
                return counter.ToString(new string('0', key.Length), CultureInfo.InvariantCulture);
            }
            return key switch
            {
                "year" => t.ToString("yyyy", CultureInfo.InvariantCulture),
                "month" => t.ToString("MM", CultureInfo.InvariantCulture),
                "monthname" => culture.DateTimeFormat.GetMonthName(t.Month),
                "day" => t.ToString("dd", CultureInfo.InvariantCulture),
                "weekday" => culture.DateTimeFormat.GetDayName(t.DayOfWeek),
                "hour" => t.ToString("HH", CultureInfo.InvariantCulture),
                "hour12" => t.ToString("hh", CultureInfo.InvariantCulture),
                "ampm" => t.Hour < 12 ? "AM" : "PM",
                "minute" => t.ToString("mm", CultureInfo.InvariantCulture),
                "second" => t.ToString("ss", CultureInfo.InvariantCulture),
                "scanner" => ctx.ScannerName ?? "Scanner",
                "dpi" => ctx.Dpi?.ToString(CultureInfo.InvariantCulture) ?? "",
                "color" => ctx.Color switch
                {
                    ColorMode.Grayscale => "Grayscale",
                    ColorMode.BlackAndWhite => "BW",
                    ColorMode.Color => "Color",
                    _ => "",
                },
                "source" => ctx.Source?.ToString() ?? "",
                "brightness" => ctx.Brightness?.ToString(CultureInfo.InvariantCulture) ?? "",
                "contrast" => ctx.Contrast?.ToString(CultureInfo.InvariantCulture) ?? "",
                "format" => ctx.Format.DisplayName(),
                // Unknown tokens stay visible so typos are noticed in the preview.
                _ => m.Value,
            };
        });
        result = Sanitize(result);
        return result.Length == 0 ? "Scan" : result;
    }

    /// <summary>Preview text for the settings page (uses example values and the current time).</summary>
    public static string Preview(string pattern, OutputFormat format) =>
        Render(pattern, new NamingContext(DateTime.Now, format, "Scanner", 300, ColorMode.Color, ScanSource.Flatbed, 0, 0)) + format.Extension();

    /// <summary>Replaces characters that are invalid on any of Windows, macOS or Linux.</summary>
    public static string Sanitize(string name)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
        }
        // Windows rejects trailing dots and spaces.
        return sb.ToString().Trim().TrimEnd('.', ' ');
    }

    /// <summary>
    /// Picks a free path for one file. With a counter token the counter is incremented; otherwise
    /// " (2)", " (3)"… is appended when the name is taken.
    /// </summary>
    public static string ResolvePath(string folder, string pattern, NamingContext ctx) =>
        ResolvePaths(folder, pattern, ctx, 1)[0];

    /// <summary>
    /// Picks <paramref name="count"/> free paths that share one base name. With more than one page each file gets
    /// a page suffix ("Scan 2026-10-28 (page 01).jpg").
    /// </summary>
    public static IReadOnlyList<string> ResolvePaths(string folder, string pattern, NamingContext ctx, int count)
    {
        var ext = ctx.Format.Extension();
        var hasCounter = HasCounter(pattern);
        var digits = Math.Max(2, count.ToString(CultureInfo.InvariantCulture).Length);
        for (var attempt = 1; attempt < 100_000; attempt++)
        {
            string baseName;
            if (hasCounter)
            {
                baseName = Render(pattern, ctx, attempt);
            }
            else
            {
                baseName = Render(pattern, ctx);
                if (attempt > 1)
                {
                    baseName += $" ({attempt})";
                }
            }

            var paths = count == 1
                ? [Path.Combine(folder, baseName + ext)]
                : Enumerable.Range(1, count)
                    .Select(i => Path.Combine(folder, $"{baseName} (page {i.ToString(new string('0', digits), CultureInfo.InvariantCulture)}){ext}"))
                    .ToList();
            if (paths.All(p => !File.Exists(p)))
            {
                return paths;
            }
        }
        throw new IOException($"Could not find a free file name in {folder}.");
    }
}
