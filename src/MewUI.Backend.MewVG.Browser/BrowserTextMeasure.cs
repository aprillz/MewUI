using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Text measurement through the browser's Canvas2D metrics, shared by the render and measure contexts.
/// </summary>
internal static class BrowserTextMeasure
{
    // Measuring crosses into JS, and layout re-measures the same runs constantly. The cache is keyed
    // per font so a hit can be looked up straight from the span, without materialising the run.
    private static readonly Dictionary<string, Dictionary<string, double>> _widths =
        new(StringComparer.Ordinal);

    private static int _widthCount;

    // The managed text engine owns line breaking and hands this backend one run at a time, so the
    // width limit never has to be honoured here.
    internal static Size Measure(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        double lineHeight = Math.Max(1, font.Ascent + font.Descent);
        if (text.IsEmpty)
        {
            return new Size(0, lineHeight);
        }

        var cssFont = BrowserFont.CssFontFor(font);
        double widest = 0;
        int lines = 0;
        int start = 0;
        for (int index = 0; index <= text.Length; index++)
        {
            if (index != text.Length && text[index] != '\n')
            {
                continue;
            }

            int end = index > start && text[index - 1] == '\r' ? index - 1 : index;
            widest = Math.Max(widest, MeasureLine(text[start..end], cssFont));
            lines++;
            start = index + 1;
        }

        return new Size(widest, lineHeight * Math.Max(1, lines));
    }

    internal static double MeasureLine(ReadOnlySpan<char> line, string cssFont)
    {
        if (line.IsEmpty)
        {
            return 0;
        }

        if (!_widths.TryGetValue(cssFont, out var forFont))
        {
            forFont = new Dictionary<string, double>(StringComparer.Ordinal);
            _widths[cssFont] = forFont;
        }

        var lookup = forFont.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(line, out var cached))
        {
            return cached;
        }

        var content = line.ToString();
        var width = BrowserNative.MeasureText(content, cssFont, out _, out _);
        if (_widthCount > 4096)
        {
            _widths.Clear();
            _widthCount = 0;
            forFont = new Dictionary<string, double>(StringComparer.Ordinal);
            _widths[cssFont] = forFont;
        }

        forFont[content] = width;
        _widthCount++;
        return width;
    }

}
