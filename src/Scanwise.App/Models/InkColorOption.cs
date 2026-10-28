using Avalonia.Media;

namespace Scanwise.App.Models;

public sealed record InkColorOption(Color Value, string Label)
{
    public IBrush Brush { get; } = new SolidColorBrush(Value);
}
