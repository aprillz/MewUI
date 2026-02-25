namespace Aprillz.MewUI;

/// <summary>
/// Utilities for snapping layout and rendering geometry to device pixels (WPF-style).
/// </summary>
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
    public static Rect MakeClipRect(Rect rect, double dpiScale, int rightPx = 0, int bottomPx = 0) =>
        ExpandClipByDevicePixels(rect, dpiScale, rightPx, bottomPx);

    /// <summary>
    /// Snaps a constraint rectangle (used for Measure inputs) to device pixels.
    /// This should be stable and must not cause layout expansion beyond the available slot.
    /// </summary>
    public static Rect SnapConstraintRectToPixels(Rect rect, double dpiScale) =>
        SnapRectEdgesToPixels(rect, dpiScale);

    /// <summary>
    /// Snaps a thickness value in DIPs to an integer number of pixels (with a minimum pixel count).
    /// </summary>
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

    /// <summary>
    /// Rounds a size to device pixels.
    /// </summary>
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

    /// <summary>
    /// Snaps a rectangle by rounding both edges to pixels (may change size slightly).
    /// </summary>
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

    /// <summary>
    /// Snaps a rectangle outward (floor left/top, ceil right/bottom) so it never shrinks.
    /// A small epsilon tolerance prevents floating-point round-trip errors
    /// (e.g. <c>Floor((16/1.5 + 2/1.5) * 1.5)</c> yielding 17 instead of 18) from
    /// shifting edges by an extra device pixel.
    /// </summary>
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

        const double eps = 1e-6;
        int leftPx = (int)Math.Floor(rect.X * dpiScale + eps);
        int topPx = (int)Math.Floor(rect.Y * dpiScale + eps);
        int rightPx = (int)Math.Ceiling((rect.X + rect.Width) * dpiScale - eps);
        int bottomPx = (int)Math.Ceiling((rect.Y + rect.Height) * dpiScale - eps);

        int widthPx = Math.Max(0, rightPx - leftPx);
        int heightPx = Math.Max(0, bottomPx - topPx);

        double x = leftPx / dpiScale;
        double y = topPx / dpiScale;
        double w = widthPx / dpiScale;
        double h = heightPx / dpiScale;

        return new Rect(x, y, w, h);
    }

    /// <summary>
    /// Expands a clip rectangle by whole device pixels and snaps outward.
    /// </summary>
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

    /// <summary>
    /// Rounds a rectangle position/size independently (avoids edge-based jitter).
    /// </summary>
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

    /// <summary>
    /// Rounds a DIP value to a pixel-aligned integer coordinate.
    /// </summary>
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

    /// <summary>
    /// Ceils a DIP value to a pixel-aligned integer coordinate.
    /// A small epsilon tolerance prevents floating-point round-trip errors from
    /// expanding by an extra device pixel.
    /// </summary>
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

        const double eps = 1e-6;
        return (int)Math.Ceiling(value * dpiScale - eps);
    }

    /// <summary>
    /// Rounds a DIP value to a pixel-aligned DIP value.
    /// </summary>
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
