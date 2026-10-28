using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Scanwise.App.Controls;

/// <summary>
/// A crop rectangle with eight handles over an image. Edges are fractions (0..1) of the control's size, so the
/// overlay can sit in the same scaled container as the image. Optionally locked to an aspect ratio (width/height
/// in the control's own units).
/// </summary>
public sealed class CropOverlay : Control
{
    public static readonly StyledProperty<double> LeftEdgeProperty =
        AvaloniaProperty.Register<CropOverlay, double>(nameof(LeftEdge), 0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> TopEdgeProperty =
        AvaloniaProperty.Register<CropOverlay, double>(nameof(TopEdge), 0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> RightEdgeProperty =
        AvaloniaProperty.Register<CropOverlay, double>(nameof(RightEdge), 1, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> BottomEdgeProperty =
        AvaloniaProperty.Register<CropOverlay, double>(nameof(BottomEdge), 1, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double?> AspectRatioProperty =
        AvaloniaProperty.Register<CropOverlay, double?>(nameof(AspectRatio));
    /// <summary>The scale the overlay is displayed at, so handles keep a constant on-screen size.</summary>
    public static readonly StyledProperty<double> DisplayScaleProperty =
        AvaloniaProperty.Register<CropOverlay, double>(nameof(DisplayScale), 1);

    private const double HandleSize = 12;
    private const double MinSize = 0.02;

    private Handle _drag = Handle.None;
    private Point _dragStart;
    private Rect _startRect;

    static CropOverlay()
    {
        AffectsRender<CropOverlay>(LeftEdgeProperty, TopEdgeProperty, RightEdgeProperty, BottomEdgeProperty, DisplayScaleProperty);
        FocusableProperty.OverrideDefaultValue<CropOverlay>(true);
    }

    public CropOverlay()
    {
        ClipToBounds = false;
    }

    public double LeftEdge { get => GetValue(LeftEdgeProperty); set => SetValue(LeftEdgeProperty, value); }
    public double TopEdge { get => GetValue(TopEdgeProperty); set => SetValue(TopEdgeProperty, value); }
    public double RightEdge { get => GetValue(RightEdgeProperty); set => SetValue(RightEdgeProperty, value); }
    public double BottomEdge { get => GetValue(BottomEdgeProperty); set => SetValue(BottomEdgeProperty, value); }
    public double? AspectRatio { get => GetValue(AspectRatioProperty); set => SetValue(AspectRatioProperty, value); }
    public double DisplayScale { get => GetValue(DisplayScaleProperty); set => SetValue(DisplayScaleProperty, value); }

    [Flags]
    private enum Handle { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, Move = 16 }

    private Rect CropRect => new(
        LeftEdge * Bounds.Width, TopEdge * Bounds.Height,
        Math.Max(0, (RightEdge - LeftEdge) * Bounds.Width), Math.Max(0, (BottomEdge - TopEdge) * Bounds.Height));

    private double Scaled(double screenPixels) => screenPixels / Math.Max(0.01, DisplayScale);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var crop = CropRect;
        var shade = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));

        // Dim everything outside the crop.
        context.DrawRectangle(shade, null, new Rect(0, 0, bounds.Width, crop.Top));
        context.DrawRectangle(shade, null, new Rect(0, crop.Bottom, bounds.Width, bounds.Height - crop.Bottom));
        context.DrawRectangle(shade, null, new Rect(0, crop.Top, crop.Left, crop.Height));
        context.DrawRectangle(shade, null, new Rect(crop.Right, crop.Top, bounds.Width - crop.Right, crop.Height));

        var thin = Scaled(1);
        var accent = Application.Current?.TryFindResource("SystemAccentColor", out var color) == true && color is Color c ? c : Colors.DodgerBlue;
        context.DrawRectangle(null, new Pen(Brushes.White, Scaled(2)), crop);
        context.DrawRectangle(null, new Pen(new SolidColorBrush(accent), thin), crop.Deflate(Scaled(1)));

        // Rule-of-thirds guides.
        var guide = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), thin);
        for (var i = 1; i < 3; i++)
        {
            var x = crop.Left + crop.Width * i / 3;
            var y = crop.Top + crop.Height * i / 3;
            context.DrawLine(guide, new Point(x, crop.Top), new Point(x, crop.Bottom));
            context.DrawLine(guide, new Point(crop.Left, y), new Point(crop.Right, y));
        }

        var size = Scaled(HandleSize);
        foreach (var point in HandlePoints(crop))
        {
            var r = new Rect(point.X - size / 2, point.Y - size / 2, size, size);
            context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(accent), thin), r, size / 4, size / 4);
        }
    }

    private static IEnumerable<Point> HandlePoints(Rect r) =>
    [
        r.TopLeft, r.TopRight, r.BottomLeft, r.BottomRight,
        new(r.Center.X, r.Top), new(r.Center.X, r.Bottom), new(r.Left, r.Center.Y), new(r.Right, r.Center.Y),
    ];

    private Handle HitTest(Point p)
    {
        var r = CropRect;
        var tolerance = Scaled(HandleSize);
        var nearLeft = Math.Abs(p.X - r.Left) <= tolerance;
        var nearRight = Math.Abs(p.X - r.Right) <= tolerance;
        var nearTop = Math.Abs(p.Y - r.Top) <= tolerance;
        var nearBottom = Math.Abs(p.Y - r.Bottom) <= tolerance;
        var inX = p.X >= r.Left - tolerance && p.X <= r.Right + tolerance;
        var inY = p.Y >= r.Top - tolerance && p.Y <= r.Bottom + tolerance;

        var handle = Handle.None;
        if (nearLeft && inY) handle |= Handle.Left;
        else if (nearRight && inY) handle |= Handle.Right;
        if (nearTop && inX) handle |= Handle.Top;
        else if (nearBottom && inX) handle |= Handle.Bottom;
        if (handle == Handle.None && r.Contains(p))
        {
            handle = Handle.Move;
        }
        return handle;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag == Handle.None)
        {
            Cursor = HitTest(p) switch
            {
                Handle.Move => new Cursor(StandardCursorType.SizeAll),
                Handle.Left or Handle.Right => new Cursor(StandardCursorType.SizeWestEast),
                Handle.Top or Handle.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
                Handle.Left | Handle.Top or Handle.Right | Handle.Bottom => new Cursor(StandardCursorType.TopLeftCorner),
                Handle.Right | Handle.Top or Handle.Left | Handle.Bottom => new Cursor(StandardCursorType.TopRightCorner),
                _ => new Cursor(StandardCursorType.Cross),
            };
            return;
        }
        Drag(p);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Bounds.Width <= 0)
        {
            return;
        }
        var p = e.GetPosition(this);
        _drag = HitTest(p);
        if (_drag == Handle.None)
        {
            // Start a new rectangle from the click point.
            _drag = Handle.Right | Handle.Bottom;
            LeftEdge = RightEdge = Math.Clamp(p.X / Bounds.Width, 0, 1);
            TopEdge = BottomEdge = Math.Clamp(p.Y / Bounds.Height, 0, 1);
        }
        _dragStart = p;
        _startRect = CropRect;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag != Handle.None)
        {
            _drag = Handle.None;
            e.Pointer.Capture(null);
            if (RightEdge - LeftEdge < MinSize || BottomEdge - TopEdge < MinSize)
            {
                // A click without a drag: reset to the whole page.
                LeftEdge = TopEdge = 0;
                RightEdge = BottomEdge = 1;
                ApplyAspect(Handle.Right | Handle.Bottom);
            }
            e.Handled = true;
        }
    }

    private void Drag(Point p)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var dx = p.X - _dragStart.X;
        var dy = p.Y - _dragStart.Y;
        var r = _startRect;
        double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;

        if (_drag == Handle.Move)
        {
            dx = Math.Clamp(dx, -r.Left, w - r.Right);
            dy = Math.Clamp(dy, -r.Top, h - r.Bottom);
            left += dx;
            right += dx;
            top += dy;
            bottom += dy;
        }
        else
        {
            if (_drag.HasFlag(Handle.Left)) left = Math.Clamp(r.Left + dx, 0, r.Right - MinSize * w);
            if (_drag.HasFlag(Handle.Right)) right = Math.Clamp(r.Right + dx, r.Left + MinSize * w, w);
            if (_drag.HasFlag(Handle.Top)) top = Math.Clamp(r.Top + dy, 0, r.Bottom - MinSize * h);
            if (_drag.HasFlag(Handle.Bottom)) bottom = Math.Clamp(r.Bottom + dy, r.Top + MinSize * h, h);
        }

        LeftEdge = left / w;
        RightEdge = right / w;
        TopEdge = top / h;
        BottomEdge = bottom / h;
        if (_drag != Handle.Move)
        {
            ApplyAspect(_drag);
        }
    }

    /// <summary>Adjusts the edge(s) being dragged so the rectangle keeps the locked aspect ratio.</summary>
    private void ApplyAspect(Handle dragged)
    {
        if (AspectRatio is not { } aspect || aspect <= 0 || Bounds.Width <= 0)
        {
            return;
        }
        var w = Bounds.Width;
        var h = Bounds.Height;
        var left = LeftEdge * w;
        var right = RightEdge * w;
        var top = TopEdge * h;
        var bottom = BottomEdge * h;
        var width = right - left;
        var height = bottom - top;

        if (dragged is Handle.Top or Handle.Bottom)
        {
            width = height * aspect;
        }
        else
        {
            height = width / aspect;
        }
        // Keep within bounds, shrinking both dimensions if needed.
        var maxWidth = dragged.HasFlag(Handle.Left) ? right : w - left;
        var maxHeight = dragged.HasFlag(Handle.Top) ? bottom : h - top;
        var scale = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
        width *= scale;
        height *= scale;

        if (dragged.HasFlag(Handle.Left)) left = right - width; else right = left + width;
        if (dragged.HasFlag(Handle.Top)) top = bottom - height; else bottom = top + height;

        LeftEdge = left / w;
        RightEdge = right / w;
        TopEdge = top / h;
        BottomEdge = bottom / h;
    }
}
