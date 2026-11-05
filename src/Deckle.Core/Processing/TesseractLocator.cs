using System.Runtime.InteropServices;
using Deckle.Core.Settings;

namespace Deckle.Core.Processing;

/// <summary>A usable Tesseract install: the executable plus every folder with language data.</summary>
public sealed record TesseractInstall(string ExePath, IReadOnlyList<string> DataDirs, bool IsBundled)
{
    public IReadOnlyList<string> Languages { get; } = DataDirs
        .Where(Directory.Exists)
        .SelectMany(d => Directory.EnumerateFiles(d, "*.traineddata"))
        .Select(f => Path.GetFileNameWithoutExtension(f))
        .Where(l => l != "osd" && l != "equ")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public bool HasOsd => FindDataDir("osd") is not null;

    /// <summary>Returns the first data folder that holds every language in <paramref name="languages"/> ("eng+deu").</summary>
    public string? FindDataDir(string languages)
    {
        var wanted = languages.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return DataDirs.FirstOrDefault(d => wanted.All(l => File.Exists(Path.Combine(d, l + ".traineddata"))));
    }

    public bool HasLanguage(string languages) => FindDataDir(languages) is not null;
}

/// <summary>
/// Finds Tesseract: a system install first (it usually ships language data), then the copy bundled with the app.
/// Downloaded language files go to the app's own tessdata folder; English and OSD data ship with the app.
/// </summary>
public sealed class TesseractLocator
{
    public const string DownloadBaseUrl = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/";

    private readonly AppPaths _paths;
    private TesseractInstall? _cached;
    private bool _searched;

    public TesseractLocator(AppPaths paths)
    {
        _paths = paths;
    }

    public string UserDataDir => _paths.TessdataDir;

    /// <summary>Language data shipped next to the app by scripts/publish.sh.</summary>
    public static string BundledDataDir => Path.Combine(AppContext.BaseDirectory, "tessdata");

    public TesseractInstall? Find()
    {
        if (!_searched)
        {
            _cached = Search();
            _searched = true;
        }
        return _cached;
    }

    /// <summary>Forget the cached result (after downloading languages).</summary>
    public void Refresh() => _searched = false;

    /// <summary>Downloads <c>{language}.traineddata</c> from tessdata_fast into the user data folder.</summary>
    public async Task DownloadLanguageAsync(string language, IProgress<double>? progress, CancellationToken cancelToken)
    {
        if (language.Length is 0 or > 32 || !language.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new ArgumentException("Invalid language code.", nameof(language));
        }
        Directory.CreateDirectory(UserDataDir);
        var target = Path.Combine(UserDataDir, language + ".traineddata");
        var temp = target + ".download";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Deckle/1.0");
        using (var response = await http.GetAsync(DownloadBaseUrl + language + ".traineddata", HttpCompletionOption.ResponseHeadersRead, cancelToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
            await using var file = File.Create(temp);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, cancelToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, n), cancelToken).ConfigureAwait(false);
                read += n;
                if (total > 0)
                {
                    progress?.Report((double)read / total.Value);
                }
            }
        }
        File.Move(temp, target, overwrite: true);
        Refresh();
    }

    private TesseractInstall? Search()
    {
        var overrideExe = Environment.GetEnvironmentVariable("DECKLE_TESSERACT");
        var systemExe = !string.IsNullOrEmpty(overrideExe) && File.Exists(overrideExe) ? overrideExe : FindSystemExe();
        var bundledExe = FindBundledExe();
        var exe = systemExe ?? bundledExe;
        if (exe is null)
        {
            return null;
        }
        // Downloaded languages first, then system data, then the English/OSD data shipped with the app.
        var dirs = new List<string> { UserDataDir };
        dirs.AddRange(SystemDataDirs());
        dirs.Add(BundledDataDir);
        dirs = dirs.Distinct().ToList();
        return new TesseractInstall(exe, dirs, IsBundled: systemExe is null);
    }

    private static string? FindSystemExe()
    {
        var name = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var extra = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tesseract-OCR"),
            }
            : ["/usr/local/bin", "/usr/bin", "/opt/homebrew/bin", "/opt/local/bin"];
        return pathDirs.Concat(extra).Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
    }

    private static string? FindBundledExe()
    {
        var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        string? relative = null;
        if (OperatingSystem.IsWindows())
        {
            relative = arm ? "_winarm/tesseract.exe" : Environment.Is64BitProcess ? "_win64/tesseract.exe" : "_win32/tesseract.exe";
        }
        else if (OperatingSystem.IsMacOS())
        {
            relative = arm ? "_macarm/tesseract" : "_mac/tesseract";
        }
        else if (OperatingSystem.IsLinux())
        {
            relative = arm ? "_linuxarm/tesseract" : "_linux/tesseract";
        }
        if (relative is null)
        {
            return null;
        }
        var path = Path.Combine(AppContext.BaseDirectory, relative);
        if (!File.Exists(path))
        {
            return null;
        }
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                // NuGet content files lose the executable bit.
                File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return path;
    }

    private static IEnumerable<string> SystemDataDirs()
    {
        var prefix = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
        if (!string.IsNullOrEmpty(prefix))
        {
            yield return prefix;
            yield return Path.Combine(prefix, "tessdata");
        }
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tessdata");
            yield break;
        }
        string[] roots = ["/usr/share/tesseract/tessdata", "/usr/share/tessdata", "/usr/local/share/tessdata",
            "/opt/homebrew/share/tessdata", "/opt/local/share/tessdata"];
        foreach (var root in roots)
        {
            yield return root;
        }
        // Debian/Ubuntu: /usr/share/tesseract-ocr/<version>/tessdata
        const string debian = "/usr/share/tesseract-ocr";
        if (Directory.Exists(debian))
        {
            foreach (var dir in Directory.EnumerateDirectories(debian).OrderDescending())
            {
                yield return Path.Combine(dir, "tessdata");
            }
        }
    }
}
