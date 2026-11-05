using Deckle.Core.Pages;
using Deckle.Core.Settings;

namespace Deckle.Core.Export;

/// <summary>Where a save ended up.</summary>
public sealed record SaveResult(IReadOnlyList<string> Paths, OutputFormat Format)
{
    public string? Folder => Paths.Count > 0 ? Path.GetDirectoryName(Paths[0]) : null;
}

/// <summary>
/// Decides where scans are written, following Scanner's model:
/// <list type="bullet">
/// <item>Save location is a fixed folder, or the user is asked for every new file.</item>
/// <item>PDF/TIFF: with "add to existing" new pages extend the current file; otherwise each scan starts a new file.</item>
/// <item>JPG/PNG/BMP: each new page becomes its own file.</item>
/// </list>
/// </summary>
public sealed class SaveCoordinator
{
    private readonly ExportService _export;

    public SaveCoordinator(ExportService export)
    {
        _export = export;
    }

    /// <summary>The multi-page file the current document is being written to, if any.</summary>
    public string? CurrentDocumentPath { get; private set; }
    public OutputFormat? CurrentDocumentFormat { get; private set; }

    /// <summary>Document version at the last save; compare with <see cref="PageDocument.Version"/>.</summary>
    public int SavedVersion { get; private set; } = -1;

    /// <summary>Asks the user for a path. Arguments: suggested file name, format. Null = cancelled.</summary>
    public Func<string, OutputFormat, Task<string?>>? AskForPath { get; set; }

    public bool HasUnsavedChanges(PageDocument document) => document.Count > 0 && document.Version != SavedVersion;

    /// <summary>Forget the current output file (after "Start fresh" or clearing the list).</summary>
    public void StartNewDocument()
    {
        CurrentDocumentPath = null;
        CurrentDocumentFormat = null;
        SavedVersion = -1;
    }

    /// <summary>Saves after a scan. Returns null when the user cancelled the save dialog.</summary>
    public async Task<SaveResult?> SaveScanAsync(
        PageDocument document,
        IReadOnlyList<Page> newPages,
        OutputFormat format,
        AppSettings settings,
        NamingContext naming,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancelToken = default)
    {
        if (newPages.Count == 0)
        {
            return new SaveResult([], format);
        }

        if (format.IsMultiPage())
        {
            var appending = settings.ScanAction == ScanAction.AddToExisting
                && CurrentDocumentFormat == format
                && CurrentDocumentPath is not null
                && File.Exists(CurrentDocumentPath);
            var path = appending ? CurrentDocumentPath : await ChoosePathAsync(settings, naming, format).ConfigureAwait(false);
            if (path is null)
            {
                return null;
            }
            // "Add to existing" rewrites the whole document; "start fresh" holds only this scan's pages anyway.
            var pages = settings.ScanAction == ScanAction.AddToExisting ? document.Pages : newPages;
            await _export.ExportDocumentAsync(pages.Select(p => p.Image).ToList(), path!, format, settings, progress, cancelToken).ConfigureAwait(false);
            CurrentDocumentPath = path;
            CurrentDocumentFormat = format;
            SavedVersion = document.Version;
            return new SaveResult([path!], format);
        }

        var paths = await ChoosePagePathsAsync(settings, naming, format, newPages.Count).ConfigureAwait(false);
        if (paths is null)
        {
            return null;
        }
        await _export.ExportPagesAsync(newPages.Select(p => p.Image).ToList(), paths, format, settings, progress, cancelToken).ConfigureAwait(false);
        SavedVersion = document.Version;
        return new SaveResult(paths, format);
    }

    /// <summary>
    /// Re-saves the whole document to its current file (after edits). Without a current file this behaves like
    /// <see cref="SaveAsAsync"/> with a generated name.
    /// </summary>
    public async Task<SaveResult?> SaveAsync(
        PageDocument document,
        OutputFormat format,
        AppSettings settings,
        NamingContext naming,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancelToken = default)
    {
        if (document.Count == 0)
        {
            return new SaveResult([], format);
        }
        if (format.IsMultiPage() && CurrentDocumentFormat == format && CurrentDocumentPath is not null)
        {
            await _export.ExportDocumentAsync(document.Pages.Select(p => p.Image).ToList(), CurrentDocumentPath, format, settings, progress, cancelToken).ConfigureAwait(false);
            SavedVersion = document.Version;
            return new SaveResult([CurrentDocumentPath], format);
        }
        var path = await ChoosePathAsync(settings, naming, format).ConfigureAwait(false);
        return path is null ? null : await SaveAsAsync(document, document.Pages, path, format, settings, progress, cancelToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes <paramref name="pages"/> to an explicit path. For image formats with several pages the path is used as
    /// the base name and each page gets a "(page NN)" suffix.
    /// </summary>
    public async Task<SaveResult> SaveAsAsync(
        PageDocument document,
        IReadOnlyList<Page> pages,
        string path,
        OutputFormat format,
        AppSettings settings,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancelToken = default)
    {
        var images = pages.Select(p => p.Image).ToList();
        if (format.IsMultiPage())
        {
            await _export.ExportDocumentAsync(images, path, format, settings, progress, cancelToken).ConfigureAwait(false);
            if (pages.Count == document.Count)
            {
                CurrentDocumentPath = path;
                CurrentDocumentFormat = format;
                SavedVersion = document.Version;
            }
            return new SaveResult([path], format);
        }

        IReadOnlyList<string> paths;
        if (images.Count == 1)
        {
            paths = [path];
        }
        else
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var baseName = Path.GetFileNameWithoutExtension(path);
            var digits = Math.Max(2, images.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
            paths = Enumerable.Range(1, images.Count)
                .Select(i => Path.Combine(folder, $"{baseName} (page {i.ToString(new string('0', digits), System.Globalization.CultureInfo.InvariantCulture)}){format.Extension()}"))
                .ToList();
        }
        await _export.ExportPagesAsync(images, paths, format, settings, progress, cancelToken).ConfigureAwait(false);
        if (pages.Count == document.Count)
        {
            SavedVersion = document.Version;
        }
        return new SaveResult(paths, format);
    }

    private async Task<string?> ChoosePathAsync(AppSettings settings, NamingContext naming, OutputFormat format)
    {
        if (settings.SaveLocation == SaveLocationMode.AskEveryTime && AskForPath is not null)
        {
            var suggested = FileNamer.Render(settings.FileNamePattern, naming with { Format = format }) + format.Extension();
            return await AskForPath(suggested, format).ConfigureAwait(false);
        }
        return FileNamer.ResolvePath(EnsureFolder(settings), settings.FileNamePattern, naming with { Format = format });
    }

    private async Task<IReadOnlyList<string>?> ChoosePagePathsAsync(AppSettings settings, NamingContext naming, OutputFormat format, int count)
    {
        if (settings.SaveLocation == SaveLocationMode.AskEveryTime && AskForPath is not null)
        {
            var suggested = FileNamer.Render(settings.FileNamePattern, naming with { Format = format }) + format.Extension();
            var chosen = await AskForPath(suggested, format).ConfigureAwait(false);
            if (chosen is null)
            {
                return null;
            }
            var folder = Path.GetDirectoryName(Path.GetFullPath(chosen))!;
            var pattern = Path.GetFileNameWithoutExtension(chosen).Replace("{", "(").Replace("}", ")");
            return FileNamer.ResolvePaths(folder, pattern, naming with { Format = format }, count);
        }
        return FileNamer.ResolvePaths(EnsureFolder(settings), settings.FileNamePattern, naming with { Format = format }, count);
    }

    private static string EnsureFolder(AppSettings settings)
    {
        var folder = string.IsNullOrWhiteSpace(settings.SaveFolder) ? AppPaths.DefaultScanFolder() : settings.SaveFolder;
        Directory.CreateDirectory(folder);
        return folder;
    }
}
