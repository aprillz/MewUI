using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls.Text;

internal sealed class TextBoxView
{
    private const int MeasureChunkSize = 64;

    private readonly record struct MeasureFontKey(string FontFamily, double FontSize, FontWeight FontWeight, uint Dpi);

    private int _measureCacheVersion = -1;
    private MeasureFontKey _measureCacheFontKey;
    private char[]? _measureCacheText;
    private int _measureCacheTextLength;
    private double[]? _measureCacheChunkPrefixWidths;
    private double[]? _measureCacheChunkKerningAdjust;
    private double _measureCacheTotalWidth;

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
        Action<char[], int, int> copyTextTo)
    {
        if (textLength <= 0)
        {
            if (isFocused && !isReadOnly)
            {
                var caretX = contentBounds.X - horizontalOffset;
                context.DrawLine(
                    new Point(caretX, contentBounds.Y + 2),
                    new Point(caretX, contentBounds.Bottom - 2),
                    theme.Palette.WindowText, 1);
            }

            return;
        }

        EnsureMeasureCache(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, copyTextTo);
        var text = _measureCacheText!.AsSpan(0, _measureCacheTextLength);

        double xFrom = Math.Max(0, horizontalOffset);
        double xTo = xFrom + Math.Max(0, contentBounds.Width);

        int startCol = GetCharIndexFromXCached(xFrom, context, font);
        int endCol = GetCharIndexFromXCached(xTo, context, font);
        if (endCol < startCol)
        {
            endCol = startCol;
        }

        endCol = Math.Min(text.Length, endCol + 2);

        double prefixWidthStart = startCol <= 0 ? 0 : GetPrefixWidthCached(startCol, context, font);
        double drawX = contentBounds.X - horizontalOffset + prefixWidthStart;

        var visible = text[startCol..endCol];

        if (hasSelection)
        {
            int s = Math.Max(selectionStart, startCol);
            int t = Math.Min(selectionEnd, endCol);
            if (s < t)
            {
                double beforeW = GetPrefixWidthCached(s, context, font) - prefixWidthStart;
                double selW = GetPrefixWidthCached(t, context, font) - GetPrefixWidthCached(s, context, font);
                if (selW > 0)
                {
                    context.FillRectangle(new Rect(drawX + beforeW, contentBounds.Y, selW, contentBounds.Height), theme.Palette.SelectionBackground);
                }
            }
        }

        var textColor = isEnabled ? foreground : theme.Palette.DisabledText;
        context.DrawText(visible, new Rect(drawX, contentBounds.Y, 1_000_000, contentBounds.Height), font, textColor,
            TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

        if (isFocused && !isReadOnly)
        {
            int caret = Math.Clamp(caretPosition, 0, text.Length);
            if (caret >= startCol && caret <= endCol)
            {
                double caretX = contentBounds.X - horizontalOffset + GetPrefixWidthCached(caret, context, font);
                context.DrawLine(
                    new Point(caretX, contentBounds.Y + 2),
                    new Point(caretX, contentBounds.Bottom - 2),
                    theme.Palette.WindowText, 1);
            }
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
        if (textLength <= 0)
        {
            return 0;
        }

        EnsureMeasureCache(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, copyTextTo);

        if (x <= 0)
        {
            return 0;
        }

        int idx = GetCharIndexFromXCached(x, context, font);
        idx = Math.Clamp(idx, 0, _measureCacheTextLength);

        if (idx <= 0)
        {
            return 0;
        }

        double w0 = GetPrefixWidthCached(idx - 1, context, font);
        double w1 = GetPrefixWidthCached(idx, context, font);
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
        if (textLength <= 0)
        {
            return 0;
        }

        EnsureMeasureCache(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, copyTextTo);
        double caretX = GetPrefixWidthCached(caretPosition, context, font);

        double newOffset = horizontalOffset;
        if (caretX - newOffset > viewportWidthDip - 5)
        {
            newOffset = caretX - viewportWidthDip + 10;
        }
        else if (caretX - newOffset < 5)
        {
            newOffset = Math.Max(0, caretX - 10);
        }

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
        if (textLength <= 0)
        {
            return 0;
        }

        EnsureMeasureCache(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, copyTextTo);
        // Allow a small gutter after the last glyph so it doesn't get clipped at the viewport edge.
        double maxOffset = Math.Max(0, _measureCacheTotalWidth - Math.Max(0, viewportWidthDip) + Math.Max(0, endGutterDip));
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
        if (textLength <= 0)
        {
            return 0;
        }

        EnsureMeasureCache(context, font, fontFamily, fontSize, fontWeight, dpi, documentVersion, textLength, copyTextTo);
        return _measureCacheTotalWidth;
    }

    private void EnsureMeasureCache(
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
        if (_measureCacheText != null &&
            _measureCacheVersion == version &&
            _measureCacheFontKey == key &&
            _measureCacheTextLength == length &&
            _measureCacheChunkPrefixWidths != null &&
            _measureCacheChunkKerningAdjust != null)
        {
            return;
        }

        _measureCacheVersion = version;
        _measureCacheFontKey = key;
        _measureCacheTextLength = length;

        if (length <= 0)
        {
            _measureCacheText = Array.Empty<char>();
            _measureCacheChunkPrefixWidths = new[] { 0.0 };
            _measureCacheChunkKerningAdjust = new[] { 0.0 };
            _measureCacheTotalWidth = 0;
            return;
        }

        _measureCacheText = new char[length];
        copyTo(_measureCacheText, 0, length);
        var span = _measureCacheText.AsSpan(0, length);

        int chunkCount = (length + MeasureChunkSize - 1) / MeasureChunkSize;
        var prefixWidths = new double[chunkCount + 1];
        var kerningAdjust = new double[chunkCount + 1];

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

        prefixWidths[0] = 0;
        for (int i = 1; i <= chunkCount; i++)
        {
            int chunkStart = (i - 1) * MeasureChunkSize;
            int chunkEnd = Math.Min(length, i * MeasureChunkSize);
            double chunkW = chunkEnd <= chunkStart ? 0 : context.MeasureText(span.Slice(chunkStart, chunkEnd - chunkStart), font).Width;
            double adjust = i <= 1 ? 0 : kerningAdjust[i - 1];
            prefixWidths[i] = prefixWidths[i - 1] + chunkW + adjust;
        }

        _measureCacheChunkPrefixWidths = prefixWidths;
        _measureCacheChunkKerningAdjust = kerningAdjust;
        _measureCacheTotalWidth = prefixWidths[^1];
    }

    private int GetCharIndexFromXCached(double x, IGraphicsContext context, IFont font)
    {
        if (_measureCacheTextLength <= 0)
        {
            return 0;
        }

        if (x <= 0)
        {
            return 0;
        }

        if (x >= _measureCacheTotalWidth)
        {
            return _measureCacheTextLength;
        }

        var span = _measureCacheText!.AsSpan(0, _measureCacheTextLength);
        var prefixWidths = _measureCacheChunkPrefixWidths!;
        var kerningAdjust = _measureCacheChunkKerningAdjust!;

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
        int chunkEnd = Math.Min(_measureCacheTextLength, chunkStart + MeasureChunkSize);
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

        return Math.Clamp(lo, 0, _measureCacheTextLength);
    }

    private double GetPrefixWidthCached(int index, IGraphicsContext context, IFont font)
    {
        index = Math.Clamp(index, 0, _measureCacheTextLength);
        if (index <= 0)
        {
            return 0;
        }

        var span = _measureCacheText!.AsSpan(0, _measureCacheTextLength);
        var prefixWidths = _measureCacheChunkPrefixWidths!;
        var kerningAdjust = _measureCacheChunkKerningAdjust!;

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
}
