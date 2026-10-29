using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Scanwise.App.Services;

/// <summary>
/// The app's keyboard shortcuts for each platform. macOS uses Command (Apple HIG: "Prefer the Command key as the
/// main modifier"), ⇧⌘Z for redo, ⌘⌫ for delete and ⌘, for Settings; Windows and Linux use Ctrl. Key bindings,
/// tooltips, menus and the Help page all read from here, so they can't disagree.
/// </summary>
public static class AppShortcuts
{
    public const string Scan = nameof(Scan);
    public const string ScanNew = nameof(ScanNew);
    public const string Save = nameof(Save);
    public const string SaveAs = nameof(SaveAs);
    public const string Import = nameof(Import);
    public const string Copy = nameof(Copy);
    public const string Share = nameof(Share);
    public const string Undo = nameof(Undo);
    public const string Redo = nameof(Redo);
    public const string Delete = nameof(Delete);
    public const string SelectAll = nameof(SelectAll);
    public const string RotateLeft = nameof(RotateLeft);
    public const string RotateRight = nameof(RotateRight);
    public const string MoveUp = nameof(MoveUp);
    public const string MoveDown = nameof(MoveDown);
    public const string ZoomIn = nameof(ZoomIn);
    public const string ZoomOut = nameof(ZoomOut);
    public const string ZoomFit = nameof(ZoomFit);
    public const string PreviousPage = nameof(PreviousPage);
    public const string NextPage = nameof(NextPage);
    public const string Settings = nameof(Settings);
    public const string Cancel = nameof(Cancel);

    // Menu bar only (macOS).
    public const string Cut = nameof(Cut);
    public const string Paste = nameof(Paste);
    public const string Duplicate = nameof(Duplicate);
    public const string ShowScan = nameof(ShowScan);
    public const string ShowPages = nameof(ShowPages);
    public const string ShowEditor = nameof(ShowEditor);
    public const string CloseWindow = nameof(CloseWindow);
    public const string Minimize = nameof(Minimize);
    public const string FullScreen = nameof(FullScreen);
    public const string ToggleSidebar = nameof(ToggleSidebar);

    /// <summary>Use macOS conventions. Tests switch this to check both platforms.</summary>
    public static bool IsMac { get; set; } = OperatingSystem.IsMacOS();

    /// <summary>⌘ on macOS, Ctrl elsewhere (also the modifier for wheel zoom).</summary>
    public static KeyModifiers CommandModifier => IsMac ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>Every gesture for <paramref name="action"/>; the first is the one shown in labels and menus.</summary>
    public static IReadOnlyList<KeyGesture> Gestures(string action)
    {
        var cmd = CommandModifier;
        const KeyModifiers shift = KeyModifiers.Shift, alt = KeyModifiers.Alt, none = KeyModifiers.None;
        return action switch
        {
            Scan => [new(Key.N, cmd)],
            ScanNew => [new(Key.N, cmd | shift)],
            Save => [new(Key.S, cmd)],
            SaveAs => [new(Key.S, cmd | shift)],
            Import => [new(Key.O, cmd)],
            Copy => [new(Key.C, cmd)],
            // ⌘E is "Use Selection for Find" on macOS, so Share has no shortcut there.
            Share => IsMac ? [] : [new(Key.E, cmd)],
            Undo => [new(Key.Z, cmd)],
            Redo => IsMac ? [new(Key.Z, cmd | shift)] : [new(Key.Y, cmd), new(Key.Z, cmd | shift)],
            Delete => IsMac ? [new(Key.Back, cmd), new(Key.Delete, none)] : [new(Key.Delete, none)],
            SelectAll => [new(Key.A, cmd)],
            RotateLeft => IsMac ? [new(Key.L, cmd), new(Key.R, shift)] : [new(Key.R, shift)],
            RotateRight => IsMac ? [new(Key.R, cmd), new(Key.R, none)] : [new(Key.R, none)],
            MoveUp => [new(Key.Up, IsMac ? alt | cmd : alt)],
            MoveDown => [new(Key.Down, IsMac ? alt | cmd : alt)],
            ZoomIn => [new(Key.OemPlus, cmd), new(Key.Add, cmd)],
            ZoomOut => [new(Key.OemMinus, cmd), new(Key.Subtract, cmd)],
            ZoomFit => [new(Key.D0, cmd), new(Key.NumPad0, cmd)],
            PreviousPage => [new(Key.PageUp, none)],
            NextPage => [new(Key.PageDown, none)],
            Settings => IsMac ? [new(Key.OemComma, cmd)] : [],
            Cancel => [new(Key.Escape, none)],
            Cut => [new(Key.X, cmd)],
            Paste => [new(Key.V, cmd)],
            Duplicate => IsMac ? [new(Key.D, cmd)] : [],
            ShowScan => IsMac ? [new(Key.D1, cmd)] : [],
            ShowPages => IsMac ? [new(Key.D2, cmd)] : [],
            ShowEditor => IsMac ? [new(Key.D3, cmd)] : [],
            CloseWindow => IsMac ? [new(Key.W, cmd)] : [],
            Minimize => IsMac ? [new(Key.M, cmd)] : [],
            FullScreen => IsMac ? [new(Key.F, KeyModifiers.Control | cmd)] : [],
            ToggleSidebar => IsMac ? [new(Key.S, KeyModifiers.Control | cmd)] : [],
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown shortcut."),
        };
    }

    /// <summary>The gesture shown for <paramref name="action"/>, or null when it has none on this platform.</summary>
    public static KeyGesture? Gesture(string action) => Gestures(action).FirstOrDefault();

    public static bool Matches(string action, KeyEventArgs e) => Gestures(action).Any(g => g.Matches(e));

    /// <summary>"⇧⌘S" on macOS, "Ctrl+Shift+S" elsewhere; null when the action has no shortcut.</summary>
    public static string? Label(string action) => Gesture(action) is { } gesture ? Format(gesture) : null;

    /// <summary>Tooltip text with the shortcut in brackets, when there is one.</summary>
    public static string Tip(string text, string action) => Label(action) is { } label ? $"{text} ({label})" : text;

    public static string Format(KeyGesture gesture)
    {
        var m = gesture.KeyModifiers;
        if (IsMac)
        {
            // HIG order: Control, Option, Shift, Command.
            return (m.HasFlag(KeyModifiers.Control) ? "⌃" : "") + (m.HasFlag(KeyModifiers.Alt) ? "⌥" : "")
                + (m.HasFlag(KeyModifiers.Shift) ? "⇧" : "") + (m.HasFlag(KeyModifiers.Meta) ? "⌘" : "")
                + KeyName(gesture.Key);
        }
        var parts = new List<string>();
        if (m.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }
        if (m.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }
        if (m.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }
        parts.Add(KeyName(gesture.Key));
        return string.Join("+", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        Key.Back => IsMac ? "⌫" : "Backspace",
        Key.Delete => IsMac ? "⌦" : "Delete",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.OemComma => ",",
        Key.OemPlus => IsMac ? "+" : "=",
        Key.OemMinus => "-",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        Key.Escape => IsMac ? "⎋" : "Esc",
        Key.PageUp => "Page Up",
        Key.PageDown => "Page Down",
        _ => key.ToString(),
    };

    /// <summary>Adds a key binding for every gesture of <paramref name="action"/>.</summary>
    public static void Bind(InputElement target, string action, ICommand command)
    {
        foreach (var gesture in Gestures(action))
        {
            target.KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
        }
    }
}

/// <summary>XAML: <c>ToolTip.Tip="{svc:ShortcutTip 'Save as…', SaveAs}"</c> gives "Save as… (⇧⌘S)" on macOS.</summary>
public sealed class ShortcutTipExtension(string text, string action) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => AppShortcuts.Tip(text, action);
}

/// <summary>XAML: <c>InputGesture="{svc:Shortcut Save}"</c>; nothing when the action has no shortcut here.</summary>
public sealed class ShortcutExtension(string action) : MarkupExtension
{
    // Null is a valid InputGesture/Gesture value: the menu item just shows no shortcut.
    public override object ProvideValue(IServiceProvider serviceProvider) => AppShortcuts.Gesture(action)!;
}

/// <summary>XAML: the shortcut text alone, for the Help page. Several space-separated actions give "⌘S / ⇧⌘S".</summary>
public sealed class ShortcutLabelExtension(string actions) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        string.Join(" / ", actions.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(AppShortcuts.Label).OfType<string>());
}
