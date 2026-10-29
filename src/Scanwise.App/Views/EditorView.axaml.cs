using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Scanwise.App.Services;
using Scanwise.App.ViewModels;

namespace Scanwise.App.Views;

public partial class EditorView : UserControl
{
    private const double MagnifyStep = 0.25;
    private double _magnify;

    public EditorView()
    {
        InitializeComponent();
        Scroller.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty && Editor is { } editor)
            {
                editor.Viewport = Scroller.Bounds.Size;
            }
        };
        AddHandler(PointerWheelChangedEvent, OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(Gestures.PointerTouchPadGestureMagnifyEvent, OnMagnify);
        KeyDown += OnKeyDown;
        Focusable = true;
    }

    private EditorViewModel? Editor => (DataContext as MainViewModel)?.Editor;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (Editor is { } editor)
        {
            editor.Viewport = Scroller.Bounds.Size;
        }
    }

    /// <summary>Trackpad pinch zooms one step each time the fingers spread or close by about a quarter.</summary>
    private void OnMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (Editor is not { } editor)
        {
            return;
        }
        _magnify += e.Delta.X;
        if (_magnify >= MagnifyStep)
        {
            editor.ZoomInCommand.Execute(null);
            _magnify = 0;
        }
        else if (_magnify <= -MagnifyStep)
        {
            editor.ZoomOutCommand.Execute(null);
            _magnify = 0;
        }
        e.Handled = true;
    }

    /// <summary>Ctrl+wheel (⌘+wheel on macOS) zooms; plain wheel scrolls.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Editor is not { } editor || !e.KeyModifiers.HasFlag(AppShortcuts.CommandModifier))
        {
            return;
        }
        if (e.Delta.Y > 0)
        {
            editor.ZoomInCommand.Execute(null);
        }
        else if (e.Delta.Y < 0)
        {
            editor.ZoomOutCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Editor is not { } editor)
        {
            return;
        }
        switch (e.Key)
        {
            case var _ when AppShortcuts.Matches(AppShortcuts.PreviousPage, e):
                editor.PreviousCommand.Execute(null);
                break;
            case var _ when AppShortcuts.Matches(AppShortcuts.NextPage, e):
                editor.NextCommand.Execute(null);
                break;
            case var _ when AppShortcuts.Matches(AppShortcuts.ZoomIn, e):
                editor.ZoomInCommand.Execute(null);
                break;
            case var _ when AppShortcuts.Matches(AppShortcuts.ZoomOut, e):
                editor.ZoomOutCommand.Execute(null);
                break;
            case var _ when AppShortcuts.Matches(AppShortcuts.ZoomFit, e):
                editor.ZoomFitCommand.Execute(null);
                break;
            case Key.Enter when editor.IsCropping:
                editor.ApplyCropCommand.Execute(null);
                break;
            case Key.Escape when editor.IsToolOpen:
                editor.CancelToolCommand.Execute(null);
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}
