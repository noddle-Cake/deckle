using Avalonia.Platform;
using Scanwise.App.Services.Platform;

namespace Scanwise.App.Services;

/// <summary>How the scan shortcut key behaves on this system.</summary>
public enum ScanHotkeyKind
{
    /// <summary>Handled by the system: works from anywhere, even when Scanwise is closed (Windows, GNOME).</summary>
    Global,

    /// <summary>Handled by Scanwise: works from any app while Scanwise is running (macOS).</summary>
    WhileRunning,

    /// <summary>Can't be set automatically; the user adds a shortcut that runs <see cref="ScanShortcuts.ScanCommand"/>.</summary>
    Manual,
}

/// <summary>
/// Ways to start a scan from outside the window: "Scan" in the taskbar jump list / launcher actions / Dock menu,
/// and a Ctrl+Alt+key (Control+Option+key) shortcut. All of them run <c>Scanwise --scan</c>, which
/// <see cref="SingleInstance"/> hands to the running window, except the macOS hotkey and Dock menu, which call
/// <see cref="ScanRequested"/> directly.
/// </summary>
public sealed class ScanShortcuts
{
    public const string ScanArgument = "--scan";

    /// <summary>Raised (on any thread) when the macOS Dock menu item or hotkey is used.</summary>
    public event Action? ScanRequested;

    /// <summary>The Scanwise executable; never the dotnet host (as when started with "dotnet Scanwise.dll").</summary>
    public static string ExePath
    {
        get
        {
            var process = Environment.ProcessPath;
            if (process is not null && !Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return process;
            }
            return Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Scanwise.exe" : "Scanwise");
        }
    }

    public static string ScanCommand => (ExePath.Contains(' ') ? $"\"{ExePath}\"" : ExePath) + " " + ScanArgument;

    public static ScanHotkeyKind HotkeyKind =>
        OperatingSystem.IsWindows() ? ScanHotkeyKind.Global
        : OperatingSystem.IsMacOS() ? ScanHotkeyKind.WhileRunning
        : OperatingSystem.IsLinux() && LinuxDesktop.CanSetShortcut ? ScanHotkeyKind.Global
        : ScanHotkeyKind.Manual;

    public static string KeyLabel(char key) => OperatingSystem.IsMacOS() ? $"⌃⌥{char.ToUpperInvariant(key)}" : $"Ctrl+Alt+{char.ToUpperInvariant(key)}";

    /// <summary>Adds the "Scan" entry to the taskbar jump list, the Linux launcher or the Dock menu. Never throws.</summary>
    public void InstallLauncherEntries()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                WindowsShell.UpdateJumpList(ExePath);
            }
            else if (OperatingSystem.IsLinux())
            {
                LinuxDesktop.InstallLauncher(ExePath, () => AssetLoader.Open(new Uri("avares://Scanwise/Assets/scanwise.png")));
            }
            else if (OperatingSystem.IsMacOS())
            {
                MacIntegration.InstallDockMenu(RaiseScanRequested, RaiseScanNewRequested);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Scanwise: could not add the Scan launcher entry: {ex}");
        }
    }

    /// <summary>
    /// Turns the shortcut on (or updates its key and the path to this copy of Scanwise) or off. Throws
    /// <see cref="InvalidOperationException"/> with a message for the user when it can't be done.
    /// </summary>
    public async Task ApplyHotkeyAsync(bool enabled, char key)
    {
        key = char.ToUpperInvariant(key);
        if (enabled && key is < 'A' or > 'Z')
        {
            throw new ArgumentOutOfRangeException(nameof(key), "The shortcut key must be a letter.");
        }
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (enabled)
                {
                    WindowsShell.CreateHotkeyShortcut(ExePath, key);
                }
                else
                {
                    WindowsShell.RemoveHotkeyShortcut();
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (enabled)
                {
                    MacIntegration.RegisterHotkey(key, RaiseScanRequested);
                }
                else
                {
                    MacIntegration.UnregisterHotkey();
                }
            }
            else if (OperatingSystem.IsLinux() && LinuxDesktop.CanSetShortcut)
            {
                if (enabled)
                {
                    await LinuxDesktop.SetShortcutAsync(ExePath, key);
                }
                else
                {
                    await LinuxDesktop.RemoveShortcutAsync();
                }
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Could not {(enabled ? "set up" : "remove")} the shortcut: {ex.Message}", ex);
        }
    }

    private void RaiseScanRequested() => ScanRequested?.Invoke();

    /// <summary>The Dock menu's "Scan into New Document" (macOS).</summary>
    public event Action? ScanNewRequested;

    private void RaiseScanNewRequested() => ScanNewRequested?.Invoke();
}
