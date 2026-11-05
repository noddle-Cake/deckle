using System.Collections.Immutable;
using System.Text.Json.Serialization;
using NAPS2.Images;
using NAPS2.Images.Transforms;
using NAPS2.ImportExport;
using NAPS2.Scan;
using Deckle.Core.Settings;

namespace Deckle.Core.Pages;

/// <summary>
/// Keeps a copy of the page list on disk so pages survive a crash or a closed window (like NAPS2's recovery).
/// Each page's untransformed image is written once; edits only rewrite the small JSON index.
/// </summary>
public sealed class SessionStore
{
    private readonly string _dir;
    private readonly ScanningContext _scanning;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SessionStore(string dir, ScanningContext scanning)
    {
        _dir = dir;
        _scanning = scanning;
    }

    private string IndexFile => Path.Combine(_dir, "session.json");

    public bool HasSavedSession => File.Exists(IndexFile) && JsonStore.Load(IndexFile, () => new SessionIndex()).Pages.Count > 0;

    public int SavedPageCount => File.Exists(IndexFile) ? JsonStore.Load(IndexFile, () => new SessionIndex()).Pages.Count : 0;

    /// <summary>Writes the current pages. Safe to call often; runs the image encoding off the calling thread.</summary>
    public async Task SaveAsync(IReadOnlyList<Page> pages, CancellationToken cancelToken = default)
    {
        // Hold references so the images are not disposed while we encode them.
        var snapshot = pages.Select(p => (Page: p, Image: p.Image.Clone())).ToList();
        await _lock.WaitAsync(cancelToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(_dir);
                var index = new SessionIndex();
                foreach (var (page, image) in snapshot)
                {
                    var file = EnsureBaseImage(page.Id, image);
                    index.Pages.Add(new SessionPage
                    {
                        Id = page.Id,
                        File = Path.GetFileName(file),
                        Info = page.Info,
                        Transforms = image.TransformState.Transforms.Select(TransformDto.From).OfType<TransformDto>().ToList(),
                    });
                }
                JsonStore.Save(IndexFile, index);
                DeleteOrphans(index);
            }, cancelToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
            foreach (var (_, image) in snapshot)
            {
                image.Dispose();
            }
        }
    }

    public async Task<List<Page>> RestoreAsync(CancellationToken cancelToken = default)
    {
        var index = JsonStore.Load(IndexFile, () => new SessionIndex());
        var importer = new ImageImporter(_scanning);
        var pages = new List<Page>();
        foreach (var entry in index.Pages)
        {
            var path = Path.Combine(_dir, entry.File);
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                await foreach (var image in importer.Import(path, new ImportParams(), cancelToken).ConfigureAwait(false))
                {
                    var transforms = entry.Transforms.Select(t => t.ToTransform()).OfType<Transform>().ToImmutableList();
                    var restored = image.WithTransformState(new TransformState(transforms), disposeSelf: true);
                    pages.Add(new Page(entry.Id, restored, entry.Info ?? new PageInfo(DateTime.Now)));
                    break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A damaged page file should not block the rest.
            }
        }
        return pages;
    }

    public void Clear()
    {
        if (!Directory.Exists(_dir))
        {
            return;
        }
        foreach (var file in Directory.EnumerateFiles(_dir))
        {
            TryDelete(file);
        }
    }

    private string EnsureBaseImage(Guid id, ProcessedImage image)
    {
        var existing = Directory.EnumerateFiles(_dir, id.ToString("N") + ".*").FirstOrDefault(f => !f.EndsWith(".tmp", StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }
        // Keep lossy scans lossy (much smaller); everything else as PNG.
        var lossy = !image.Metadata.Lossless;
        var path = Path.Combine(_dir, id.ToString("N") + (lossy ? ".jpg" : ".png"));
        var temp = path + ".tmp";
        using (var raw = _scanning.ImageContext.RenderWithoutTransforms(image))
        {
            raw.Save(temp, lossy ? ImageFileFormat.Jpeg : ImageFileFormat.Png, new ImageSaveOptions { Quality = 95 });
        }
        File.Move(temp, path, overwrite: true);
        return path;
    }

    private void DeleteOrphans(SessionIndex index)
    {
        var keep = index.Pages.Select(p => p.File).ToHashSet(StringComparer.Ordinal);
        keep.Add(Path.GetFileName(IndexFile));
        foreach (var file in Directory.EnumerateFiles(_dir))
        {
            if (!keep.Contains(Path.GetFileName(file)))
            {
                TryDelete(file);
            }
        }
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

    internal sealed class SessionIndex
    {
        public List<SessionPage> Pages { get; set; } = [];
    }

    internal sealed class SessionPage
    {
        public Guid Id { get; set; }
        public string File { get; set; } = "";
        public PageInfo? Info { get; set; }
        public List<TransformDto> Transforms { get; set; } = [];
    }
}

/// <summary>Serializable form of the NAPS2 transforms the app can produce.</summary>
internal sealed class TransformDto
{
    public string Type { get; set; } = "";
    public double Value { get; set; }
    public int Left { get; set; }
    public int Right { get; set; }
    public int Top { get; set; }
    public int Bottom { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Width { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Height { get; set; }

    public static TransformDto? From(Transform transform) => transform switch
    {
        RotationTransform t => new() { Type = "rotate", Value = t.Angle },
        CropTransform t => new() { Type = "crop", Left = t.Left, Right = t.Right, Top = t.Top, Bottom = t.Bottom, Width = t.OriginalWidth, Height = t.OriginalHeight },
        BrightnessTransform t => new() { Type = "brightness", Value = t.Brightness },
        TrueContrastTransform t => new() { Type = "contrast", Value = t.Contrast },
        HueTransform t => new() { Type = "hue", Value = t.HueShift },
        SaturationTransform t => new() { Type = "saturation", Value = t.Saturation },
        SharpenTransform t => new() { Type = "sharpen", Value = t.Sharpness },
        GrayscaleTransform => new() { Type = "grayscale" },
        BlackWhiteTransform t => new() { Type = "blackwhite", Value = t.Threshold },
        ScaleTransform t => new() { Type = "scale", Value = t.ScaleFactor },
        _ => null,
    };

    public Transform? ToTransform() => Type switch
    {
        "rotate" => new RotationTransform(Value),
        "crop" => new CropTransform(Left, Right, Top, Bottom, Width, Height),
        "brightness" => new BrightnessTransform((int)Value),
        "contrast" => new TrueContrastTransform((int)Value),
        "hue" => new HueTransform((int)Value),
        "saturation" => new SaturationTransform((int)Value),
        "sharpen" => new SharpenTransform((int)Value),
        "grayscale" => new GrayscaleTransform(),
        "blackwhite" => new BlackWhiteTransform((int)Value),
        "scale" => new ScaleTransform(Value),
        _ => null,
    };
}
