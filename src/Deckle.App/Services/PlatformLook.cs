using Deckle.Core.Settings;

namespace Deckle.App.Services;

/// <summary>
/// The look the window follows (tasks.md phase 12): Windows (Fluent), macOS or GNOME, chosen in Settings
/// (Automatic = the operating system's own) and fixed when the app starts. It decides layout and conventions:
/// which window shell, title-case menus, dialog button order, Settings in a page or a window.
/// Keyboard modifiers and native integrations follow the real OS instead (<see cref="AppShortcuts.IsMac"/>,
/// <c>OperatingSystem.IsMacOS()</c>): a Mac keyboard has ⌘ whatever the look.
/// XAML: <c>IsVisible="{x:Static svc:PlatformLook.IsWindows}"</c> for Fluent-only controls.
/// </summary>
public static class PlatformLook
{
    /// <summary>Set once at startup (tests switch it to render each look).</summary>
    public static AppLook Current { get; set; } = Resolve(AppLook.Automatic);

    public static AppLook Resolve(AppLook setting) => setting switch
    {
        AppLook.Automatic when OperatingSystem.IsMacOS() => AppLook.MacOS,
        AppLook.Automatic when OperatingSystem.IsWindows() => AppLook.Windows,
        AppLook.Automatic => AppLook.Gnome,
        _ => setting,
    };

    public static bool IsWindows => Current == AppLook.Windows;
    public static bool IsMac => Current == AppLook.MacOS;
    public static bool IsGnome => Current == AppLook.Gnome;
    public static bool NotGnome => !IsGnome;

    /// <summary>
    /// macOS and GNOME: a toolbar/header bar in the title bar instead of the Fluent nav rail, title-case menus,
    /// the confirming button last in dialogs, Settings and Help in their own windows, the document as the window
    /// title, and no question at launch.
    /// </summary>
    public static bool IsToolbarLook => IsMac || IsGnome;

    /// <summary>
    /// Action buttons inside the page list and editor (rotate, copy, share, undo…): Windows and GNOME. The macOS
    /// look moves them to the toolbar and the menu bar; GNOME has no menu bar, so they stay with their content.
    /// </summary>
    public static bool HasPaneActions => !IsMac;
}
