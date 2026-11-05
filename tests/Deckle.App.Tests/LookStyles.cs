using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Deckle.Core.Settings;

namespace Deckle.App.Tests;

/// <summary>
/// The test app starts in the Windows look, so App.ApplyLook never loads the other looks' styles; tests that
/// render a look add them to their window.
/// </summary>
internal static class LookStyles
{
    public static void AddTo(Window window, AppLook look)
    {
        foreach (var file in App.LookStyles(look))
        {
            window.Styles.Add(new StyleInclude(new Uri("avares://Deckle/")) { Source = new Uri($"avares://Deckle/{file}") });
        }
    }
}
