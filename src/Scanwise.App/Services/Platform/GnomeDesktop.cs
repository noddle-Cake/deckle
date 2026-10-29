using System.Diagnostics;
using Avalonia.Media;

namespace Scanwise.App.Services.Platform;

/// <summary>
/// GNOME settings the GNOME look follows: the accent colour (GNOME 47+) and which window buttons the header bar
/// shows, and on which side. Read with gsettings; GNOME's defaults (blue, close button only) where that fails —
/// other desktops, Windows, macOS.
/// </summary>
public static class GnomeDesktop
{
    /// <summary>libadwaita's accent colours by GNOME's accent-color names.</summary>
    private static readonly Dictionary<string, Color> Accents = new()
    {
        ["blue"] = Color.Parse("#3584E4"),
        ["teal"] = Color.Parse("#2190A4"),
        ["green"] = Color.Parse("#3A944A"),
        ["yellow"] = Color.Parse("#C88800"),
        ["orange"] = Color.Parse("#ED5B00"),
        ["red"] = Color.Parse("#E62D42"),
        ["pink"] = Color.Parse("#D56199"),
        ["purple"] = Color.Parse("#9141AC"),
        ["slate"] = Color.Parse("#6F8396"),
    };

    public static Color AccentColor() =>
        Accents.TryGetValue(Read("org.gnome.desktop.interface", "accent-color") ?? "blue", out var color) ? color : Accents["blue"];

    /// <summary>Window buttons before and after the title, from GNOME's button-layout (e.g. "appmenu:minimize,close").</summary>
    public static (string[] Start, string[] End) ButtonLayout() =>
        ParseButtonLayout(Read("org.gnome.desktop.wm.preferences", "button-layout") ?? "appmenu:close");

    public static (string[] Start, string[] End) ParseButtonLayout(string layout)
    {
        static string[] Buttons(string side) => side.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(b => b is "close" or "minimize" or "maximize").ToArray();
        var parts = layout.Split(':', 2);
        return (Buttons(parts[0]), parts.Length > 1 ? Buttons(parts[1]) : []);
    }

    /// <summary>A gsettings string value without its quotes, or null.</summary>
    private static string? Read(string schema, string key)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }
        try
        {
            var start = new ProcessStartInfo("gsettings") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("get");
            start.ArgumentList.Add(schema);
            start.ArgumentList.Add(key);
            using var process = Process.Start(start);
            if (process is null || !process.WaitForExit(1000) || process.ExitCode != 0)
            {
                return null;
            }
            return process.StandardOutput.ReadToEnd().Trim().Trim('\'');
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null; // No gsettings: not GNOME.
        }
    }
}
