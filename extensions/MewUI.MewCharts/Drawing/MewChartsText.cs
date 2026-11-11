using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewCharts.Drawing;

/// <summary>Ambient text layouts for chart geometries, available outside a frame.</summary>
/// <remarks>
/// LiveCharts measures label geometries without a frame context, so layouts come from the graphics
/// factory captured by <see cref="EnsureInitialized"/> when the first chart attaches to a window.
/// </remarks>
public static class MewChartsText
{
    private static readonly object _lock = new();
    private static IGraphicsFactory? _factory;

    /// <summary>Gets or sets the font family for chart text; defaults to the system font family, and each chart assigns its inherited <see cref="Controls.TextElement.FontFamily"/> before drawing.</summary>
    public static string FontFamily { get; set; } = ThemeMetrics.SystemFontFamily;

    /// <summary>
    /// Gets or sets the multiplier applied to every chart text size; defaults to 1, and each chart assigns
    /// its inherited <see cref="Controls.TextElement.FontSize"/> divided by the theme's 16 reference size before drawing.
    /// </summary>
    public static double FontScale { get; set; } = 1;

    /// <summary>Captures the graphics factory used to build text layouts; the first factory wins and later calls are ignored.</summary>
    public static void EnsureInitialized(IGraphicsFactory factory)
    {
        if (_factory is not null) return;
        lock (_lock)
        {
            if (_factory is not null) return;
            _factory = factory;
        }
    }

    /// <summary>Gets a cached layout of the text at the given size, or <see langword="null"/> before <see cref="EnsureInitialized"/> has run or when the text is empty.</summary>
    public static ITextLayout? GetLayout(string text, float size) => GetLayout(text, size, null, float.MaxValue);

    /// <summary>
    /// Gets a cached layout of the text at the given size in the font of <paramref name="paint"/>, falling back to the
    /// chart font for unset values, wrapped at word boundaries to <paramref name="maxWidth"/> DIPs when it is finite;
    /// <see langword="null"/> before <see cref="EnsureInitialized"/> has run or when the text is empty.
    /// </summary>
    public static ITextLayout? GetLayout(string text, float size, Painting.MewPaint? paint, float maxWidth)
    {
        if (_factory is null || string.IsNullOrEmpty(text)) return null;
        var family = paint?.FontFamily;
        if (string.IsNullOrEmpty(family)) family = string.IsNullOrEmpty(FontFamily) ? ThemeManager.DefaultMetrics.FontFamily : FontFamily;
        var scaled = (float)Math.Max(1, size * FontScale);
        lock (_lock)
        {
            return _factory.TextEngine.GetOrCreateLayout(
                new TextLayoutRequest
                {
                    Text = text.AsMemory(),
                    Dpi = 96,
                    DefaultStyle = new TextRunStyle(family, scaled, paint?.FontWeight ?? FontWeight.Normal, paint?.Italic ?? false),
                    Paragraph = maxWidth > 0 && maxWidth < float.MaxValue
                        ? new TextParagraphStyle { MaxWidth = maxWidth, Wrapping = TextWrapping.Wrap }
                        : new TextParagraphStyle(),
                },
                TextLayoutCachePolicy.Content);
        }
    }

    /// <summary>Measures the text at the given size; returns <see cref="Size.Empty"/> before <see cref="EnsureInitialized"/> has run.</summary>
    public static Size Measure(string text, float size)
    {
        return GetLayout(text, size)?.MeasuredSize ?? Size.Empty;
    }

    /// <summary>Measures the text at the given size in the font of <paramref name="paint"/>, wrapped to <paramref name="maxWidth"/> DIPs when it is finite; returns <see cref="Size.Empty"/> before <see cref="EnsureInitialized"/> has run.</summary>
    public static Size Measure(string text, float size, Painting.MewPaint? paint, float maxWidth)
    {
        return GetLayout(text, size, paint, maxWidth)?.MeasuredSize ?? Size.Empty;
    }
}
