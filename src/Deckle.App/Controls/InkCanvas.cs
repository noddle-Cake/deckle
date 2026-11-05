using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Deckle.Core.Processing;

namespace Deckle.App.Controls;

/// <summary>
/// Captures pen and highlighter strokes over a page. Points are stored as fractions of the control size so they map
/// directly onto the page image (see <see cref="Annotator"/>).
/// </summary>
public sealed class InkCanvas : Control
{
    public static readonly StyledProperty<ObservableCollection<InkStroke>?> StrokesProperty =
        AvaloniaProperty.Register<InkCanvas, ObservableCollection<InkStroke>?>(nameof(Strokes));
    public static readonly StyledProperty<Color> InkColorProperty =
        AvaloniaProperty.Register<InkCanvas, Color>(nameof(InkColor), Colors.Red);
    /// <summary>Stroke width as a fraction of the page width.</summary>
    public static readonly StyledProperty<double> InkWidthProperty =
        AvaloniaProperty.Register<InkCanvas, double>(nameof(InkWidth), 0.004);
    public static readonly StyledProperty<bool> IsHighlighterProperty =
        AvaloniaProperty.Register<InkCanvas, bool>(nameof(IsHighlighter));

    private List<(double X, double Y)>? _current;

    static InkCanvas()
    {
        AffectsRender<InkCanvas>(StrokesProperty);
    }

    public InkCanvas()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public ObservableCollection<InkStroke>? Strokes { get => GetValue(StrokesProperty); set => SetValue(StrokesProperty, value); }
    public Color InkColor { get => GetValue(InkColorProperty); set => SetValue(InkColorProperty, value); }
    public double InkWidth { get => GetValue(InkWidthProperty); set => SetValue(InkWidthProperty, value); }
    public bool IsHighlighter { get => GetValue(IsHighlighterProperty); set => SetValue(IsHighlighterProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StrokesProperty)
        {
            if (change.OldValue is INotifyCollectionChanged old)
            {
                old.CollectionChanged -= OnStrokesChanged;
            }
            if (change.NewValue is INotifyCollectionChanged now)
            {
                now.CollectionChanged += OnStrokesChanged;
            }
        }
    }

    private void OnStrokesChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        // Transparent fill so the whole page receives pointer input.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        foreach (var stroke in Strokes ?? [])
        {
            DrawStroke(context, stroke.Points, Color.FromUInt32(stroke.Argb), stroke.Width, stroke.Highlighter);
        }
        if (_current is { Count: > 0 })
        {
            DrawStroke(context, _current, CurrentColor, InkWidth, IsHighlighter);
        }
    }

    private Color CurrentColor => IsHighlighter ? Color.FromArgb(110, InkColor.R, InkColor.G, InkColor.B) : InkColor;

    private void DrawStroke(DrawingContext context, IReadOnlyList<(double X, double Y)> points, Color color, double width, bool highlighter)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var pen = new Pen(new SolidColorBrush(color), Math.Max(1, width * w),
            lineCap: highlighter ? PenLineCap.Flat : PenLineCap.Round, lineJoin: PenLineJoin.Round);
        if (points.Count == 1)
        {
            context.DrawEllipse(pen.Brush, null, new Point(points[0].X * w, points[0].Y * h), pen.Thickness / 2, pen.Thickness / 2);
            return;
        }
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(points[0].X * w, points[0].Y * h), false);
            foreach (var p in points.Skip(1))
            {
                g.LineTo(new Point(p.X * w, p.Y * h));
            }
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private (double, double) Normalize(Point p) =>
        (Math.Clamp(p.X / Bounds.Width, 0, 1), Math.Clamp(p.Y / Bounds.Height, 0, 1));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Bounds.Width <= 0)
        {
            return;
        }
        _current = [Normalize(e.GetPosition(this))];
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_current is null)
        {
            return;
        }
        _current.Add(Normalize(e.GetPosition(this)));
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_current is null)
        {
            return;
        }
        var color = CurrentColor;
        Strokes?.Add(new InkStroke(_current, color.ToUInt32(), InkWidth, IsHighlighter));
        _current = null;
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
    }
}
