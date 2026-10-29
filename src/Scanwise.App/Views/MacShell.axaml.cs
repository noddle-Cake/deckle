using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Scanwise.App.Views;

/// <summary>The macOS window chrome around MainWindow's content: toolbar in the title bar, sidebar column.</summary>
public partial class MacShell : UserControl
{
    /// <summary>Room for the close/minimize/zoom buttons at the window's top-left corner.</summary>
    public const double TrafficLightsWidth = 78;

    public MacShell()
    {
        InitializeComponent();
        Toolbar.PointerPressed += OnTitleBarPressed;
        SidebarTitleArea.PointerPressed += OnTitleBarPressed;
    }

    public object? Body
    {
        get => BodyHost.Content;
        set => BodyHost.Content = value;
    }

    /// <summary>Follows the scan options column; with the sidebar hidden the toolbar starts after the traffic lights.</summary>
    public void SetSidebarWidth(double width)
    {
        Root.ColumnDefinitions[0].Width = new GridLength(width);
        SidebarTitleArea.IsVisible = width > 0;
        var margin = Toolbar.Margin;
        Toolbar.Margin = new Thickness(width > 0 ? 8 : TrafficLightsWidth, margin.Top, margin.Right, margin.Bottom);
    }

    /// <summary>Empty title bar space moves the window and double-click zooms it, as in any Mac window.</summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            window.BeginMoveDrag(e);
        }
        e.Handled = true;
    }
}
