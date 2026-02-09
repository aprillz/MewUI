namespace Aprillz.MewUI;

/// <summary>
/// Represents a resolved theme (palette + metrics) used for rendering and styling controls.
/// </summary>
public record class Theme
{
    /// <summary>
    /// Gets the theme name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the computed palette (colors).
    /// </summary>
    public required Palette Palette { get; init; }

    /// <summary>
    /// Gets the theme metrics (sizes, thicknesses, radii).
    /// </summary>
    public required ThemeMetrics Metrics { get; init; }

    /// <summary>
    /// Gets whether this theme is considered dark.
    /// </summary>
    public bool IsDark => Palette.IsDarkBackground(Palette.WindowBackground);
}

/// <summary>
/// Theme selection mode.
/// </summary>
public enum ThemeVariant
{
    /// <summary>Use the OS theme variant.</summary>
    System,
    /// <summary>Force light theme.</summary>
    Light,
    /// <summary>Force dark theme.</summary>
    Dark
}
