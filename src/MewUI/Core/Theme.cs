namespace Aprillz.MewUI;

public record class Theme
{
    public required string Name { get; init; }

    public required Palette Palette { get; init; }

    public required ThemeMetrics Metrics { get; init; }

    public bool IsDark => Palette.IsDarkBackground(Palette.WindowBackground);
}

public enum ThemeVariant
{
    System,
    Light,
    Dark
}
