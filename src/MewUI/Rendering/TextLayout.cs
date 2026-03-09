namespace Aprillz.MewUI.Rendering;

internal static class TextLayout
{
    public delegate double SpanMeasure(ReadOnlySpan<char> span);

    internal readonly record struct LineSegment(int Start, int Length, double Width);

    public static void EnumerateLines(
        ReadOnlySpan<char> text,
        int maxWidthPx,
        TextWrapping wrapping,
        SpanMeasure measure,
        Action<LineSegment> onLine)
    {
        if (text.IsEmpty)
        {
            onLine(new LineSegment(0, 0, 0));
            return;
        }

        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            bool isBreak = i == text.Length || text[i] == '\n';
            if (!isBreak)
            {
                continue;
            }

            int segStart = start;
            int segLen = i - start;
            if (segLen > 0 && text[segStart + segLen - 1] == '\r')
            {
                segLen--;
            }

            EnumerateWrappedSegment(text.Slice(segStart, segLen), segStart, maxWidthPx, wrapping, measure, onLine);
            start = i + 1;
        }
    }

    private const string Ellipsis = "...";

    /// <summary>
    /// Trims a line segment to fit within the available width, appending an ellipsis (…).
    /// Uses estimation-based approach: avgCharWidth → estimatedLen → verify with 1-3 measure calls.
    /// </summary>
    internal static LineSegment TrimLineWithEllipsis(
        ReadOnlySpan<char> lineText,
        int lineStart,
        double availableWidth,
        SpanMeasure measure)
    {
        if (lineText.IsEmpty || availableWidth <= 0)
        {
            return new LineSegment(lineStart, 0, 0);
        }

        double ellipsisWidth = measure(Ellipsis);
        double targetWidth = availableWidth - ellipsisWidth;
        if (targetWidth <= 0)
        {
            return new LineSegment(lineStart, 0, ellipsisWidth);
        }

        double fullWidth = measure(lineText);
        if (fullWidth <= availableWidth)
        {
            return new LineSegment(lineStart, lineText.Length, fullWidth);
        }

        // Estimation: avgCharWidth → estimatedLen
        double avgCharWidth = fullWidth / lineText.Length;
        int estimatedLen = Math.Clamp((int)(targetWidth / avgCharWidth), 1, lineText.Length);

        double w = measure(lineText.Slice(0, estimatedLen));

        // Adjust: if too wide, shrink; if too narrow, grow
        if (w > targetWidth)
        {
            while (estimatedLen > 1 && w > targetWidth)
            {
                estimatedLen--;
                w = measure(lineText.Slice(0, estimatedLen));
            }
        }
        else
        {
            while (estimatedLen < lineText.Length)
            {
                double next = measure(lineText.Slice(0, estimatedLen + 1));
                if (next > targetWidth)
                {
                    break;
                }
                estimatedLen++;
                w = next;
            }
        }

        return new LineSegment(lineStart, estimatedLen, w + ellipsisWidth);
    }

    private static void EnumerateWrappedSegment(
        ReadOnlySpan<char> segment,
        int segmentOffset,
        int maxWidthPx,
        TextWrapping wrapping,
        SpanMeasure measure,
        Action<LineSegment> onLine)
    {
        if (wrapping == TextWrapping.NoWrap || maxWidthPx <= 0)
        {
            double w = measure(segment);
            onLine(new LineSegment(segmentOffset, segment.Length, w));
            return;
        }

        if (segment.IsEmpty)
        {
            onLine(new LineSegment(segmentOffset, 0, 0));
            return;
        }

        double maxWidth = maxWidthPx;
        double singleSpaceWidth = measure(" ");

        int i = 0;
        while (i < segment.Length)
        {
            while (i < segment.Length && segment[i] == ' ')
            {
                i++;
            }

            if (i >= segment.Length)
            {
                break;
            }

            int lineStart = i;
            int lastGoodEnd = -1;
            double lineWidth = 0;
            bool anyWord = false;
            double pendingSpaceWidth = 0;

            while (i < segment.Length)
            {
                int wordStart = i;
                while (i < segment.Length && segment[i] != ' ')
                {
                    i++;
                }

                var word = segment.Slice(wordStart, i - wordStart);
                double wordWidth = measure(word);

                // Use pending space (from the previous word's trailing spaces)
                // as the inter-word gap before this word.
                double candidateWidth = lineWidth > 0 ? lineWidth + pendingSpaceWidth + wordWidth : wordWidth;
                if (lineWidth > 0 && candidateWidth > maxWidth)
                {
                    i = wordStart;
                    break;
                }

                lineWidth = candidateWidth;
                lastGoodEnd = wordStart + word.Length;
                anyWord = true;

                // Consume trailing spaces after this word; save width for next iteration.
                int spaceStart = i;
                while (i < segment.Length && segment[i] == ' ')
                {
                    i++;
                }

                int spaceCount = i - spaceStart;
                pendingSpaceWidth = spaceCount > 0 ? singleSpaceWidth * spaceCount : 0;
            }

            if (!anyWord)
            {
                int end = lineStart + 1;
                double width = measure(segment.Slice(lineStart, 1));
                while (end < segment.Length)
                {
                    double nextWidth = measure(segment.Slice(lineStart, end - lineStart + 1));
                    if (nextWidth > maxWidth)
                    {
                        break;
                    }
                    width = nextWidth;
                    end++;
                }

                onLine(new LineSegment(segmentOffset + lineStart, end - lineStart, width));
                i = end;
                continue;
            }

            onLine(new LineSegment(segmentOffset + lineStart, lastGoodEnd - lineStart, lineWidth));

            i = lastGoodEnd;
            while (i < segment.Length && segment[i] == ' ')
            {
                i++;
            }
        }
    }
}
