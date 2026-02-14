using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI;

/// <summary>
/// Application-wide defaults applied by <see cref="ApplicationBuilder"/> before starting the message loop.
/// </summary>
public sealed class AppOptions
{
    public PlatformHostKind? Platform { get; set; }

    public GraphicsBackend? GraphicsBackend { get; set; }

    /// <summary>
    /// Gets or sets the default theme mode.
    /// </summary>
    public ThemeVariant? ThemeMode { get; set; }

    /// <summary>
    /// Gets or sets the default accent.
    /// </summary>
    public Accent? Accent { get; set; }

    /// <summary>
    /// Gets or sets the light theme seed palette.
    /// </summary>
    public ThemeSeed? LightSeed { get; set; }

    /// <summary>
    /// Gets or sets the dark theme seed palette.
    /// </summary>
    public ThemeSeed? DarkSeed { get; set; }

    /// <summary>
    /// Gets or sets theme metric overrides.
    /// </summary>
    public ThemeMetrics? Metrics { get; set; }
}
