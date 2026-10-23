namespace Aprillz.MewUI.Headless;

/// <summary>
/// Settings a headless session is started with; the instance is read once by <see cref="HeadlessSession.Start"/>.
/// </summary>
public sealed class HeadlessOptions
{
    /// <summary>Gets or sets whether frames are discarded or drawn offscreen; the default is <see cref="HeadlessRendering.MeasureOnly"/>.</summary>
    public HeadlessRendering Rendering { get; set; } = HeadlessRendering.MeasureOnly;

    /// <summary>Gets or sets the DPI every window reports; the default is 96.</summary>
    public uint Dpi { get; set; } = 96;

    /// <summary>Gets or sets the clock timers run against; the default is <see cref="HeadlessClock.Virtual"/>.</summary>
    public HeadlessClock Clock { get; set; } = HeadlessClock.Virtual;

    /// <summary>
    /// Gets or sets the font family themes resolve as the system font, or null for the operating
    /// system's default.
    /// </summary>
    public string? DefaultFontFamily { get; set; }

    /// <summary>Gets or sets the system theme variant the session reports; the default is <see cref="ThemeVariant.Light"/>.</summary>
    public ThemeVariant SystemTheme { get; set; } = ThemeVariant.Light;
}
