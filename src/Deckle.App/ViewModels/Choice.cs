namespace Deckle.App.ViewModels;

/// <summary>A value with a display label for combo boxes and radio lists.</summary>
public sealed record Choice<T>(T Value, string Label, string? Description = null)
{
    public override string ToString() => Label;
}
