using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Text measurement through the browser's Canvas2D metrics, shared by the render and measure contexts.
/// </summary>
internal static class BrowserTextMeasure
{
    // Measuring crosses into JS, and layout re-measures the same runs constantly.
    private static readonly Dictionary<(string Text, string CssFont), double> _widths = new();

    internal static Size Measure(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        double lineHeight = Math.Max(1, font.Ascent + font.Descent);
        if (text.IsEmpty)
        {
            return new Size(0, lineHeight);
        }

        double widest = 0;
        int lines = 0;
        foreach (var range in EnumerateLines(text))
        {
            double lineWidth = MeasureLine(text[range], BrowserFont.CssFontFor(font));
            widest = Math.Max(widest, lineWidth);
            lines++;
        }

        if (double.IsPositiveInfinity(maxWidth) || maxWidth <= 0 || widest <= maxWidth)
        {
            return new Size(widest, lineHeight * Math.Max(1, lines));
        }

        // Wrapped height is approximated from the total advance; the browser does the real
        // line breaking when the run is rasterized.
        int wrapped = Math.Max(1, (int)Math.Ceiling(widest / maxWidth));
        return new Size(maxWidth, lineHeight * Math.Max(lines, wrapped));
    }

    internal static double MeasureLine(ReadOnlySpan<char> line, string cssFont)
    {
        if (line.IsEmpty)
        {
            return 0;
        }

        var content = line.ToString();
        var key = (content, cssFont);
        if (_widths.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var width = BrowserNative.MeasureText(content, cssFont, out _, out _);
        if (_widths.Count > 4096)
        {
            _widths.Clear();
        }

        _widths[key] = width;
        return width;
    }

    private static List<Range> EnumerateLines(ReadOnlySpan<char> text)
    {
        var ranges = new List<Range>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                int end = i > start && text[i - 1] == '\r' ? i - 1 : i;
                ranges.Add(new Range(start, end));
                start = i + 1;
            }
        }

        ranges.Add(new Range(start, text.Length));
        return ranges;
    }
}
