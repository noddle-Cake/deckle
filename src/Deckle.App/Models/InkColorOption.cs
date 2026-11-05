using Avalonia.Media;

namespace Deckle.App.Models;

public sealed record InkColorOption(Color Value, string Label)
{
    public IBrush Brush { get; } = new SolidColorBrush(Value);
}
