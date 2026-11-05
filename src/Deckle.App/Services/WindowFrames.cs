using Avalonia;
using Avalonia.Controls;
using Deckle.Core.Settings;

namespace Deckle.App.Services;

/// <summary>Brings a window back where the user left it (expected of Mac apps, welcome everywhere).</summary>
public static class WindowFrames
{
    /// <summary>
    /// The window's frame now. A maximized window keeps its <paramref name="previous"/> normal size and position,
    /// so un-maximizing after the next launch goes back to them.
    /// </summary>
    public static WindowFrame Capture(Window window, WindowFrame? previous)
    {
        if (window.WindowState == WindowState.Maximized && previous is not null)
        {
            return new WindowFrame { X = previous.X, Y = previous.Y, Width = previous.Width, Height = previous.Height, Maximized = true };
        }
        return new WindowFrame
        {
            X = window.Position.X,
            Y = window.Position.Y,
            Width = window.Width,
            Height = window.Height,
            Maximized = window.WindowState == WindowState.Maximized,
        };
    }

    /// <summary>Applies <paramref name="frame"/> before the window opens, unless it would be off every screen.</summary>
    public static void Restore(Window window, WindowFrame? frame)
    {
        if (frame is null || frame.Width < window.MinWidth || frame.Height < window.MinHeight || !IsOnScreen(window, frame))
        {
            return;
        }
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(frame.X, frame.Y);
        window.Width = frame.Width;
        window.Height = frame.Height;
        if (frame.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>The title bar must land on a connected screen, or the window could open where nobody can reach it.</summary>
    private static bool IsOnScreen(Window window, WindowFrame frame)
    {
        var titleBar = new PixelPoint(frame.X + 40, frame.Y + 10);
        return window.Screens.All.Any(s => s.WorkingArea.Contains(titleBar));
    }
}
