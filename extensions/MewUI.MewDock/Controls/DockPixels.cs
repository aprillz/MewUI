namespace Aprillz.MewUI.MewDock.Controls;

/// <summary>Device-pixel rules for pane content, which is drawn in a layer above the frames around it.</summary>
internal static class DockPixels
{
    private const double EPSILON = 1e-6;

    /// <summary>
    /// The area inside a frame drawn at <paramref name="frame"/> with a <paramref name="borderDip"/> border, on whole
    /// device pixels that never reach the border's pixels.
    /// </summary>
    public static Rect Inside(Rect frame, double borderDip, double dpiScale)
    {
        double border = LayoutRounding.SnapThicknessToPixels(borderDip, dpiScale, 1);
        return SnapInward(frame.Deflate(new Thickness(border)), dpiScale);
    }

    /// <summary>Moves each edge of <paramref name="rect"/> inward to the nearest device pixel.</summary>
    public static Rect SnapInward(Rect rect, double dpiScale)
    {
        if (rect.IsEmpty || dpiScale <= 0 || !double.IsFinite(dpiScale))
        {
            return rect;
        }

        int leftPx = (int)Math.Ceiling(rect.X * dpiScale - EPSILON);
        int topPx = (int)Math.Ceiling(rect.Y * dpiScale - EPSILON);
        int rightPx = (int)Math.Floor(rect.Right * dpiScale + EPSILON);
        int bottomPx = (int)Math.Floor(rect.Bottom * dpiScale + EPSILON);
        return new Rect(
            leftPx / dpiScale,
            topPx / dpiScale,
            Math.Max(0, rightPx - leftPx) / dpiScale,
            Math.Max(0, bottomPx - topPx) / dpiScale);
    }
}
