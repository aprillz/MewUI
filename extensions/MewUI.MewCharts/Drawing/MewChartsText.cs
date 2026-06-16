using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewCharts.Drawing;

/// <summary>
/// Ambient text resources for chart geometries. LiveCharts measures label geometries without a
/// live frame context, so a measurement-only <see cref="IGraphicsContext"/> and a font cache are
/// kept here, initialized once a chart attaches to a graphics factory.
/// </summary>
public static class MewChartsText
{
    private static readonly object _lock = new();
    private static readonly Dictionary<(string Family, float Size), IFont> _fonts = new();
    private static IGraphicsFactory? _factory;
    private static IGraphicsContext? _measure;

    /// <summary>Font family for chart text; set from the chart's (inherited) <c>Control.FontFamily</c>.</summary>
    public static string FontFamily { get; set; } = "Segoe UI";

    /// <summary>
    /// Multiplier applied to every text size, set from the chart's <c>Control.FontSize</c> relative
    /// to the default (12). Lets the theme's per-role sizes scale with the inherited font size.
    /// </summary>
    public static double FontScale { get; set; } = 1;

    /// <summary>Wires the ambient text resources to a graphics factory (idempotent).</summary>
    public static void EnsureInitialized(IGraphicsFactory factory)
    {
        if (_factory is not null) return;
        lock (_lock)
        {
            if (_factory is not null) return;
            _factory = factory;
            _measure = factory.CreateMeasurementContext(96);
        }
    }

    /// <summary>Gets a cached font of the given size, or <see langword="null"/> before init.</summary>
    public static IFont? GetFont(float size)
    {
        if (_factory is null) return null;
        var family = string.IsNullOrEmpty(FontFamily) ? "Segoe UI" : FontFamily;
        var scaled = (float)Math.Max(1, size * FontScale);
        lock (_lock)
        {
            var key = (family, scaled);
            if (_fonts.TryGetValue(key, out var font)) return font;
            font = _factory.CreateFont(family, scaled);
            _fonts[key] = font;
            return font;
        }
    }

    /// <summary>Measures text without a frame context; returns zero size before init.</summary>
    public static Size Measure(string text, float size)
    {
        if (_measure is null || string.IsNullOrEmpty(text)) return Size.Empty;
        var font = GetFont(size);
        if (font is null) return Size.Empty;
        return _measure.MeasureText(text, font);
    }
}
