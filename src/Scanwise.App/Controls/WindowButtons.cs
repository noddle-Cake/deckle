using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Scanwise.App.Controls;

public enum WindowButtonStyle { Gnome, Mac }

/// <summary>
/// Close / minimize / maximize buttons for looks that draw their own window frame: GNOME's header bar buttons
/// (round and flat, in the order of the user's button-layout setting), or macOS traffic lights when the macOS look
/// runs on Windows or Linux. The native frames keep their own buttons.
/// </summary>
public sealed class WindowButtons : StackPanel
{
    public WindowButtons(WindowButtonStyle style, IEnumerable<string> buttons)
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Spacing = style == WindowButtonStyle.Mac ? 8 : 12;
        foreach (var name in buttons)
        {
            Children.Add(style == WindowButtonStyle.Mac ? TrafficLight(name) : GnomeButton(name));
        }
    }

    public IEnumerable<string> ButtonNames => Children.OfType<Button>().Select(b => (string)b.Tag!);

    private static Button GnomeButton(string name)
    {
        var button = new Button
        {
            Tag = name,
            Width = 24,
            Height = 24,
            Padding = default,
            CornerRadius = new CornerRadius(12),
            BorderThickness = default,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new LucideIcon
            {
                Kind = name switch { "minimize" => LucideKind.Minus, "maximize" => LucideKind.Square, _ => LucideKind.X },
                FontSize = 13,
            },
        };
        button.Classes.Add("windowbutton");
        Wire(button, name);
        return button;
    }

    private static Button TrafficLight(string name)
    {
        var button = new Button
        {
            Tag = name,
            Width = 12,
            Height = 12,
            Padding = default,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(0.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)),
            Background = new SolidColorBrush(Color.Parse(name switch
            {
                "close" => "#FF5F57",
                "minimize" => "#FEBC2E",
                _ => "#28C840",
            })),
        };
        button.Classes.Add("trafficlight");
        Wire(button, name);
        return button;
    }

    private static void Wire(Button button, string name)
    {
        var label = name switch { "minimize" => "Minimize", "maximize" => "Maximize", _ => "Close" };
        ToolTip.SetTip(button, label);
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(button) is not Window window)
            {
                return;
            }
            switch (name)
            {
                case "minimize":
                    window.WindowState = WindowState.Minimized;
                    break;
                case "maximize":
                    window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    break;
                default:
                    window.Close();
                    break;
            }
        };
        // A press on a window button must not start a window drag.
        button.AddHandler(PointerPressedEvent, (_, e) => e.Handled = true, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
    }
}
