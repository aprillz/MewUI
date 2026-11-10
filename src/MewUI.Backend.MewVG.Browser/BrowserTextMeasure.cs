using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

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

    // A prefix set costs a measurement per code unit, so sets of short runs are kept per font.
    private static readonly Dictionary<string, Dictionary<string, double[]>> _prefixAdvances =
        new(StringComparer.Ordinal);

    private static int _prefixAdvanceCount;

    private const int MAX_CACHED_PREFIX_LENGTH = 1024;
    private const int MAX_CACHED_PREFIX_SETS = 2048;

    // The spacing the canvas was last told to use. It keeps it for whatever is measured or drawn next,
    // so every call that reaches the canvas states its own first.
    private static double _canvasLetterSpacing;

    /// <summary>
    /// Makes the canvas measure and draw the runs that follow with <paramref name="letterSpacing"/>
    /// device-independent units after every character.
    /// </summary>
    internal static void UseLetterSpacing(double letterSpacing)
    {
        if (letterSpacing != _canvasLetterSpacing)
        {
            BrowserNative.SetTextLetterSpacing(0, (float)letterSpacing);
            _canvasLetterSpacing = letterSpacing;
        }
    }

    /// <summary>The key the measurement caches file a font under: spaced runs measure differently.</summary>
    private static string CacheKeyFor(string cssFont, double letterSpacing)
        => letterSpacing == 0
            ? cssFont
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{cssFont}|{letterSpacing:R}");

    // The managed text engine owns line breaking and hands this backend one run at a time, so the
    // width limit never has to be honoured here.
    internal static Size Measure(ReadOnlySpan<char> text, IFont font, double maxWidth, double letterSpacing = 0)
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
            widest = Math.Max(widest, MeasureLine(text[start..end], cssFont, letterSpacing));
            lines++;
            start = index + 1;
        }

        return new Size(widest, lineHeight * Math.Max(1, lines));
    }

    /// <summary>Ink of one run past its advance box and the font's ascent/descent band, in device-independent units.</summary>
    internal static unsafe TextInkOverhang MeasureInk(ReadOnlySpan<char> text, IFont font, double letterSpacing = 0)
    {
        if (text.IsEmpty)
        {
            return TextInkOverhang.None;
        }

        double* horizontal = stackalloc double[2];
        double* vertical = stackalloc double[2];
        UseLetterSpacing(letterSpacing);
        BrowserNative.MeasureInkExtent(text.ToString(), BrowserFont.CssFontFor(font), horizontal, vertical);
        return TextInkOverhang.FromEdges(horizontal[0], vertical[0], horizontal[1], vertical[1]);
    }

    internal static double MeasureLine(ReadOnlySpan<char> line, string cssFont, double letterSpacing = 0)
    {
        if (line.IsEmpty)
        {
            return 0;
        }

        string cacheKey = CacheKeyFor(cssFont, letterSpacing);
        if (!_widths.TryGetValue(cacheKey, out var forFont))
        {
            forFont = new Dictionary<string, double>(StringComparer.Ordinal);
            _widths[cacheKey] = forFont;
        }

        var lookup = forFont.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(line, out var cached))
        {
            return cached;
        }

        var content = line.ToString();
        UseLetterSpacing(letterSpacing);
        var width = BrowserNative.MeasureText(content, cssFont, out _, out _);
        if (_widthCount > 4096)
        {
            _widths.Clear();
            _widthCount = 0;
            forFont = new Dictionary<string, double>(StringComparer.Ordinal);
            _widths[cacheKey] = forFont;
        }

        forFont[content] = width;
        _widthCount++;
        return width;
    }

    /// <summary>Writes the advance after every UTF-16 code unit of the run, laid out as the rasterizer draws it.</summary>
    internal static void FillPrefixAdvances(ReadOnlySpan<char> text, IFont font, Span<double> destination, double letterSpacing = 0)
    {
        if (text.IsEmpty)
        {
            return;
        }

        var cssFont = BrowserFont.CssFontFor(font);
        string cacheKey = CacheKeyFor(cssFont, letterSpacing);
        if (!_prefixAdvances.TryGetValue(cacheKey, out var forFont))
        {
            forFont = new Dictionary<string, double[]>(StringComparer.Ordinal);
            _prefixAdvances[cacheKey] = forFont;
        }

        var lookup = forFont.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(text, out var cached))
        {
            cached.AsSpan().CopyTo(destination);
            return;
        }

        var content = text.ToString();
        var advances = new double[content.Length];
        UseLetterSpacing(letterSpacing);
        MeasurePrefixAdvances(content, cssFont, advances);
        advances.AsSpan().CopyTo(destination);

        if (content.Length > MAX_CACHED_PREFIX_LENGTH)
        {
            return;
        }

        if (_prefixAdvanceCount > MAX_CACHED_PREFIX_SETS)
        {
            _prefixAdvances.Clear();
            _prefixAdvanceCount = 0;
            forFont = new Dictionary<string, double[]>(StringComparer.Ordinal);
            _prefixAdvances[cacheKey] = forFont;
        }

        forFont[content] = advances;
        _prefixAdvanceCount++;
    }

    private static unsafe void MeasurePrefixAdvances(string text, string cssFont, double[] advances)
    {
        double capacity = advances.Length;
        fixed (double* output = advances)
        {
            BrowserNative.MeasurePrefixAdvances(text, cssFont, output, &capacity);
        }
    }

}
