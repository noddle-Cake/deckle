namespace Scanwise.App.Services;

/// <summary>
/// For XAML: <c>IsVisible="{x:Static svc:PlatformLook.NotMac}"</c> hides Windows-style controls whose job the macOS
/// toolbar or menu bar does. Follows <see cref="AppShortcuts.IsMac"/>, read when the view is created.
/// </summary>
public static class PlatformLook
{
    public static bool IsMac => AppShortcuts.IsMac;
    public static bool NotMac => !AppShortcuts.IsMac;
}
