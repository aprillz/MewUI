namespace Aprillz.MewUI;

public static class LayoutRounding
{
    /// <summary>
    /// Snaps bounds geometry (background/border/layout boxes) to device pixels.
    /// This snapping may shrink/grow by rounding edges, so prefer it for geometry that should be stable.
    /// </summary>
    public static Rect SnapBoundsRectToPixels(Rect rect, double dpiScale) =>
        SnapRectEdgesToPixels(rect, dpiScale);

    /// <summary>
    /// Snaps a viewport rectangle to device pixels without allowing it to shrink due to rounding.
    /// Prefer this for scroll viewports and clip rectangles.
    /// </summary>
    public static Rect SnapViewportRectToPixels(Rect rect, double dpiScale) =>
        SnapRectEdgesToPixelsOutward(rect, dpiScale);

    /// <summary>
    /// Produces a clip rectangle that won't shrink due to rounding and can optionally be expanded by whole device pixels.
    /// </summary>
    public static Rect MakeClipRect(Rect rect, double dpiScale, int rightPx = 1, int bottomPx = 1) =>
        ExpandClipByDevicePixels(rect, dpiScale, rightPx, bottomPx);

    /// <summary>
    /// Snaps a constraint rectangle (used for Measure inputs) to device pixels.
    /// This should be stable and must not cause layout expansion beyond the available slot.
    /// </summary>
    public static Rect SnapConstraintRectToPixels(Rect rect, double dpiScale) =>
        SnapRectEdgesToPixels(rect, dpiScale);

    public static double SnapThicknessToPixels(double thicknessDip, double dpiScale, int minPixels)
    {
        if (thicknessDip <= 0)
        {
            return 0;
        }

        int px = RoundToPixelInt(thicknessDip, dpiScale);
        if (px < minPixels)
        {
            px = minPixels;
        }

        return px / dpiScale;
    }

    public static Size RoundSizeToPixels(Size size, double dpiScale)
    {
        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            return size;
        }

        if (size.IsEmpty)
        {
            return size;
        }

        var w = RoundToPixel(size.Width, dpiScale);
        var h = RoundToPixel(size.Height, dpiScale);
        return new Size(Math.Max(0, w), Math.Max(0, h));
    }

    public static Rect SnapRectEdgesToPixels(Rect rect, double dpiScale)
    {
        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            return rect;
        }

        if (rect.IsEmpty)
        {
            return rect;
        }

        int leftPx = RoundToPixelInt(rect.X, dpiScale);
        int topPx = RoundToPixelInt(rect.Y, dpiScale);
        int rightPx = RoundToPixelInt(rect.X + rect.Width, dpiScale);
        int bottomPx = RoundToPixelInt(rect.Y + rect.Height, dpiScale);

        int widthPx = Math.Max(0, rightPx - leftPx);
        int heightPx = Math.Max(0, bottomPx - topPx);

        double x = leftPx / dpiScale;
        double y = topPx / dpiScale;
        double w = widthPx / dpiScale;
        double h = heightPx / dpiScale;

        return new Rect(x, y, w, h);
    }

    public static Rect SnapRectEdgesToPixelsOutward(Rect rect, double dpiScale)
    {
        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            return rect;
        }

        if (rect.IsEmpty)
        {
            return rect;
        }

        int leftPx = (int)Math.Floor(rect.X * dpiScale);
        int topPx = (int)Math.Floor(rect.Y * dpiScale);
        int rightPx = (int)Math.Ceiling((rect.X + rect.Width) * dpiScale);
        int bottomPx = (int)Math.Ceiling((rect.Y + rect.Height) * dpiScale);

        int widthPx = Math.Max(0, rightPx - leftPx);
        int heightPx = Math.Max(0, bottomPx - topPx);

        double x = leftPx / dpiScale;
        double y = topPx / dpiScale;
        double w = widthPx / dpiScale;
        double h = heightPx / dpiScale;

        return new Rect(x, y, w, h);
    }

    public static Rect ExpandClipByDevicePixels(Rect rect, double dpiScale, int rightPx = 1, int bottomPx = 1)
    {
        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            return rect;
        }

        if (rect.IsEmpty)
        {
            return rect;
        }

        rightPx = Math.Max(0, rightPx);
        bottomPx = Math.Max(0, bottomPx);

        if (rightPx == 0 && bottomPx == 0)
        {
            return SnapRectEdgesToPixelsOutward(rect, dpiScale);
        }

        double expandW = rightPx / dpiScale;
        double expandH = bottomPx / dpiScale;
        var expanded = new Rect(rect.X, rect.Y, rect.Width + expandW, rect.Height + expandH);
        return SnapRectEdgesToPixelsOutward(expanded, dpiScale);
    }

    public static Rect RoundRectToPixels(Rect rect, double dpiScale)
    {
        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            return rect;
        }

        if (rect.IsEmpty)
        {
            return rect;
        }

        // Round position and size independently (WPF-style) to avoid jitter introduced by
        // rounding both edges separately (left/right), which can change size by ±1px.
        int leftPx = RoundToPixelInt(rect.X, dpiScale);
        int topPx = RoundToPixelInt(rect.Y, dpiScale);
        int widthPx = RoundToPixelInt(rect.Width, dpiScale);
        int heightPx = RoundToPixelInt(rect.Height, dpiScale);

        double x = leftPx / dpiScale;
        double y = topPx / dpiScale;
        double w = Math.Max(0, widthPx / dpiScale);
        double h = Math.Max(0, heightPx / dpiScale);

        return new Rect(x, y, w, h);
    }

    public static int RoundToPixelInt(double value, double dpiScale)
    {
        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return (int)Math.Round(value * dpiScale, MidpointRounding.AwayFromZero);
    }

    public static int CeilToPixelInt(double value, double dpiScale)
    {
        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            return (int)Math.Ceiling(value);
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return (int)Math.Ceiling(value * dpiScale);
    }

    public static double RoundToPixel(double value, double dpiScale)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value;
        }

        // WPF-style: avoid banker's rounding to reduce jitter at .5 boundaries (e.g. 150% DPI).
        return Math.Round(value * dpiScale, MidpointRounding.AwayFromZero) / dpiScale;
    }
}
