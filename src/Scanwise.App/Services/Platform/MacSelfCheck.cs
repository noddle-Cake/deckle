using System.Runtime.Versioning;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Scanwise.App.ViewModels;
using Scanwise.App.Views;

namespace Scanwise.App.Services.Platform;

/// <summary>
/// "Scanwise --self-check[=report.txt]" on a Mac (tasks.md 9.5): asks AppKit what Scanwise actually installed —
/// menu bar and key equivalents, Dock menu, pasteboard types, edited dot, Dock badge, Liquid Glass panel — and
/// writes PASS/FAIL lines. Covers what can be read back; scripts/mac-check.sh and docs/MAC-VERIFICATION.md cover
/// what a person has to look at. Note: the pasteboard check replaces the clipboard's contents.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacSelfCheck
{
    private readonly StringBuilder _report = new();
    private int _failures;

    public static async Task<int> RunAsync(MainWindow window, MainViewModel vm, string reportPath)
    {
        var check = new MacSelfCheck();
        try
        {
            await check.RunChecksAsync(window, vm);
        }
        catch (Exception ex)
        {
            check.Fail("self-check", $"stopped with {ex.GetType().Name}: {ex.Message}");
        }
        check.Info("result", check._failures == 0 ? "all checks passed" : $"{check._failures} failed");
        await File.WriteAllTextAsync(reportPath, check._report.ToString());
        Console.Write(check._report.ToString());
        return check._failures;
    }

    private async Task RunChecksAsync(MainWindow window, MainViewModel vm)
    {
        Info("macOS", Environment.OSVersion.VersionString);
        Info("architecture", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString());

        // Managed side: the Mac conventions are switched on.
        Expect("mac conventions", AppShortcuts.IsMac, "AppShortcuts.IsMac is false");
        Info("look", PlatformLook.Current.ToString());
        if (PlatformLook.IsMac)
        {
            Expect("mac shell", window.MacShell is not null, $"window content is {window.Content?.GetType().Name}");
        }
        else
        {
            Skip("mac shell", $"Look is set to {PlatformLook.Current}; the native menu bar and Dock still apply");
        }
        Expect("command shortcuts", window.KeyBindings.Any(b => b.Gesture.KeyModifiers == KeyModifiers.Meta),
            "no ⌘ key bindings on the window");
        Info("default font", FontManager.Current.DefaultFontFamily.Name);
        Expect("vibrancy", window.ActualTransparencyLevel != WindowTransparencyLevel.None,
            $"transparency level is {window.ActualTransparencyLevel} (Reduce Transparency on?)", warnOnly: true);

        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "NSView", Handle: var view } || view == IntPtr.Zero)
        {
            Fail("native view", "no NSView handle; the native checks can't run");
            return;
        }
        Info("window-number", MacIntegration.WindowNumber(view).ToString()); // scripts/mac-check.sh screenshots it.

        CheckBundle();
        CheckMenuBar();
        CheckDockMenu();
        CheckDocumentState(view, vm);
        CheckDockBadge();
        await CheckPasteboardAsync();
        CheckGlass(window, view);
    }

    private void CheckBundle()
    {
        var id = MacIntegration.BundleIdentifier();
        if (id.Length == 0)
        {
            Skip("bundle id", "not running from Scanwise.app (no Info.plist)");
            return;
        }
        Expect("bundle id", id == "com.cjservin.scanwise", $"bundle id is {id}");
    }

    private void CheckMenuBar()
    {
        var items = MacIntegration.MenuBarItems();
        var menus = items.Select(i => i.Menu.Split(" ▸ ")[0]).Distinct().ToList();
        Info("menus", string.Join(", ", menus));
        var wanted = new[] { "File", "Edit", "View", "Page", "Window", "Help" };
        var order = wanted.Select(m => menus.IndexOf(m)).ToList();
        Expect("menu bar order", order.All(i => i >= 0) && order.SequenceEqual(order.Order()),
            $"expected {string.Join(", ", wanted)} in that order");

        foreach (var (menu, title, key, mods) in items.Where(i => i.Key.Length > 0))
        {
            Expect($"⌘ on {menu} ▸ {title}", (mods & MacIntegration.CommandMask) != 0, $"key '{key}' without Command (mask {mods:x})");
        }
        ExpectItem(items, "Save", "s", MacIntegration.CommandMask);
        ExpectItem(items, "Settings…", ",", MacIntegration.CommandMask);
        ExpectItem(items, "Import…", "o", MacIntegration.CommandMask);
        var redo = items.FirstOrDefault(i => i.Title == "Redo");
        Expect("Redo is ⇧⌘Z", redo.Key.Equals("z", StringComparison.OrdinalIgnoreCase)
            && (redo.Key == "Z" || (redo.Modifiers & MacIntegration.ShiftMask) != 0),
            $"Redo has '{redo.Key}' mask {redo.Modifiers:x}");
        Expect("no ⌘E", !items.Any(i => i.Key == "e" && i.Modifiers == MacIntegration.CommandMask),
            "⌘E is bound; it belongs to Use Selection for Find");
    }

    private void ExpectItem(List<MacIntegration.MenuEntry> items, string title, string key, ulong mods)
    {
        var item = items.FirstOrDefault(i => i.Title == title);
        Expect($"{title} shortcut", item.Key == key && (item.Modifiers & ~MacIntegration.ShiftMask) == mods,
            item.Title is null ? "menu item missing" : $"has '{item.Key}' mask {item.Modifiers:x}");
    }

    private void CheckDockMenu()
    {
        var titles = MacIntegration.DockMenuTitles();
        Expect("dock menu", titles.SequenceEqual(["Scan", "Scan into New Document"]), $"dock menu is [{string.Join(", ", titles)}]");
    }

    private void CheckDocumentState(IntPtr view, MainViewModel vm)
    {
        Expect("edited dot matches", MacIntegration.IsDocumentEdited(view) == vm.HasUnsavedChanges, "documentEdited out of step");
        MacIntegration.SetDocumentState(view, true, null);
        var set = MacIntegration.IsDocumentEdited(view);
        MacIntegration.SetDocumentState(view, vm.HasUnsavedChanges, vm.DocumentPath);
        Expect("edited dot can be set", set, "setDocumentEdited: had no effect");
    }

    private void CheckDockBadge()
    {
        MacIntegration.SetDockBadge("42%");
        var badge = MacIntegration.DockBadge();
        MacIntegration.SetDockBadge(null);
        Expect("dock badge", badge == "42%", $"badge read back as '{badge}'");
    }

    private async Task CheckPasteboardAsync()
    {
        var png = Path.Combine(Path.GetTempPath(), $"scanwise-selfcheck-{Environment.ProcessId}.png");
        try
        {
            using (var bitmap = new RenderTargetBitmap(new PixelSize(8, 8)))
            {
                bitmap.Save(png);
            }
            await PlatformShell.CopyImageAsync(png);
            var types = MacIntegration.PasteboardTypes();
            Expect("pasteboard PNG", types.Contains("public.png"), $"types: {string.Join(", ", types)}");
            Expect("pasteboard TIFF", types.Contains("public.tiff"), $"types: {string.Join(", ", types)}");
        }
        finally
        {
            File.Delete(png);
        }
    }

    private void CheckGlass(MainWindow window, IntPtr view)
    {
        if (!MacIntegration.HasClass("NSGlassEffectView"))
        {
            Skip("liquid glass", "NSGlassEffectView not available (before macOS 26); the 9.2 blur is used");
            return;
        }
        if (!SidebarGlass.Enabled)
        {
            Skip("liquid glass", "turned off with SCANWISE_LIQUID_GLASS=0");
            return;
        }
        var glass = window.SidebarGlassView;
        if (glass == IntPtr.Zero)
        {
            Fail("liquid glass", "no panel was created");
            return;
        }
        var state = MacIntegration.Glass(glass, view);
        Info("glass container", state.Container);
        Expect("glass shares Avalonia's container", state.SameContainer, "the panel isn't next to Avalonia's view");
        Expect("glass is behind the content", state.GlassIndex >= 0 && state.GlassIndex < state.ViewIndex,
            $"panel index {state.GlassIndex}, Avalonia view index {state.ViewIndex}");
        var sidebarShown = window.MacShell?.Classes.Contains("glass") == true && (window.DataContext as MainViewModel)?.SidebarVisible == true;
        if (sidebarShown)
        {
            Expect("glass visible with the sidebar", !state.Hidden, "panel is hidden");
            Expect("glass width", Math.Abs(state.Width - (340 - SidebarGlass.Inset)) < 1, $"width {state.Width}");
        }
        Info("glass frame", $"{state.Width:0}×{state.Height:0}, hidden={state.Hidden}");
    }

    private void Expect(string name, bool ok, string problem, bool warnOnly = false)
    {
        if (ok)
        {
            _report.AppendLine($"PASS  {name}");
        }
        else if (warnOnly)
        {
            _report.AppendLine($"WARN  {name}: {problem}");
        }
        else
        {
            Fail(name, problem);
        }
    }

    private void Fail(string name, string problem)
    {
        _failures++;
        _report.AppendLine($"FAIL  {name}: {problem}");
    }

    private void Skip(string name, string why) => _report.AppendLine($"SKIP  {name}: {why}");

    private void Info(string name, string value) => _report.AppendLine($"INFO  {name}: {value}");
}
