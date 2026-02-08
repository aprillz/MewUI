namespace Aprillz.MewUI.Controls;

internal static class ItemsViewportMath
{
    public static void ComputeVisibleRange(
        int count,
        double itemHeight,
        double contentHeight,
        double contentY,
        double verticalOffset,
        out int first,
        out int lastExclusive,
        out double yStart,
        out double offsetInItem)
    {
        if (count <= 0 || itemHeight <= 0 || contentHeight <= 0)
        {
            first = 0;
            lastExclusive = 0;
            yStart = contentY;
            offsetInItem = 0;
            return;
        }

        first = Math.Max(0, (int)Math.Floor(verticalOffset / itemHeight));
        offsetInItem = verticalOffset - first * itemHeight;
        yStart = contentY - offsetInItem;

        int visibleCount = (int)Math.Ceiling((contentHeight + offsetInItem) / itemHeight) + 1;
        lastExclusive = Math.Min(count, first + Math.Max(0, visibleCount));
    }

    public static bool TryGetItemIndexAtY(
        double y,
        double contentY,
        double verticalOffset,
        double itemHeight,
        int count,
        out int index)
    {
        index = -1;

        if (count <= 0 || itemHeight <= 0)
        {
            return false;
        }

        index = (int)((y - contentY + verticalOffset) / itemHeight);
        return index >= 0 && index < count;
    }
}

