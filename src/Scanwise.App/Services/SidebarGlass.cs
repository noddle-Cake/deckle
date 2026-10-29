namespace Scanwise.App.Services;

/// <summary>
/// The Liquid Glass sidebar on macOS 26 (tasks.md 9.4): a native NSGlassEffectView floats as an inset, rounded
/// panel behind the scan options, as macOS 26 sidebars do. Only the sidebar gets glass; the pages, editor and
/// settings stay in the content layer ("Don't use Liquid Glass in the content layer", Materials HIG).
/// </summary>
public static class SidebarGlass
{
    /// <summary>Gap between the panel and the window's top, leading and bottom edges.</summary>
    public const double Inset = 8;

    /// <summary>Concentric with the window's corners (about 26 pt on macOS 26) less the inset.</summary>
    public const double CornerRadius = 18;

    /// <summary>On by default where the system has it; SCANWISE_LIQUID_GLASS=0 turns it off to compare.</summary>
    public static bool Enabled => Environment.GetEnvironmentVariable("SCANWISE_LIQUID_GLASS") != "0";

    /// <summary>
    /// The panel's frame in the window's content view, in points. The insets are equal at top and bottom, so the
    /// result is the same whether the view counts y from the top or the bottom. Null when there's no sidebar.
    /// </summary>
    public static (double X, double Y, double Width, double Height)? Frame(double sidebarWidth, double windowHeight)
    {
        if (sidebarWidth <= 2 * Inset || windowHeight <= 2 * Inset)
        {
            return null;
        }
        return (Inset, Inset, sidebarWidth - Inset, windowHeight - 2 * Inset);
    }
}
