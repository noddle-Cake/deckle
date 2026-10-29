using System.Globalization;
using Avalonia.Data.Converters;

namespace Scanwise.App.Services;

/// <summary>
/// Turns an icon button's tooltip into its screen reader name: "Save as… (⇧⌘S)" is read as "Save as…".
/// Styles.axaml uses it so every icon-only button has a name for VoiceOver, Narrator and Orca.
/// </summary>
public sealed class AccessibleName : IValueConverter
{
    public static readonly AccessibleName Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        string text => Strip(text),
        _ => null,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    public static string Strip(string tooltip)
    {
        var open = tooltip.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && tooltip.EndsWith(')') ? tooltip[..open] : tooltip;
    }
}
