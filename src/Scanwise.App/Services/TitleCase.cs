using Avalonia;
using Avalonia.Controls;

namespace Scanwise.App.Services;

/// <summary>
/// macOS menus use title case ("Save Selected Pages As…"); the app's menus are written in sentence case for
/// Windows and Linux. On macOS every menu item header is converted as it is set.
/// </summary>
public static class TitleCase
{
    // Apple's style: articles, coordinating conjunctions and prepositions of four letters or fewer stay lowercase
    // unless they start or end the title.
    private static readonly HashSet<string> Minor = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "but", "or", "nor", "for", "so", "yet",
        "as", "at", "by", "in", "into", "of", "on", "onto", "to", "up", "via", "with", "from", "per", "than",
    };

    public static void ApplyToMenus() =>
        MenuItem.HeaderProperty.Changed.AddClassHandler<MenuItem>((item, _) =>
        {
            if (item.Header is string header && Convert(header) is var titled && titled != header)
            {
                item.Header = titled;
            }
        });

    public static string Convert(string text)
    {
        var words = text.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var start = word.TakeWhile(c => !char.IsLetter(c)).Count();
            if (start >= word.Length || char.IsUpper(word[start]))
            {
                continue; // No letters, or already capitalised (also keeps "PDF", "TIFF" and "Ctrl").
            }
            var core = new string(word.Skip(start).TakeWhile(char.IsLetter).ToArray());
            var first = i == 0 || words[i - 1].EndsWith(':') || word[..start].Contains('(');
            var last = i == words.Length - 1;
            if (!first && !last && Minor.Contains(core))
            {
                continue;
            }
            words[i] = word[..start] + char.ToUpperInvariant(word[start]) + word[(start + 1)..];
        }
        return string.Join(' ', words);
    }
}
