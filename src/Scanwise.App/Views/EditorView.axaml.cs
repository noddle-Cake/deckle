using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Scanwise.App.ViewModels;

namespace Scanwise.App.Views;

public partial class EditorView : UserControl
{
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

    /// <summary>Ctrl+wheel zooms; plain wheel scrolls.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Editor is not { } editor || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
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
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        switch (e.Key)
        {
            case Key.PageUp:
                editor.PreviousCommand.Execute(null);
                break;
            case Key.PageDown:
                editor.NextCommand.Execute(null);
                break;
            case Key.OemPlus or Key.Add when ctrl:
                editor.ZoomInCommand.Execute(null);
                break;
            case Key.OemMinus or Key.Subtract when ctrl:
                editor.ZoomOutCommand.Execute(null);
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
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
