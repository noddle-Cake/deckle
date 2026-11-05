using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Deckle.App.Controls;

namespace Deckle.App.Views;

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

    /// <summary>
    /// The macOS look on Windows or Linux: draws traffic lights where macOS puts them (the window draws its own
    /// frame there, see ClientDecorations).
    /// </summary>
    public void UseOwnWindowButtons()
    {
        var lights = new WindowButtons(WindowButtonStyle.Mac, ["close", "minimize", "maximize"])
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Margin = new Thickness(20, 0, 0, 0),
        };
        Grid.SetRow(lights, 0);
        Grid.SetColumnSpan(lights, 2);
        Root.Children.Add(lights);
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
