using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls.Text;

internal sealed class TextBoxView
{
    private readonly record struct MeasureFontKey(string FontFamily, double FontSize, FontWeight FontWeight, uint Dpi);

    private int _cacheVersion = -1;
    private MeasureFontKey _cacheFontKey;
    private char[]? _cacheText;
    private int _cacheTextLength;

    // Chunk-level: absolute prefix width at each chunk boundary.
    private int _chunkSize;
    private double[]? _chunkPrefixWidths;   // [chunkCount+1] — _chunkPrefixWidths[i] = width of text[0 .. i*chunkSize)
    private double[]? _chunkKerningAdjust;  // kerning correction at each chunk boundary
    private double _totalWidth;

    // Per-chunk character positions (lazily computed).
    // _charPositions[chunk] = double[chunkLen+1] where [j] = absolute X of text[chunkStart + j].
    private double[]?[]? _charPositions;

    public void Render(
        IGraphicsContext context,
        Rect contentBounds,
        IFont font,
        Theme theme,
        bool isEnabled,
        bool isFocused,
        bool isReadOnly,
        Color foreground,
        double horizontalOffset,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight,
        uint dpi,
        int documentVersion,
        int caretPosition,
        bool hasSelection,
        int selectionStart,
        int selectionEnd,
        int textLength,
        int compositionStart,
        int compositionLength,
        Action<char[], int, int> copyTextTo)
    {
        if (textLength <= 0)
        {
            if (isFocused && !isReadOnly)
            {
                double emptyLineHeight = context.MeasureText("M", font).Height;
                double emptyLineTop = contentBounds.Y + (contentBounds.Height - emptyLineHeight) / 2;
                var caretX = contentBounds.X - horizontalOffset;
                context.DrawLine(
                    new Point(caretX, emptyLineTop + 2),
                    new Point(caretX, emptyLineTop + emptyLineHeight - 2),
                    theme.Palette.WindowText, 1, pixelSnap: true);
            }

            return;
        }

        EnsureCache(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, copyTextTo);
        var text = _cacheText!.AsSpan(0, _cacheTextLength);

        double lineHeight = context.MeasureText("M", font).Height;
        double lineTop = contentBounds.Y + (contentBounds.Height - lineHeight) / 2;

        double xFrom = Math.Max(0, horizontalOffset);
        double xTo = xFrom + Math.Max(0, contentBounds.Width);

        int startCol = CharIndexFromX(xFrom, context, font);
        int endCol = CharIndexFromX(xTo, context, font);
        if (endCol < startCol) endCol = startCol;
        endCol = Math.Min(text.Length, endCol + 2);

        double prefixWidthStart = startCol <= 0 ? 0 : GetAbsoluteX(startCol, context, font);
        double drawX = contentBounds.X - horizontalOffset + prefixWidthStart;

        var visible = text[startCol..endCol];

        if (hasSelection)
        {
            int s = Math.Max(selectionStart, startCol);
            int t = Math.Min(selectionEnd, endCol);
            if (s < t)
            {
                double beforeW = GetAbsoluteX(s, context, font) - prefixWidthStart;
                double selW = GetAbsoluteX(t, context, font) - GetAbsoluteX(s, context, font);
                if (selW > 0)
                {
                    context.FillRectangle(new Rect(drawX + beforeW, lineTop, selW, lineHeight), theme.Palette.SelectionBackground);
                }
            }
        }

        var textColor = isEnabled ? foreground : theme.Palette.DisabledText;
        context.DrawText(visible, new Rect(drawX, contentBounds.Y, 1_000_000, contentBounds.Height), font, textColor,
            TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

        // Composition underline (dashed)
        if (compositionLength > 0)
        {
            int cs = Math.Max(compositionStart, startCol);
            int ce = Math.Min(compositionStart + compositionLength, endCol);
            if (cs < ce)
            {
                double ulX1 = drawX + GetAbsoluteX(cs, context, font) - prefixWidthStart;
                double ulX2 = drawX + GetAbsoluteX(ce, context, font) - prefixWidthStart;
                double ulY = lineTop + lineHeight - 2;
                DrawDashedLine(context, ulX1, ulX2, ulY, textColor);
            }
        }

        if (isFocused && !isReadOnly)
        {
            int caret = Math.Clamp(caretPosition, 0, text.Length);
            if (caret >= startCol && caret <= endCol)
            {
                double caretX = contentBounds.X - horizontalOffset + GetAbsoluteX(caret, context, font);
                context.DrawLine(
                    new Point(caretX, lineTop + 2),
                    new Point(caretX, lineTop + lineHeight - 2),
                    theme.Palette.WindowText, 1, pixelSnap: true);
            }
        }
    }

    /// <summary>
    /// Draws a dashed horizontal line for IME composition underline.
    /// </summary>
    internal static void DrawDashedLine(IGraphicsContext context, double x1, double x2, double y, Color color)
    {
        const double dash = 3;
        const double gap = 2;
        double x = x1;
        while (x < x2)
        {
            double end = Math.Min(x + dash, x2);
            context.DrawLine(new Point(x, y), new Point(end, y), color, 1, pixelSnap: true);
            x = end + gap;
        }
    }

    public int GetCaretIndexFromX(
        double x,
        IGraphicsContext context,
        IFont font,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight,
        uint dpi,
        int documentVersion,
        int textLength,
        Action<char[], int, int> copyTextTo)
    {
        if (textLength <= 0) return 0;

        EnsureCache(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, copyTextTo);
        if (x <= 0) return 0;

        int idx = CharIndexFromX(x, context, font);
        idx = Math.Clamp(idx, 0, _cacheTextLength);
        if (idx <= 0) return 0;

        double w0 = GetAbsoluteX(idx - 1, context, font);
        double w1 = GetAbsoluteX(idx, context, font);
        return x < (w0 + w1) / 2 ? idx - 1 : idx;
    }

    public double EnsureCaretVisible(
        IGraphicsContext context,
        IFont font,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight,
        uint dpi,
        int documentVersion,
        int textLength,
        int caretPosition,
        double horizontalOffset,
        double viewportWidthDip,
        double endGutterDip,
        Action<char[], int, int> copyTextTo)
    {
        if (textLength <= 0) return 0;

        EnsureCache(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, copyTextTo);
        double caretX = GetAbsoluteX(caretPosition, context, font);

        double newOffset = horizontalOffset;
        if (caretX - newOffset > viewportWidthDip - 5)
            newOffset = caretX - viewportWidthDip + 10;
        else if (caretX - newOffset < 5)
            newOffset = Math.Max(0, caretX - 10);

        return ClampScrollOffset(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, newOffset, viewportWidthDip, endGutterDip, copyTextTo);
    }

    public double ClampScrollOffset(
        IGraphicsContext context,
        IFont font,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight,
        uint dpi,
        int documentVersion,
        int textLength,
        double horizontalOffset,
        double viewportWidthDip,
        double endGutterDip,
        Action<char[], int, int> copyTextTo)
    {
        if (textLength <= 0) return 0;

        EnsureCache(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, copyTextTo);
        double maxOffset = Math.Max(0, _totalWidth - Math.Max(0, viewportWidthDip) + Math.Max(0, endGutterDip));
        return Math.Clamp(horizontalOffset, 0, maxOffset);
    }

    public double GetTextWidthDip(
        IGraphicsContext context,
        IFont font,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight,
        uint dpi,
        int documentVersion,
        int textLength,
        Action<char[], int, int> copyTextTo)
    {
        if (textLength <= 0) return 0;

        EnsureCache(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, copyTextTo);
        return _totalWidth;
    }

    #region Cache

    /// <summary>
    /// Build chunk-level prefix width cache. Per-character positions are computed lazily.
    /// </summary>
    internal void EnsureCache(
        IGraphicsContext context,
        IFont font,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight,
        uint dpi,
        int version,
        int length,
        Action<char[], int, int> copyTo)
    {
        var key = new MeasureFontKey(fontFamily, fontSize, fontWeight, dpi);
        if (_cacheText != null &&
            _cacheVersion == version &&
            _cacheFontKey == key &&
            _cacheTextLength == length &&
            _chunkPrefixWidths != null)
        {
            return;
        }

        _cacheVersion = version;
        _cacheFontKey = key;
        _cacheTextLength = length;

        if (length <= 0)
        {
            _totalWidth = 0;
            _chunkSize = 64;
            return;
        }

        // Reuse text buffer if large enough.
        if (_cacheText == null || _cacheText.Length < length)
            _cacheText = new char[length];
        copyTo(_cacheText, 0, length);
        var span = _cacheText.AsSpan(0, length);

        // Adaptive chunk size: target ≤256 chunks.
        int cs = length switch
        {
            <= 4096 => 64,
            <= 32768 => 256,
            <= 262144 => 1024,
            _ => 4096,
        };
        _chunkSize = cs;

        int chunkCount = (length + cs - 1) / cs;
        int requiredLen = chunkCount + 1;

        // Reuse chunk-level arrays if large enough.
        if (_chunkPrefixWidths == null || _chunkPrefixWidths.Length < requiredLen)
            _chunkPrefixWidths = new double[requiredLen];
        if (_chunkKerningAdjust == null || _chunkKerningAdjust.Length < requiredLen)
            _chunkKerningAdjust = new double[requiredLen];

        var prefixWidths = _chunkPrefixWidths;
        var kerningAdjust = _chunkKerningAdjust;

        kerningAdjust[0] = 0;
        for (int i = 1; i <= chunkCount; i++)
        {
            int idx = i * cs;
            if (idx <= 0 || idx >= length)
            {
                kerningAdjust[i] = 0;
                continue;
            }

            double prevW = context.MeasureText(span.Slice(idx - 1, 1), font).Width;
            double firstW = context.MeasureText(span.Slice(idx, 1), font).Width;
            double pairW = context.MeasureText(span.Slice(idx - 1, 2), font).Width;
            kerningAdjust[i] = pairW - prevW - firstW;
        }

        prefixWidths[0] = 0;
        for (int i = 1; i <= chunkCount; i++)
        {
            int chunkStart = (i - 1) * cs;
            int chunkEnd = Math.Min(length, i * cs);
            double chunkW = chunkEnd <= chunkStart ? 0 : context.MeasureText(span.Slice(chunkStart, chunkEnd - chunkStart), font).Width;
            double adjust = i <= 1 ? 0 : kerningAdjust[i - 1];
            prefixWidths[i] = prefixWidths[i - 1] + chunkW + adjust;
        }

        _totalWidth = prefixWidths[chunkCount];

        // Reuse per-chunk position array, clear stale entries.
        if (_charPositions == null || _charPositions.Length < chunkCount)
            _charPositions = new double[chunkCount][];
        else
            Array.Clear(_charPositions, 0, Math.Min(_charPositions.Length, chunkCount));
    }

    /// <summary>
    /// Lazily compute per-character absolute X positions for a chunk.
    /// After this, lookups within the chunk are O(1) array access.
    /// </summary>
    private double[] EnsureChunkCharPositions(int chunkIndex, IGraphicsContext context, IFont font)
    {
        var positions = _charPositions![chunkIndex];
        if (positions != null) return positions;

        var span = _cacheText!.AsSpan(0, _cacheTextLength);
        int cs = _chunkSize;
        int chunkStart = chunkIndex * cs;
        int chunkEnd = Math.Min(_cacheTextLength, chunkStart + cs);
        int chunkLen = chunkEnd - chunkStart;

        double baseX = _chunkPrefixWidths![chunkIndex];
        double adjust = chunkStart > 0 ? _chunkKerningAdjust![chunkIndex] : 0;

        // positions[j] = absolute X of character at (chunkStart + j), j in [0..chunkLen].
        positions = new double[chunkLen + 1];
        positions[0] = baseX;

        for (int j = 1; j <= chunkLen; j++)
        {
            double w = context.MeasureText(span.Slice(chunkStart, j), font).Width;
            positions[j] = baseX + w + adjust;
        }

        _charPositions![chunkIndex] = positions;
        return positions;
    }

    /// <summary>
    /// Get absolute X position of character at given index. O(1) after chunk is cached.
    /// </summary>
    internal double GetAbsoluteX(int index, IGraphicsContext context, IFont font)
    {
        index = Math.Clamp(index, 0, _cacheTextLength);
        if (index <= 0) return 0;

        int cs = _chunkSize;
        int chunkIndex = Math.Min(_chunkPrefixWidths!.Length - 2, index / cs);
        int chunkStart = chunkIndex * cs;

        // Exact chunk boundary — use prefix width directly.
        if (index == chunkStart) return _chunkPrefixWidths[chunkIndex];

        var positions = EnsureChunkCharPositions(chunkIndex, context, font);
        int localIndex = index - chunkStart;
        return localIndex < positions.Length ? positions[localIndex] : _chunkPrefixWidths[chunkIndex + 1];
    }

    /// <summary>
    /// Find character index from absolute X coordinate.
    /// Binary search on chunks (O(log chunks)), then binary search within chunk using cached positions (O(log chunkSize), no MeasureText).
    /// </summary>
    private int CharIndexFromX(double x, IGraphicsContext context, IFont font)
    {
        if (_cacheTextLength <= 0) return 0;
        if (x <= 0) return 0;
        if (x >= _totalWidth) return _cacheTextLength;

        var prefixWidths = _chunkPrefixWidths!;
        int chunkCount = prefixWidths.Length - 1;

        // Binary search for chunk.
        int loChunk = 0, hiChunk = chunkCount;
        while (loChunk < hiChunk)
        {
            int mid = (loChunk + hiChunk + 1) / 2;
            if (prefixWidths[mid] <= x)
                loChunk = mid;
            else
                hiChunk = mid - 1;
        }

        int chunkIndex = loChunk;
        var positions = EnsureChunkCharPositions(chunkIndex, context, font);

        // Binary search within chunk using cached positions — no MeasureText calls.
        int lo = 0, hi = positions.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (positions[mid] < x)
                lo = mid + 1;
            else
                hi = mid;
        }

        return Math.Clamp(chunkIndex * _chunkSize + lo, 0, _cacheTextLength);
    }

    #endregion
}
