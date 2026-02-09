using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls.Text;

internal sealed class TextWrapVirtualizer
{
    internal delegate void LineSpanProvider(int lineIndex, out int start, out int end);
    internal delegate string LineTextProvider(int lineIndex, int start, int end);

    internal readonly record struct WrapLayout(int Version, double Width, int[] SegmentStarts);
    private readonly record struct WrapAnchor(int LineIndex, int StartRow);

    private const int LargeDocumentThreshold = 1000;

    private readonly LineSpanProvider _getLineSpan;
    private readonly LineTextProvider _getLineText;
    private readonly Func<int> _getDocumentVersion;
    private readonly Func<int> _getLineCount;
    private readonly Func<int> _getTextLength;
    private readonly int _wrapSegmentHardLimit;

    private readonly Dictionary<int, WrapLayout> _wrapCache = new();
    private readonly List<WrapAnchor> _wrapAnchors = new();
    private readonly List<int> _wrapRowPrefix = new();
    private int _wrapRowPrefixVersion = -1;
    private double _wrapWidthForPrefix;
    private double _wrapWidthForAnchors;

    private double _cachedExtentHeight;
    private int _cachedExtentHeightVersion = -1;
    private double _cachedExtentHeightWrapWidth;

    private double _avgLineLength;
    private int _avgLineLengthVersion = -1;

    public TextWrapVirtualizer(
        LineSpanProvider getLineSpan,
        LineTextProvider getLineText,
        Func<int> getDocumentVersion,
        Func<int> getLineCount,
        Func<int> getTextLength,
        int wrapSegmentHardLimit)
    {
        _getLineSpan = getLineSpan;
        _getLineText = getLineText;
        _getDocumentVersion = getDocumentVersion;
        _getLineCount = getLineCount;
        _getTextLength = getTextLength;
        _wrapSegmentHardLimit = wrapSegmentHardLimit;
    }

    public void Reset()
    {
        _wrapCache.Clear();
        _wrapAnchors.Clear();
        _wrapRowPrefix.Clear();
        _wrapRowPrefixVersion = -1;
        _wrapWidthForPrefix = 0;
        _wrapWidthForAnchors = 0;
        _cachedExtentHeight = 0;
        _cachedExtentHeightVersion = -1;
        _cachedExtentHeightWrapWidth = 0;
        _avgLineLength = 0;
        _avgLineLengthVersion = -1;
    }

    public double GetExtentHeight(double wrapWidth, double lineHeight, double fontSize)
    {
        int version = _getDocumentVersion();
        if (_cachedExtentHeightVersion == version && Math.Abs(_cachedExtentHeightWrapWidth - wrapWidth) < 0.01)
        {
            return _cachedExtentHeight;
        }

        int totalRows = GetEstimatedTotalVisualRows(wrapWidth, fontSize);
        double height = Math.Max(0, totalRows * lineHeight);

        _cachedExtentHeightVersion = version;
        _cachedExtentHeightWrapWidth = wrapWidth;
        _cachedExtentHeight = height;

        return height;
    }

    public WrapLayout GetWrapLayout(int lineIndex, string lineText, double wrapWidth, IGraphicsContext context, IFont font)
    {
        int version = _getDocumentVersion();
        if (_wrapCache.TryGetValue(lineIndex, out var layout) &&
            layout.Version == version &&
            Math.Abs(layout.Width - wrapWidth) < 0.01)
        {
            return layout;
        }

        var segmentStarts = BuildWrapSegments(lineText, wrapWidth, context, font, _wrapSegmentHardLimit);
        layout = new WrapLayout(version, wrapWidth, segmentStarts);

        int lineCount = _getLineCount();
        int cacheLimit = lineCount > LargeDocumentThreshold ? 2048 : 512;
        if (_wrapCache.Count > cacheLimit)
        {
            var toRemove = new List<int>(_wrapCache.Count / 2);
            foreach (var kv in _wrapCache)
            {
                if (kv.Value.Version != version || Math.Abs(kv.Value.Width - wrapWidth) >= 0.01)
                {
                    toRemove.Add(kv.Key);
                    if (toRemove.Count >= _wrapCache.Count / 2)
                    {
                        break;
                    }
                }
            }

            foreach (var key in toRemove)
            {
                _wrapCache.Remove(key);
            }

            if (_wrapCache.Count > cacheLimit)
            {
                _wrapCache.Clear();
            }
        }

        _wrapCache[lineIndex] = layout;
        return layout;
    }

    public void MapVisualRowToLine(int visualRow, double wrapWidth, IGraphicsContext context, IFont font, out int lineIndex, out int rowInLine)
        => MapVisualRowToLine(visualRow, wrapWidth, context, font, out lineIndex, out rowInLine, out _);

    public void MapVisualRowToLine(int visualRow, double wrapWidth, IGraphicsContext context, IFont font, out int lineIndex, out int rowInLine, out int lineStartRow)
    {
        visualRow = Math.Max(0, visualRow);
        int lineCount = Math.Max(1, _getLineCount());

        if (lineCount > LargeDocumentThreshold)
        {
            MapVisualRowToLineFast(visualRow, wrapWidth, context, font, lineCount, out lineIndex, out rowInLine, out lineStartRow);
            return;
        }

        EnsureWrapRowPrefixCoversRow(visualRow, wrapWidth, context, font, lineCount);
        if (_wrapRowPrefix.Count > 0 && _wrapRowPrefix[^1] <= visualRow)
        {
            lineIndex = Math.Max(0, lineCount - 1);
            rowInLine = 0;
            EnsureWrapRowPrefixCoversLine(lineIndex, wrapWidth, context, font, lineCount);
            lineStartRow = _wrapRowPrefix.Count > lineIndex ? _wrapRowPrefix[lineIndex] : 0;
            return;
        }

        if (_wrapRowPrefix.Count > 1)
        {
            int ub = UpperBound(_wrapRowPrefix, visualRow);
            lineIndex = Math.Clamp(ub - 1, 0, Math.Max(0, lineCount - 1));
            EnsureWrapRowPrefixCoversLine(lineIndex, wrapWidth, context, font, lineCount);
            lineStartRow = _wrapRowPrefix[lineIndex];
            rowInLine = visualRow - lineStartRow;
            return;
        }

        int anchorLine = 0;
        int anchorRow = 0;
        for (int i = _wrapAnchors.Count - 1; i >= 0; i--)
        {
            if (_wrapAnchors[i].StartRow <= visualRow)
            {
                anchorLine = _wrapAnchors[i].LineIndex;
                anchorRow = _wrapAnchors[i].StartRow;
                break;
            }
        }

        int row = anchorRow;
        int line = anchorLine;
        int lastLineStartRow = row;
        while (line < lineCount)
        {
            _getLineSpan(line, out int s, out int e);
            string text = _getLineText(line, s, e);
            int rows = GetWrapLayout(line, text, wrapWidth, context, font).SegmentStarts.Length;
            lastLineStartRow = row;
            if (visualRow < row + rows)
            {
                lineIndex = line;
                rowInLine = visualRow - row;
                lineStartRow = row;
                return;
            }

            row += rows;
            line++;

            if (line % 256 == 0)
            {
                _wrapAnchors.Add(new WrapAnchor(line, row));
            }
        }

        lineIndex = Math.Max(0, lineCount - 1);
        rowInLine = 0;
        lineStartRow = lastLineStartRow;
    }

    public int GetVisualRowStartForLine(int lineIndex, double wrapWidth, IGraphicsContext context, IFont font)
    {
        if (lineIndex <= 0)
        {
            return 0;
        }

        int lineCount = _getLineCount();
        if (lineCount > LargeDocumentThreshold)
        {
            return GetVisualRowStartForLineFast(lineIndex, wrapWidth, context, font);
        }

        EnsureWrapRowPrefixCoversLine(lineIndex, wrapWidth, context, font, Math.Max(1, lineCount));
        if (_wrapRowPrefix.Count > lineIndex)
        {
            return _wrapRowPrefix[lineIndex];
        }

        int anchorLine = 0;
        int anchorRow = 0;
        for (int i = _wrapAnchors.Count - 1; i >= 0; i--)
        {
            if (_wrapAnchors[i].LineIndex <= lineIndex)
            {
                anchorLine = _wrapAnchors[i].LineIndex;
                anchorRow = _wrapAnchors[i].StartRow;
                break;
            }
        }

        int row = anchorRow;
        for (int line = anchorLine; line < lineIndex; line++)
        {
            _getLineSpan(line, out int s, out int e);
            string text = _getLineText(line, s, e);
            row += GetWrapLayout(line, text, wrapWidth, context, font).SegmentStarts.Length;
            if ((line + 1) % 256 == 0)
            {
                _wrapAnchors.Add(new WrapAnchor(line + 1, row));
            }
        }

        return row;
    }

    public static int GetWrapRowFromColumn(WrapLayout layout, int col)
    {
        for (int i = layout.SegmentStarts.Length - 1; i >= 0; i--)
        {
            if (layout.SegmentStarts[i] <= col)
            {
                return i;
            }
        }
        return 0;
    }

    private static int[] BuildWrapSegments(ReadOnlySpan<char> text, double wrapWidth, IGraphicsContext context, IFont font, int hardLimit)
    {
        if (text.IsEmpty)
        {
            return [0];
        }

        var segments = new List<int>(8) { 0 };
        int start = 0;
        while (start < text.Length)
        {
            if (segments.Count >= hardLimit)
            {
                break;
            }

            int end = FindWrapEnd(text, start, wrapWidth, context, font);
            if (end <= start)
            {
                end = start + 1;
            }

            start = end;

            if (start < text.Length)
            {
                segments.Add(start);
            }
        }

        return segments.ToArray();
    }

    private static int FindWrapEnd(ReadOnlySpan<char> text, int start, double wrapWidth, IGraphicsContext context, IFont font)
    {
        int min = start + 1;
        int max = text.Length;
        int best = min;

        if (context.MeasureText(text[start..], font).Width <= wrapWidth)
        {
            return max;
        }

        while (min <= max)
        {
            int mid = (min + max) / 2;
            double w = context.MeasureText(text[start..mid], font).Width;
            if (w <= wrapWidth)
            {
                best = mid;
                min = mid + 1;
            }
            else
            {
                max = mid - 1;
            }
        }

        return best;
    }

    private void EnsureWrapRowPrefixInitialized(double wrapWidth)
    {
        int version = _getDocumentVersion();
        if (_wrapRowPrefixVersion == version && Math.Abs(_wrapWidthForPrefix - wrapWidth) < 0.01)
        {
            return;
        }

        _wrapRowPrefix.Clear();
        _wrapRowPrefix.Add(0);
        _wrapRowPrefixVersion = version;
        _wrapWidthForPrefix = wrapWidth;
    }

    private void EnsureWrapRowPrefixCoversRow(int visualRow, double wrapWidth, IGraphicsContext context, IFont font, int lineCount)
    {
        EnsureWrapRowPrefixInitialized(wrapWidth);
        while (_wrapRowPrefix.Count < lineCount + 1 && _wrapRowPrefix[^1] <= visualRow)
        {
            int lineIndex = _wrapRowPrefix.Count - 1;
            _getLineSpan(lineIndex, out int s, out int e);
            string text = _getLineText(lineIndex, s, e);
            int rows = GetWrapLayout(lineIndex, text, wrapWidth, context, font).SegmentStarts.Length;
            _wrapRowPrefix.Add(_wrapRowPrefix[^1] + rows);
        }
    }

    private void EnsureWrapRowPrefixCoversLine(int lineIndex, double wrapWidth, IGraphicsContext context, IFont font, int lineCount)
    {
        EnsureWrapRowPrefixInitialized(wrapWidth);
        lineIndex = Math.Clamp(lineIndex, 0, Math.Max(0, lineCount - 1));
        while (_wrapRowPrefix.Count <= lineIndex && _wrapRowPrefix.Count < lineCount + 1)
        {
            int idx = _wrapRowPrefix.Count - 1;
            _getLineSpan(idx, out int s, out int e);
            string text = _getLineText(idx, s, e);
            int rows = GetWrapLayout(idx, text, wrapWidth, context, font).SegmentStarts.Length;
            _wrapRowPrefix.Add(_wrapRowPrefix[^1] + rows);
        }
    }

    private static int UpperBound(IReadOnlyList<int> values, int value)
    {
        int lo = 0;
        int hi = values.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (values[mid] <= value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void MapVisualRowToLineFast(int visualRow, double wrapWidth, IGraphicsContext context, IFont font, int lineCount, out int lineIndex, out int rowInLine, out int lineStartRow)
    {
        EnsureWrapAnchorsValid(wrapWidth);

        int anchorLine = 0;
        int anchorRow = 0;

        if (_wrapAnchors.Count > 0)
        {
            int lo = 0;
            int hi = _wrapAnchors.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_wrapAnchors[mid].StartRow <= visualRow)
                {
                    anchorLine = _wrapAnchors[mid].LineIndex;
                    anchorRow = _wrapAnchors[mid].StartRow;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
        }

        int row = anchorRow;
        int line = anchorLine;
        int lastLineStartRow = row;
        const int MaxIterations = 512;
        int iterations = 0;

        while (line < lineCount && iterations < MaxIterations)
        {
            _getLineSpan(line, out int s, out int e);
            string text = _getLineText(line, s, e);
            int rows = GetWrapLayout(line, text, wrapWidth, context, font).SegmentStarts.Length;
            lastLineStartRow = row;

            if (visualRow < row + rows)
            {
                lineIndex = line;
                rowInLine = visualRow - row;
                lineStartRow = row;

                if (line > anchorLine + 128 && (_wrapAnchors.Count == 0 || line > _wrapAnchors[^1].LineIndex))
                {
                    _wrapAnchors.Add(new WrapAnchor(line, row));
                }
                return;
            }

            row += rows;
            line++;
            iterations++;

            if (line % 128 == 0 && (_wrapAnchors.Count == 0 || line > _wrapAnchors[^1].LineIndex))
            {
                _wrapAnchors.Add(new WrapAnchor(line, row));
            }
        }

        lineIndex = Math.Min(line, Math.Max(0, lineCount - 1));
        rowInLine = 0;
        lineStartRow = lastLineStartRow;
    }

    private void EnsureWrapAnchorsValid(double wrapWidth)
    {
        if (_wrapAnchors.Count > 0 && Math.Abs(_wrapWidthForAnchors - wrapWidth) >= 0.01)
        {
            _wrapAnchors.Clear();
        }
        _wrapWidthForAnchors = wrapWidth;
    }

    private int GetEstimatedTotalVisualRows(double wrapWidth, double fontSize)
    {
        int lineCount = Math.Max(1, _getLineCount());

        if (lineCount > LargeDocumentThreshold)
        {
            return GetEstimatedTotalVisualRowsFast(wrapWidth, lineCount, fontSize);
        }

        int extra = 0;
        int version = _getDocumentVersion();
        foreach (var kv in _wrapCache)
        {
            if (kv.Value.Version != version)
            {
                continue;
            }

            if (Math.Abs(kv.Value.Width - wrapWidth) >= 0.01)
            {
                continue;
            }

            extra += Math.Max(0, kv.Value.SegmentStarts.Length - 1);
        }

        return lineCount + extra;
    }

    private int GetEstimatedTotalVisualRowsFast(double wrapWidth, int lineCount, double fontSize)
    {
        double avgLen = GetAverageLineLength(lineCount);
        if (avgLen <= 0)
        {
            return lineCount;
        }

        double avgCharWidth = fontSize * 0.55;
        if (avgCharWidth <= 0)
        {
            avgCharWidth = 7;
        }

        double charsPerLine = wrapWidth / avgCharWidth;
        if (charsPerLine <= 0)
        {
            charsPerLine = 80;
        }

        double wrapFactor = Math.Max(1.0, Math.Ceiling(avgLen / charsPerLine));

        int cachedLines = 0;
        int cachedRows = 0;
        int version = _getDocumentVersion();
        foreach (var kv in _wrapCache)
        {
            if (kv.Value.Version != version)
            {
                continue;
            }

            if (Math.Abs(kv.Value.Width - wrapWidth) >= 0.01)
            {
                continue;
            }

            cachedLines++;
            cachedRows += kv.Value.SegmentStarts.Length;
            if (cachedLines >= 100)
            {
                break;
            }
        }

        if (cachedLines >= 10)
        {
            wrapFactor = (double)cachedRows / cachedLines;
        }

        return Math.Max(lineCount, (int)Math.Ceiling(lineCount * wrapFactor));
    }

    private double GetAverageLineLength(int lineCount)
    {
        int version = _getDocumentVersion();
        if (_avgLineLengthVersion == version)
        {
            return _avgLineLength;
        }

        if (lineCount == 0)
        {
            _avgLineLength = 0;
            _avgLineLengthVersion = version;
            return 0;
        }

        int textLen = _getTextLength();
        if (textLen == 0)
        {
            _avgLineLength = 0;
            _avgLineLengthVersion = version;
            return 0;
        }

        _avgLineLength = (double)textLen / lineCount;
        _avgLineLengthVersion = version;
        return _avgLineLength;
    }

    private int GetVisualRowStartForLineFast(int lineIndex, double wrapWidth, IGraphicsContext context, IFont font)
    {
        EnsureWrapAnchorsValid(wrapWidth);

        int anchorLine = 0;
        int anchorRow = 0;

        if (_wrapAnchors.Count > 0)
        {
            int lo = 0;
            int hi = _wrapAnchors.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_wrapAnchors[mid].LineIndex <= lineIndex)
                {
                    anchorLine = _wrapAnchors[mid].LineIndex;
                    anchorRow = _wrapAnchors[mid].StartRow;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
        }

        int row = anchorRow;
        const int MaxIterations = 512;
        int iterations = 0;

        for (int line = anchorLine; line < lineIndex && iterations < MaxIterations; line++, iterations++)
        {
            _getLineSpan(line, out int s, out int e);
            string text = _getLineText(line, s, e);
            row += GetWrapLayout(line, text, wrapWidth, context, font).SegmentStarts.Length;

            if ((line + 1) % 128 == 0 && (_wrapAnchors.Count == 0 || line + 1 > _wrapAnchors[^1].LineIndex))
            {
                _wrapAnchors.Add(new WrapAnchor(line + 1, row));
            }
        }

        return row;
    }
}

