using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Deckle.App.Controls;

/// <summary>
/// Client-side window decorations, for looks that draw their own title bar: GNOME (its header bar is the title
/// bar, everywhere) and the macOS look away from macOS. Turns the system frame off and adds what it provided —
/// resizing from the edges and corners, and a thin border. Moving, double-click to maximize and the window buttons
/// are the header bar's job (MacShell, GnomeShell, WindowButtons).
/// </summary>
public static class ClientDecorations
{
    private const double Grip = 6;

    public static void Apply(Window window, Control content)
    {
        window.WindowDecorations = WindowDecorations.None;
        var root = new Grid();
        root.Children.Add(content);
        var border = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
        border.Bind(Border.BorderBrushProperty, window.GetResourceObservable("WindowBorder"));
        root.Children.Add(border);
        foreach (var (edge, align) in Grips)
        {
            root.Children.Add(GripFor(window, edge, align));
        }
        window.Content = root;
    }

    private static readonly (WindowEdge Edge, (HorizontalAlignment H, VerticalAlignment V) Align)[] Grips =
    [
        (WindowEdge.North, (HorizontalAlignment.Stretch, VerticalAlignment.Top)),
        (WindowEdge.South, (HorizontalAlignment.Stretch, VerticalAlignment.Bottom)),
        (WindowEdge.West, (HorizontalAlignment.Left, VerticalAlignment.Stretch)),
        (WindowEdge.East, (HorizontalAlignment.Right, VerticalAlignment.Stretch)),
        (WindowEdge.NorthWest, (HorizontalAlignment.Left, VerticalAlignment.Top)),
        (WindowEdge.NorthEast, (HorizontalAlignment.Right, VerticalAlignment.Top)),
        (WindowEdge.SouthWest, (HorizontalAlignment.Left, VerticalAlignment.Bottom)),
        (WindowEdge.SouthEast, (HorizontalAlignment.Right, VerticalAlignment.Bottom)),
    ];

    private static Border GripFor(Window window, WindowEdge edge, (HorizontalAlignment H, VerticalAlignment V) align)
    {
        var corner = edge is WindowEdge.NorthWest or WindowEdge.NorthEast or WindowEdge.SouthWest or WindowEdge.SouthEast;
        var grip = new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = align.H,
            VerticalAlignment = align.V,
            Width = align.H == HorizontalAlignment.Stretch ? double.NaN : (corner ? Grip * 2 : Grip),
            Height = align.V == VerticalAlignment.Stretch ? double.NaN : (corner ? Grip * 2 : Grip),
            Cursor = new Cursor(edge switch
            {
                WindowEdge.North => StandardCursorType.TopSide,
                WindowEdge.South => StandardCursorType.BottomSide,
                WindowEdge.West => StandardCursorType.LeftSide,
                WindowEdge.East => StandardCursorType.RightSide,
                WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
                WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
                WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
                _ => StandardCursorType.BottomRightCorner,
            }),
        };
        grip.PointerPressed += (_, e) =>
        {
            if (window.WindowState == WindowState.Normal && e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            {
                window.BeginResizeDrag(edge, e);
                e.Handled = true;
            }
        };
        return grip;
    }
}
