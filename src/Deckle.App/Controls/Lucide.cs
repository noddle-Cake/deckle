using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace Deckle.App.Controls;

/// <summary>
/// Deckle's icons: Lucide (https://lucide.dev, ISC), an outline set that reads as neither Windows nor macOS.
/// They come from a font (Assets/Fonts/lucide.ttf), so they take the text colour, including disabled and accent.
/// </summary>
public static partial class Lucide
{
    public static readonly FontFamily Font = new("avares://Deckle/Assets/Fonts#lucide");

    public static string Glyph(LucideKind kind) => Code(kind);
}

/// <summary>XAML: <c>&lt;c:LucideIcon Kind="Undo2" /&gt;</c>, sized by FontSize like any FAFontIcon.</summary>
public class LucideIcon : FAFontIcon
{
    public static readonly StyledProperty<LucideKind> KindProperty =
        AvaloniaProperty.Register<LucideIcon, LucideKind>(nameof(Kind));

    static LucideIcon()
    {
        KindProperty.Changed.AddClassHandler<LucideIcon>((icon, _) => icon.Glyph = Lucide.Glyph(icon.Kind));
    }

    public LucideIcon()
    {
        FontFamily = Lucide.Font;
        Glyph = Lucide.Glyph(Kind);
    }

    protected override Type StyleKeyOverride => typeof(FAFontIcon);

    public LucideKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }
}

/// <summary>XAML: <c>FAIconSource="{c:LucideSource Folder}"</c> for FANavigationView and FASettingsExpander.</summary>
public sealed class LucideSourceExtension(LucideKind kind) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new FAFontIconSource { FontFamily = Lucide.Font, Glyph = Lucide.Glyph(kind) };
}
