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

            while (i < segment.Length)
            {
                int wordStart = i;
                while (i < segment.Length && segment[i] != ' ')
                {
                    i++;
                }

                var word = segment.Slice(wordStart, i - wordStart);
                double wordWidth = measure(word);

                int spaceStart = i;
                while (i < segment.Length && segment[i] == ' ')
                {
                    i++;
                }

                int spaceCount = i - spaceStart;
                double spaceWidth = spaceCount > 0 ? singleSpaceWidth * spaceCount : 0;

                double candidateWidth = lineWidth > 0 ? lineWidth + spaceWidth + wordWidth : wordWidth;
                if (lineWidth > 0 && candidateWidth > maxWidth)
                {
                    i = wordStart;
                    break;
                }

                lineWidth = candidateWidth;
                lastGoodEnd = wordStart + word.Length;
                anyWord = true;
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
