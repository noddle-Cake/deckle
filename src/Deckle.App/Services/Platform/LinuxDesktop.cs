using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace Deckle.App.Services.Platform;

/// <summary>
/// Linux desktop integration: an app launcher entry with a "Scan" action (right-click in the dock or app grid),
/// and a GNOME custom keyboard shortcut.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class LinuxDesktop
{
    private const string DesktopFileName = "deckle.desktop";
    private const string Marker = "X-Deckle-Generated=true";
    private const string MediaKeysSchema = "org.gnome.settings-daemon.plugins.media-keys";
    private const string KeybindingSchema = "org.gnome.settings-daemon.plugins.media-keys.custom-keybinding";
    private const string KeybindingPath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/deckle/";
    private const string OldKeybindingPath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/scanwise/";

    private static string DataHome => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dir
        ? dir
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    public static string DesktopFilePath => Path.Combine(DataHome, "applications", DesktopFileName);

    /// <summary>
    /// Writes the launcher entry for this copy of Deckle. An entry pointing at another copy that still exists is
    /// left alone, so running a second (e.g. development) build doesn't take over the launcher.
    /// </summary>
    public static void InstallLauncher(string exePath, Func<Stream> openIcon)
    {
        if (File.Exists(DesktopFilePath))
        {
            var existing = File.ReadAllText(DesktopFilePath);
            if (!existing.Contains(Marker))
            {
                return; // Written by a package or by the user.
            }
            var exec = existing.Split('\n').FirstOrDefault(l => l.StartsWith("Exec=", StringComparison.Ordinal));
            var command = exec?["Exec=".Length..].Trim();
            if (command?.EndsWith(" %F", StringComparison.Ordinal) == true)
            {
                command = command[..^3];
            }
            var target = command is null ? null : Unquote(command);
            if (target is not null && target != exePath && File.Exists(target))
            {
                return;
            }
        }
        var iconPath = Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps", "deckle.png");
        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
        using (var icon = openIcon())
        using (var file = File.Create(iconPath))
        {
            icon.CopyTo(file);
        }
        var quoted = Quote(exePath);
        var content = new StringBuilder()
            .AppendLine("[Desktop Entry]")
            .AppendLine("Type=Application")
            .AppendLine("Name=Deckle")
            .AppendLine("GenericName=Document Scanner")
            .AppendLine("Comment=Scan documents and photos")
            .AppendLine($"Exec={quoted} %F")
            .AppendLine("Icon=deckle")
            .AppendLine("Terminal=false")
            .AppendLine("Categories=Graphics;Scanning;Office;")
            .AppendLine("Keywords=scan;scanner;pdf;document;ocr;")
            .AppendLine("StartupWMClass=Deckle")
            .AppendLine("Actions=Scan;")
            .AppendLine(Marker)
            .AppendLine()
            .AppendLine("[Desktop Action Scan]")
            .AppendLine("Name=Scan")
            .AppendLine($"Exec={quoted} --scan")
            .ToString();
        Directory.CreateDirectory(Path.GetDirectoryName(DesktopFilePath)!);
        var temp = DesktopFilePath + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, DesktopFilePath, overwrite: true);
    }

    /// <summary>Removes the launcher entry and icon generated under the old name, Scanwise.</summary>
    public static void RemoveOldLauncher()
    {
        var entry = Path.Combine(DataHome, "applications", "scanwise.desktop");
        if (File.Exists(entry) && File.ReadAllText(entry).Contains("X-Scanwise-Generated=true"))
        {
            File.Delete(entry);
            File.Delete(Path.Combine(DataHome, "icons", "hicolor", "256x256", "apps", "scanwise.png"));
        }
    }

    /// <summary>True on GNOME (and GNOME-based desktops) where the shortcut can be added automatically.</summary>
    public static bool CanSetShortcut =>
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Split(':').Any(d => d.Equals("GNOME", StringComparison.OrdinalIgnoreCase))
        && FindGsettings() is not null;

    /// <summary>Adds or updates the Ctrl+Alt+<paramref name="key"/> custom shortcut in GNOME Settings.</summary>
    public static async Task SetShortcutAsync(string exePath, char key)
    {
        await RemoveKeybindingAsync(OldKeybindingPath);
        var paths = await GetCustomPathsAsync();
        if (!paths.Contains(KeybindingPath))
        {
            paths.Add(KeybindingPath);
        }
        var relocatable = $"{KeybindingSchema}:{KeybindingPath}";
        await GsettingsAsync("set", relocatable, "name", Variant("Deckle Scan"));
        await GsettingsAsync("set", relocatable, "command", Variant($"{Quote(exePath)} --scan"));
        await GsettingsAsync("set", relocatable, "binding", Variant($"<Control><Alt>{char.ToLowerInvariant(key)}"));
        await GsettingsAsync("set", MediaKeysSchema, "custom-keybindings", FormatList(paths));
    }

    public static async Task RemoveShortcutAsync()
    {
        await RemoveKeybindingAsync(KeybindingPath);
        await RemoveKeybindingAsync(OldKeybindingPath);
    }

    private static async Task RemoveKeybindingAsync(string path)
    {
        var paths = await GetCustomPathsAsync();
        if (paths.Remove(path))
        {
            await GsettingsAsync("set", MediaKeysSchema, "custom-keybindings", FormatList(paths));
        }
        var relocatable = $"{KeybindingSchema}:{path}";
        foreach (var name in new[] { "name", "command", "binding" })
        {
            await GsettingsAsync("reset", relocatable, name);
        }
    }

    private static async Task<List<string>> GetCustomPathsAsync()
    {
        // Output looks like "['/org/.../custom0/', '/org/.../deckle/']" or "@as []".
        var raw = await GsettingsAsync("get", MediaKeysSchema, "custom-keybindings");
        return raw.Split('\'').Where((_, i) => i % 2 == 1).ToList();
    }

    private static string FormatList(IEnumerable<string> paths) => "[" + string.Join(", ", paths.Select(Variant)) + "]";

    /// <summary>A GVariant string literal: gsettings parses values, so text with spaces must be quoted.</summary>
    private static string Variant(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private static string? FindGsettings() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(d => Path.Combine(d, "gsettings"))
        .FirstOrDefault(File.Exists);

    private static async Task<string> GsettingsAsync(params string[] args)
    {
        var start = new ProcessStartInfo(FindGsettings() ?? "gsettings")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not run gsettings.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"gsettings {args[0]} failed: {(await error).Trim()}");
        }
        return (await output).Trim();
    }

    /// <summary>Quotes a path for a desktop entry Exec line or a GNOME shortcut command.</summary>
    private static string Quote(string path) =>
        path.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '\\' or '$' or '`')
            ? "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`") + "\""
            : path;

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"'
            ? value[1..^1].Replace("\\\"", "\"").Replace("\\$", "$").Replace("\\`", "`").Replace("\\\\", "\\")
            : value;
}
