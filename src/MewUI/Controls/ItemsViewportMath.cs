namespace Aprillz.MewUI.Controls;

internal static class ItemsViewportMath
{
    public static double ComputeScrollOffsetToBringItemIntoView(
        int index,
        double itemHeight,
        double viewportHeight,
        double currentOffset)
    {
        if (index < 0 || itemHeight <= 0 || viewportHeight <= 0 ||
            double.IsNaN(itemHeight) || double.IsInfinity(itemHeight) ||
            double.IsNaN(viewportHeight) || double.IsInfinity(viewportHeight) ||
            double.IsNaN(currentOffset) || double.IsInfinity(currentOffset))
        {
            return currentOffset;
        }

        double itemTop = index * itemHeight;
        double itemBottom = itemTop + itemHeight;

        double newOffset = currentOffset;
        if (itemTop < newOffset)
        {
            newOffset = itemTop;
        }
        else if (itemBottom > newOffset + viewportHeight)
        {
            newOffset = itemBottom - viewportHeight;
        }

        return newOffset;
    }

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
