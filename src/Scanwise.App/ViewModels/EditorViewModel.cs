using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAPS2.Images;
using NAPS2.Images.Transforms;
using Scanwise.App.Services;
using Scanwise.Core.Pages;

namespace Scanwise.App.ViewModels;

public enum EditorTool { None, Crop, Adjust }

/// <summary>
/// Single-page editor: Scanner's crop (with aspect-ratio presets) and rotate, plus NAPS2's image adjustments.
/// Crop coordinates are fractions (0..1) of the displayed page.
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    public const int PreviewSize = 2000;
    private const int CropBasis = 10_000;

    private readonly AppServices _services;
    private readonly PageListViewModel _pages;
    private CancellationTokenSource? _renderCts;
    private Timer? _adjustTimer;

    public EditorViewModel(AppServices services, PageListViewModel pages)
    {
        _services = services;
        _pages = pages;
        pages.SelectionChanged += (_, _) => Show(pages.SelectedItem);
        services.Document.Changed += (_, _) => OnDocumentChanged();
    }

    public IReadOnlyList<Choice<double?>> AspectRatios { get; } =
    [
        new(null, "Free"),
        new(1.0, "Square (1:1)"),
        new(3.0 / 2, "3:2"),
        new(4.0 / 3, "4:3"),
        new(1 / Math.Sqrt(2), "DIN A (A4, A5, …)"),
        new(8.5 / 11, "ANSI A (Letter)"),
        new(11.0 / 17, "ANSI B (Tabloid)"),
        new(17.0 / 22, "ANSI C"),
        new(8.5 / 14, "Legal"),
        new(389.0 / 543, "Kai 4"),
        new(273.0 / 394, "Kai 8"),
        new(195.0 / 271, "Kai 16"),
        new(135.0 / 195, "Kai 32"),
    ];

    [ObservableProperty] private PageItemViewModel? _current;
    [ObservableProperty] private Bitmap? _preview;
    [ObservableProperty] private PixelSize _previewPixels;
    [ObservableProperty] private bool _isRendering;

    // Zoom: 0 means "fit to window".
    [ObservableProperty] private double _zoom;
    [ObservableProperty] private Size _viewport;

    [ObservableProperty] private EditorTool _tool;

    // Crop rectangle, fractions of the page.
    [ObservableProperty] private double _cropLeft;
    [ObservableProperty] private double _cropTop;
    [ObservableProperty] private double _cropRight = 1;
    [ObservableProperty] private double _cropBottom = 1;
    [ObservableProperty] private Choice<double?>? _aspectRatio;
    [ObservableProperty] private bool _aspectLandscape;
    [ObservableProperty] private bool _cropSimilarPages;

    // Adjustments, -100..100 (NAPS2 uses ×10).
    [ObservableProperty] private int _brightness;
    [ObservableProperty] private int _contrast;
    [ObservableProperty] private int _hue;
    [ObservableProperty] private int _saturation;
    [ObservableProperty] private int _sharpen;
    [ObservableProperty] private bool _grayscale;
    [ObservableProperty] private bool _blackAndWhite;
    [ObservableProperty] private int _blackWhiteThreshold;
    [ObservableProperty] private bool _adjustSelectedPages;
    [ObservableProperty] private int _straighten;

    public bool HasPage => Current is not null;
    public bool IsCropping => Tool == EditorTool.Crop;
    public bool IsAdjusting => Tool == EditorTool.Adjust;
    public bool IsToolOpen => Tool != EditorTool.None;
    public string PageTitle => Current is null ? "No page selected" : $"Page {Current.Number} of {_pages.Count}";
    public bool CanGoPrevious => Current is not null && Current.Number > 1;
    public bool CanGoNext => Current is not null && Current.Number < _pages.Count;

    /// <summary>Effective zoom factor (resolves "fit").</summary>
    public double EffectiveZoom
    {
        get
        {
            if (Zoom > 0)
            {
                return Zoom;
            }
            if (PreviewPixels.Width == 0 || Viewport.Width <= 0)
            {
                return 1;
            }
            return Math.Min((Viewport.Width - 32) / PreviewPixels.Width, (Viewport.Height - 32) / PreviewPixels.Height);
        }
    }

    public double DisplayWidth => Math.Max(1, PreviewPixels.Width * EffectiveZoom);
    public double DisplayHeight => Math.Max(1, PreviewPixels.Height * EffectiveZoom);
    public string ZoomLabel => Zoom > 0 ? $"{Zoom * 100 * PreviewScale:0}%" : "Fit";

    /// <summary>Page pixels per preview pixel, so 100% zoom means actual size.</summary>
    private double PreviewScale { get; set; } = 1;

    /// <summary>Aspect ratio (width/height) the crop box is locked to, in preview pixel terms.</summary>
    public double? LockedAspect
    {
        get
        {
            if (AspectRatio?.Value is not { } ratio)
            {
                return null;
            }
            var r = AspectLandscape ? 1 / ratio : ratio;
            return r;
        }
    }

    public void Show(PageItemViewModel? item)
    {
        if (ReferenceEquals(item, Current))
        {
            return;
        }
        CancelTool();
        Current = item;
    }

    partial void OnCurrentChanged(PageItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasPage));
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        Zoom = 0;
        _ = RenderAsync();
    }

    private void OnDocumentChanged()
    {
        if (Current is null)
        {
            Show(_pages.Items.LastOrDefault());
            return;
        }
        if (!_pages.Items.Contains(Current))
        {
            Current = _pages.SelectedItem ?? _pages.Items.LastOrDefault();
            return;
        }
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        _ = RenderAsync();
    }

    private ProcessedImage? _renderedImage;

    private async Task RenderAsync(IReadOnlyList<Transform>? extra = null)
    {
        if (Current is null)
        {
            Preview = null;
            PreviewPixels = default;
            _renderedImage = null;
            return;
        }
        var image = Current.Page.Image;
        if (extra is null && ReferenceEquals(image, _renderedImage) && Preview is not null)
        {
            return;
        }
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        IsRendering = true;
        try
        {
            ProcessedImage source = image;
            var temp = new List<ProcessedImage>();
            if (extra is { Count: > 0 })
            {
                source = image.Clone();
                temp.Add(source);
                foreach (var t in extra)
                {
                    source = source.WithTransform(t, disposeSelf: false);
                    temp.Add(source);
                }
            }
            try
            {
                var bitmap = await _services.Renderer.RenderAsync(source, PreviewSize, cts.Token);
                if (extra is null)
                {
                    var full = await _services.Renderer.MeasureAsync(image);
                    PreviewScale = bitmap is null || bitmap.PixelSize.Width == 0 ? 1 : (double)full.Width / bitmap.PixelSize.Width;
                }
                if (bitmap is null || cts.IsCancellationRequested)
                {
                    bitmap?.Dispose();
                    return;
                }
                var old = Preview;
                Preview = bitmap;
                PreviewPixels = bitmap.PixelSize;
                if (extra is null)
                {
                    _renderedImage = image;
                }
                old?.Dispose();
            }
            finally
            {
                foreach (var t in temp)
                {
                    t.Dispose();
                }
            }
        }
        finally
        {
            if (ReferenceEquals(cts, _renderCts))
            {
                IsRendering = false;
            }
        }
    }

    partial void OnPreviewPixelsChanged(PixelSize value) => RaiseZoom();
    partial void OnViewportChanged(Size value) => RaiseZoom();
    partial void OnZoomChanged(double value) => RaiseZoom();

    private void RaiseZoom()
    {
        OnPropertyChanged(nameof(EffectiveZoom));
        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
        OnPropertyChanged(nameof(ZoomLabel));
    }

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(8, EffectiveZoom * 1.25);

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(0.05, EffectiveZoom / 1.25);

    [RelayCommand]
    private void ZoomFit() => Zoom = 0;

    [RelayCommand]
    private void ZoomActual() => Zoom = 1 / PreviewScale;

    [RelayCommand]
    private void Previous() => Navigate(-1);

    [RelayCommand]
    private void Next() => Navigate(+1);

    private void Navigate(int delta)
    {
        if (Current is null)
        {
            return;
        }
        var index = Math.Clamp(_pages.Items.IndexOf(Current) + delta, 0, _pages.Items.Count - 1);
        _pages.Select([_pages.Items[index].Id]);
    }

    [RelayCommand]
    private void RotateLeft() => RotateCurrent(-90);

    [RelayCommand]
    private void RotateRight() => RotateCurrent(90);

    [RelayCommand]
    private void Rotate180() => RotateCurrent(180);

    private void RotateCurrent(double degrees)
    {
        if (Current is not null)
        {
            CancelTool();
            _services.Document.Rotate([Current.Id], degrees);
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Current is not null)
        {
            CancelTool();
            _services.Document.Remove([Current.Id]);
        }
    }

    // ---- Crop ----

    [RelayCommand]
    private void StartCrop()
    {
        if (Current is null)
        {
            return;
        }
        CancelTool();
        CropLeft = 0;
        CropTop = 0;
        CropRight = 1;
        CropBottom = 1;
        AspectRatio = AspectRatios[0];
        Tool = EditorTool.Crop;
    }

    [RelayCommand]
    private void FlipAspect() => AspectLandscape = !AspectLandscape;

    partial void OnAspectRatioChanged(Choice<double?>? value) => FitCropToAspect();
    partial void OnAspectLandscapeChanged(bool value) => FitCropToAspect();

    /// <summary>Shrinks the crop box around its center to match the chosen aspect ratio.</summary>
    private void FitCropToAspect()
    {
        OnPropertyChanged(nameof(LockedAspect));
        if (LockedAspect is not { } aspect || PreviewPixels.Width == 0)
        {
            return;
        }
        var w = (CropRight - CropLeft) * PreviewPixels.Width;
        var h = (CropBottom - CropTop) * PreviewPixels.Height;
        if (w / h > aspect)
        {
            w = h * aspect;
        }
        else
        {
            h = w / aspect;
        }
        var cx = (CropLeft + CropRight) / 2 * PreviewPixels.Width;
        var cy = (CropTop + CropBottom) / 2 * PreviewPixels.Height;
        CropLeft = Math.Max(0, (cx - w / 2) / PreviewPixels.Width);
        CropRight = Math.Min(1, (cx + w / 2) / PreviewPixels.Width);
        CropTop = Math.Max(0, (cy - h / 2) / PreviewPixels.Height);
        CropBottom = Math.Min(1, (cy + h / 2) / PreviewPixels.Height);
    }

    [RelayCommand]
    private async Task ApplyCrop()
    {
        if (Current is null || Tool != EditorTool.Crop)
        {
            return;
        }
        var crop = new CropTransform(
            (int)Math.Round(CropLeft * CropBasis),
            (int)Math.Round((1 - CropRight) * CropBasis),
            (int)Math.Round(CropTop * CropBasis),
            (int)Math.Round((1 - CropBottom) * CropBasis),
            CropBasis,
            CropBasis);
        IReadOnlyList<Guid> ids = [Current.Id];
        if (CropSimilarPages)
        {
            // Scanner's "crop similar pages": every page with the same size as this one.
            var size = await _services.Renderer.MeasureAsync(Current.Page.Image);
            var similar = new List<Guid>();
            foreach (var item in _pages.Items)
            {
                var other = await _services.Renderer.MeasureAsync(item.Page.Image);
                if (Math.Abs(other.Width - size.Width) <= 2 && Math.Abs(other.Height - size.Height) <= 2)
                {
                    similar.Add(item.Id);
                }
            }
            ids = similar;
        }
        Tool = EditorTool.None;
        _services.Document.ApplyTransform(ids, crop);
    }

    // ---- Adjust ----

    [RelayCommand]
    private void StartAdjust()
    {
        if (Current is null)
        {
            return;
        }
        CancelTool();
        ResetAdjustValues();
        Tool = EditorTool.Adjust;
    }

    private void ResetAdjustValues()
    {
        Brightness = Contrast = Hue = Saturation = Sharpen = BlackWhiteThreshold = Straighten = 0;
        Grayscale = BlackAndWhite = false;
    }

    [RelayCommand]
    private void ResetAdjust()
    {
        ResetAdjustValues();
        _ = RenderAsync();
    }

    private List<Transform> PendingAdjustments()
    {
        var list = new List<Transform>();
        if (Straighten != 0)
        {
            list.Add(new RotationTransform(Straighten / 2.0));
        }
        if (Brightness != 0)
        {
            list.Add(new BrightnessTransform(Brightness * 10));
        }
        if (Contrast != 0)
        {
            list.Add(new TrueContrastTransform(Contrast * 10));
        }
        if (Hue != 0)
        {
            list.Add(new HueTransform(Hue * 10));
        }
        if (Saturation != 0)
        {
            list.Add(new SaturationTransform(Saturation * 10));
        }
        if (Sharpen != 0)
        {
            list.Add(new SharpenTransform(Sharpen * 10));
        }
        if (BlackAndWhite)
        {
            list.Add(new BlackWhiteTransform(BlackWhiteThreshold * 10));
        }
        else if (Grayscale)
        {
            list.Add(new GrayscaleTransform());
        }
        return list;
    }

    partial void OnBrightnessChanged(int value) => SchedulePreview();
    partial void OnContrastChanged(int value) => SchedulePreview();
    partial void OnHueChanged(int value) => SchedulePreview();
    partial void OnSaturationChanged(int value) => SchedulePreview();
    partial void OnSharpenChanged(int value) => SchedulePreview();
    partial void OnGrayscaleChanged(bool value) => SchedulePreview();
    partial void OnBlackAndWhiteChanged(bool value) => SchedulePreview();
    partial void OnBlackWhiteThresholdChanged(int value) => SchedulePreview();
    partial void OnStraightenChanged(int value) => SchedulePreview();

    private void SchedulePreview()
    {
        if (Tool != EditorTool.Adjust)
        {
            return;
        }
        _adjustTimer?.Dispose();
        _adjustTimer = new Timer(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Tool == EditorTool.Adjust)
            {
                _ = RenderAsync(PendingAdjustments());
            }
        }), null, 150, Timeout.Infinite);
    }

    [RelayCommand]
    private void ApplyAdjust()
    {
        if (Current is null || Tool != EditorTool.Adjust)
        {
            return;
        }
        var transforms = PendingAdjustments();
        Tool = EditorTool.None;
        if (transforms.Count == 0)
        {
            return;
        }
        var ids = AdjustSelectedPages && _pages.SelectedItems.Count > 1 ? _pages.SelectedIds : [Current.Id];
        _services.Document.Update(ids, page =>
        {
            var image = page.Image;
            var created = new List<ProcessedImage>();
            foreach (var t in transforms)
            {
                image = image.WithTransform(t, disposeSelf: false);
                created.Add(image);
            }
            // Only the last image is kept; intermediate ones are released.
            foreach (var intermediate in created.Take(created.Count - 1))
            {
                intermediate.Dispose();
            }
            return image;
        });
    }

    [RelayCommand]
    private void CancelTool()
    {
        var wasAdjusting = Tool == EditorTool.Adjust;
        Tool = EditorTool.None;
        if (wasAdjusting)
        {
            _renderedImage = null;
            _ = RenderAsync();
        }
    }

    partial void OnToolChanged(EditorTool value)
    {
        OnPropertyChanged(nameof(IsCropping));
        OnPropertyChanged(nameof(IsAdjusting));
        OnPropertyChanged(nameof(IsToolOpen));
    }
}
