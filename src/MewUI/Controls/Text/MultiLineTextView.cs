using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls.Text;

internal sealed class MultiLineTextView
{
    private const int MeasureChunkSize = 64;

    internal readonly record struct MeasureFontKey(string FontFamily, double FontSize, FontWeight FontWeight, uint Dpi);

    internal sealed record CachedLineMeasure(
        int Version,
        MeasureFontKey FontKey,
        int Start,
        int End,
        string Text,
        double[] PrefixWidths,
        double[] KerningAdjust,
        double TotalWidth);

    private readonly record struct CachedLine(int Version, int Start, int End, string Text);

    private const int LargeDocumentThreshold = 1000;

    private readonly Func<int> _getDocumentVersion;
    private readonly Func<int> _getLineCount;
    private readonly Func<int, int, string> _getSubstring;
    private readonly Func<string> _getFontFamily;
    private readonly Func<double> _getFontSize;
    private readonly Func<FontWeight> _getFontWeight;
    private readonly Func<uint> _getDpi;

    private readonly Dictionary<int, CachedLine> _lineTextCache = new();
    private readonly Dictionary<int, CachedLineMeasure> _lineMeasureCache = new();

    public MultiLineTextView(
        Func<int> getDocumentVersion,
        Func<int> getLineCount,
        Func<int, int, string> getSubstring,
        Func<string> getFontFamily,
        Func<double> getFontSize,
        Func<FontWeight> getFontWeight,
        Func<uint> getDpi)
    {
        _getDocumentVersion = getDocumentVersion ?? throw new ArgumentNullException(nameof(getDocumentVersion));
        _getLineCount = getLineCount ?? throw new ArgumentNullException(nameof(getLineCount));
        _getSubstring = getSubstring ?? throw new ArgumentNullException(nameof(getSubstring));
        _getFontFamily = getFontFamily ?? throw new ArgumentNullException(nameof(getFontFamily));
        _getFontSize = getFontSize ?? throw new ArgumentNullException(nameof(getFontSize));
        _getFontWeight = getFontWeight ?? throw new ArgumentNullException(nameof(getFontWeight));
        _getDpi = getDpi ?? throw new ArgumentNullException(nameof(getDpi));
    }

    public void Reset()
    {
        _lineTextCache.Clear();
        _lineMeasureCache.Clear();
    }

    public string GetLineText(int lineIndex, int start, int end)
    {
        if (end <= start)
        {
            return string.Empty;
        }

        int lineCount = _getLineCount();
        int cacheLimit = lineCount > LargeDocumentThreshold ? 1024 : 256;
        if (_lineTextCache.Count > cacheLimit)
        {
            _lineTextCache.Clear();
        }

        int version = _getDocumentVersion();
        if (_lineTextCache.TryGetValue(lineIndex, out var cached) &&
            cached.Version == version &&
            cached.Start == start &&
            cached.End == end)
        {
            return cached.Text;
        }

        var text = _getSubstring(start, end - start);
        _lineTextCache[lineIndex] = new CachedLine(version, start, end, text);
        return text;
    }

    public CachedLineMeasure EnsureLineMeasureCache(int lineIndex, int start, int end, IGraphicsContext context, IFont font)
    {
        int lineCount = _getLineCount();
        int cacheLimit = lineCount > LargeDocumentThreshold ? 1024 : 256;
        if (_lineMeasureCache.Count > cacheLimit)
        {
            _lineMeasureCache.Clear();
        }

        int version = _getDocumentVersion();
        var key = new MeasureFontKey(_getFontFamily(), _getFontSize(), _getFontWeight(), _getDpi());

        if (_lineMeasureCache.TryGetValue(lineIndex, out var cached) &&
            cached.Version == version &&
            cached.FontKey == key &&
            cached.Start == start &&
            cached.End == end)
        {
            return cached;
        }

        string text = GetLineText(lineIndex, start, end);
        ReadOnlySpan<char> span = text.AsSpan();
        int length = span.Length;

        if (length <= 0)
        {
            var empty = new CachedLineMeasure(version, key, start, end, string.Empty, new[] { 0.0 }, new[] { 0.0 }, 0);
            _lineMeasureCache[lineIndex] = empty;
            return empty;
        }

        int chunkCount = (length + MeasureChunkSize - 1) / MeasureChunkSize;
        var prefixWidths = new double[chunkCount + 1];
        var kerningAdjust = new double[chunkCount + 1];

        prefixWidths[0] = 0;

        kerningAdjust[0] = 0;
        for (int i = 1; i <= chunkCount; i++)
        {
            int idx = i * MeasureChunkSize;
            if (idx <= 0 || idx >= length)
            {
                kerningAdjust[i] = 0;
                continue;
            }

            var prev = span.Slice(idx - 1, 1);
            var first = span.Slice(idx, 1);
            var pair = span.Slice(idx - 1, 2);
            double prevW = context.MeasureText(prev, font).Width;
            double firstW = context.MeasureText(first, font).Width;
            double pairW = context.MeasureText(pair, font).Width;
            kerningAdjust[i] = pairW - prevW - firstW;
        }

        for (int i = 1; i <= chunkCount; i++)
        {
            int chunkStart = (i - 1) * MeasureChunkSize;
            int chunkEnd = Math.Min(length, i * MeasureChunkSize);
            double chunkW = chunkEnd <= chunkStart ? 0 : context.MeasureText(span.Slice(chunkStart, chunkEnd - chunkStart), font).Width;
            double adjust = i <= 1 ? 0 : kerningAdjust[i - 1];
            prefixWidths[i] = prefixWidths[i - 1] + chunkW + adjust;
        }

        var result = new CachedLineMeasure(
            version,
            key,
            start,
            end,
            text,
            prefixWidths,
            kerningAdjust,
            prefixWidths[^1]);

        _lineMeasureCache[lineIndex] = result;
        return result;
    }

    public static int GetCharIndexFromXCached(CachedLineMeasure cache, double x, IGraphicsContext context, IFont font)
    {
        ReadOnlySpan<char> span = cache.Text.AsSpan();
        if (span.IsEmpty)
        {
            return 0;
        }

        if (x <= 0)
        {
            return 0;
        }

        if (x >= cache.TotalWidth)
        {
            return span.Length;
        }

        var prefixWidths = cache.PrefixWidths;
        var kerningAdjust = cache.KerningAdjust;
        int chunkCount = prefixWidths.Length - 1;

        int loChunk = 0;
        int hiChunk = chunkCount;
        while (loChunk < hiChunk)
        {
            int mid = (loChunk + hiChunk + 1) / 2;
            if (prefixWidths[mid] <= x)
            {
                loChunk = mid;
            }
            else
            {
                hiChunk = mid - 1;
            }
        }

        int chunkIndex = loChunk;
        int chunkStart = chunkIndex * MeasureChunkSize;
        int chunkEnd = Math.Min(span.Length, chunkStart + MeasureChunkSize);
        double baseWidth = prefixWidths[chunkIndex];
        double adjust = chunkStart > 0 ? kerningAdjust[chunkIndex] : 0;

        int lo = chunkStart;
        int hi = chunkEnd;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            double w = baseWidth;
            if (mid > chunkStart)
            {
                w += context.MeasureText(span.Slice(chunkStart, mid - chunkStart), font).Width;
                if (adjust != 0)
                {
                    w += adjust;
                }
            }

            if (w < x)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return Math.Clamp(lo, 0, span.Length);
    }

    public static double GetPrefixWidthCached(CachedLineMeasure cache, int index, IGraphicsContext context, IFont font)
    {
        ReadOnlySpan<char> span = cache.Text.AsSpan();
        index = Math.Clamp(index, 0, span.Length);
        if (index <= 0)
        {
            return 0;
        }

        var prefixWidths = cache.PrefixWidths;
        var kerningAdjust = cache.KerningAdjust;

        int chunkIndex = Math.Min(prefixWidths.Length - 1, index / MeasureChunkSize);
        int chunkStart = chunkIndex * MeasureChunkSize;
        double baseWidth = prefixWidths[chunkIndex];
        if (index == chunkStart)
        {
            return baseWidth;
        }

        double extra = context.MeasureText(span.Slice(chunkStart, index - chunkStart), font).Width;
        double adjust = chunkStart > 0 ? kerningAdjust[chunkIndex] : 0;
        return baseWidth + extra + adjust;
    }

    public static double GetSpanWidthCached(CachedLineMeasure cache, int startCol, int endCol, IGraphicsContext context, IFont font)
    {
        if (endCol <= startCol)
        {
            return 0;
        }

        return GetPrefixWidthCached(cache, endCol, context, font) -
               GetPrefixWidthCached(cache, startCol, context, font);
    }
}
