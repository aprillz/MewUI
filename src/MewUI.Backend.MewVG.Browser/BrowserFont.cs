using System.Globalization;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Font backed by the browser's own font stack. Metrics come from a Canvas2D measurement of the
/// equivalent CSS font, so the family string is resolved by the browser rather than by MewUI.
/// </summary>
internal sealed class BrowserFont : FontBase
{
    // Metrics cost a JS call and a text measurement, and depend only on the CSS font string.
    private static readonly Dictionary<string, (double Ascent, double Descent)> _metrics = new(StringComparer.Ordinal);

    internal BrowserFont(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
        : base(family, size, weight, italic, underline, strikethrough)
    {
        CssFont = BuildCssFont(family, size, weight, italic);

        var (ascent, descent) = ResolveMetrics(CssFont);
        Ascent = ascent > 0 ? ascent : size;
        Descent = descent > 0 ? descent : size * 0.25;
        CapHeight = Ascent * 0.7;
    }

    /// <summary>The CSS <c>font</c> shorthand this font maps to.</summary>
    internal string CssFont { get; }

    private static (double Ascent, double Descent) ResolveMetrics(string cssFont)
    {
        if (_metrics.TryGetValue(cssFont, out var cached))
        {
            return cached;
        }

        // "Mg" covers a tall ascender and a descender so the browser reports the full box.
        BrowserNative.MeasureText("Mg", cssFont, out var ascent, out var descent);
        var resolved = (ascent, descent);
        _metrics[cssFont] = resolved;
        return resolved;
    }

    /// <summary>CSS font shorthand for any font, including ones this backend did not create.</summary>
    internal static string CssFontFor(IFont font)
        => font is BrowserFont browserFont
            ? browserFont.CssFont
            : BuildCssFont(font.Family, font.Size, font.Weight, font.IsItalic);

    private static string BuildCssFont(string family, double size, FontWeight weight, bool italic)
    {
        var style = italic ? "italic " : string.Empty;
        var weightValue = ((int)weight).ToString(CultureInfo.InvariantCulture);
        var sizeValue = size.ToString("0.###", CultureInfo.InvariantCulture);
        return $"{style}{weightValue} {sizeValue}px {QuoteFamily(family)}, sans-serif";
    }

    private static string QuoteFamily(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return "sans-serif";
        }

        // A comma-separated request is already a CSS family list; pass it through untouched.
        return family.Contains(',') ? family : $"\"{family}\"";
    }
}
