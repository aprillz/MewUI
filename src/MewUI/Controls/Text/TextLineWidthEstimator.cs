using System.Buffers;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls.Text;

internal sealed class TextLineWidthEstimator
{
    internal delegate void LineSpanProvider(int lineIndex, out int start, out int end);
    internal delegate void CopyProvider(Span<char> destination, int start, int length);

    private readonly LineSpanProvider _getLineSpan;
    private readonly CopyProvider _copy;
    private readonly Func<int> _getLineCount;
    private readonly Func<int> _getTextLength;

    private int _cachedVersion = -1;
    private FontKey _cachedFontKey;
    private double _cachedWidth;

    internal readonly record struct FontKey(string Family, double Size, FontWeight Weight, uint Dpi);

    public TextLineWidthEstimator(LineSpanProvider getLineSpan, CopyProvider copy, Func<int> getLineCount, Func<int> getTextLength)
    {
        _getLineSpan = getLineSpan;
        _copy = copy;
        _getLineCount = getLineCount;
        _getTextLength = getTextLength;
    }

    public void Reset()
    {
        _cachedVersion = -1;
        _cachedFontKey = default;
        _cachedWidth = 0;
    }

    public bool TryGetCached(int documentVersion, FontKey fontKey, out double width)
    {
        if (_cachedVersion == documentVersion && _cachedFontKey == fontKey)
        {
            width = _cachedWidth;
            return true;
        }

        width = 0;
        return false;
    }

    public double ComputeObservedMax(
        IGraphicsContext context,
        IFont font,
        int documentVersion,
        FontKey fontKey,
        int firstLine,
        int lastExclusive)
    {
        int lineCount = _getLineCount();
        if (lineCount <= 0 || _getTextLength() == 0)
        {
            Cache(documentVersion, fontKey, 0);
            return 0;
        }

        firstLine = Math.Clamp(firstLine, 0, lineCount);
        lastExclusive = Math.Clamp(lastExclusive, firstLine, lineCount);

        if (_cachedVersion != documentVersion || _cachedFontKey != fontKey)
        {
            Cache(documentVersion, fontKey, 0);
        }

        if (firstLine >= lastExclusive)
        {
            return _cachedWidth;
        }

        const int StackAllocThreshold = 512;
        Span<char> smallBuffer = stackalloc char[StackAllocThreshold];

        double max = _cachedWidth;
        for (int i = firstLine; i < lastExclusive; i++)
        {
            _getLineSpan(i, out int start, out int end);
            if (end <= start)
            {
                continue;
            }

            int lineLength = end - start;
            char[]? rented = null;
            Span<char> lineBuffer = lineLength <= StackAllocThreshold
                ? smallBuffer.Slice(0, lineLength)
                : (rented = ArrayPool<char>.Shared.Rent(lineLength)).AsSpan(0, lineLength);

            try
            {
                _copy(lineBuffer, start, lineLength);
                max = Math.Max(max, context.MeasureText(lineBuffer, font).Width);
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<char>.Shared.Return(rented);
                }
            }
        }

        Cache(documentVersion, fontKey, max);
        return max;
    }

    public double Compute(IGraphicsContext context, IFont font, int documentVersion, FontKey fontKey)
    {
        int lineCount = _getLineCount();
        if (lineCount <= 0 || _getTextLength() == 0)
        {
            Cache(documentVersion, fontKey, 0);
            return 0;
        }

        Cache(documentVersion, fontKey, 0);
        return ComputeObservedMax(context, font, documentVersion, fontKey, 0, lineCount);
    }

    private void Cache(int documentVersion, FontKey fontKey, double width)
    {
        _cachedVersion = documentVersion;
        _cachedFontKey = fontKey;
        _cachedWidth = width;
    }
}
