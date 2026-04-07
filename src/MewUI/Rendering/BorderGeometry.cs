// Portions of this file are derived from dotnet/wpf (MIT License).
// https://github.com/dotnet/wpf
// Copyright (c) .NET Foundation and Contributors. All rights reserved.
// Licensed under the MIT License. See https://github.com/dotnet/wpf/blob/main/LICENSE.TXT

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Generates <see cref="PathGeometry"/> contours for non-uniform border rendering.
/// Adapted from WPF's Border.GenerateGeometry with fill-based rendering
/// (all coordinates pixel-aligned, no halfBorder adjustment).
/// </summary>
internal static class BorderGeometry
{
    private const double K = 0.5522847498307936; // 4/3 * (sqrt(2) - 1)

    /// <summary>
    /// WPF-style radius clamping.
    /// </summary>
    public static CornerRadius ClampRadii(Rect bounds, CornerRadius cr)
    {
        double w = bounds.Width;
        double h = bounds.Height;

        if (w <= 0 || h <= 0)
            return CornerRadius.Zero;

        double scale = 1.0;

        double topSum = cr.TopLeft + cr.TopRight;
        if (topSum > 0) scale = Math.Min(scale, w / topSum);

        double bottomSum = cr.BottomLeft + cr.BottomRight;
        if (bottomSum > 0) scale = Math.Min(scale, w / bottomSum);

        double leftSum = cr.TopLeft + cr.BottomLeft;
        if (leftSum > 0) scale = Math.Min(scale, h / leftSum);

        double rightSum = cr.TopRight + cr.BottomRight;
        if (rightSum > 0) scale = Math.Min(scale, h / rightSum);

        if (scale >= 1.0)
            return cr;

        return cr * scale;
    }

    /// <summary>
    /// Outer contour only (CW). Fill with borderBrush, then paint background on top.
    /// </summary>
    public static PathGeometry CreateOuterContour(in BorderRenderMetrics m)
    {
        var path = new PathGeometry();
        GenerateContour(path, m.Bounds, m.CornerRadius);
        return path;
    }

    /// <summary>
    /// Border region: outer CW + inner CCW, fill with NonZero.
    /// Use when background is transparent (no overwrite available).
    /// </summary>
    public static PathGeometry CreateBorderRegion(in BorderRenderMetrics m)
    {
        var path = new PathGeometry { FillRule = FillRule.NonZero };

        GenerateContour(path, m.Bounds, m.CornerRadius);

        if (m.InnerBounds.Width > 0 && m.InnerBounds.Height > 0)
        {
            GenerateContourReversed(path, m.InnerBounds,
                m.InnerTopLeftX, m.InnerTopLeftY,
                m.InnerTopRightX, m.InnerTopRightY,
                m.InnerBottomRightX, m.InnerBottomRightY,
                m.InnerBottomLeftX, m.InnerBottomLeftY);
        }

        return path;
    }

    /// <summary>
    /// Background region: inner contour CW.
    /// </summary>
    public static PathGeometry CreateBackgroundRegion(in BorderRenderMetrics m)
    {
        var path = new PathGeometry();
        if (m.InnerBounds.Width <= 0 || m.InnerBounds.Height <= 0)
            return path;

        GenerateContour(path, m.InnerBounds,
            m.InnerTopLeftX, m.InnerTopLeftY,
            m.InnerTopRightX, m.InnerTopRightY,
            m.InnerBottomRightX, m.InnerBottomRightY,
            m.InnerBottomLeftX, m.InnerBottomLeftY);

        return path;
    }

    /// <summary>
    /// CW contour with uniform per-corner radius (outer contour: rx == ry per corner).
    /// </summary>
    private static void GenerateContour(PathGeometry path, Rect rect, CornerRadius cr)
    {
        GenerateContour(path, rect,
            cr.TopLeft, cr.TopLeft,
            cr.TopRight, cr.TopRight,
            cr.BottomRight, cr.BottomRight,
            cr.BottomLeft, cr.BottomLeft);
    }

    /// <summary>
    /// CW contour with per-corner per-axis radii.
    /// Port of WPF GenerateGeometry vertex layout + overlap resolution.
    /// </summary>
    private static void GenerateContour(
        PathGeometry path, Rect rect,
        double leftTop, double topLeft,       // TopLeft: X, Y
        double rightTop, double topRight,     // TopRight: X, Y
        double rightBottom, double bottomRight, // BottomRight: X, Y
        double leftBottom, double bottomLeft) // BottomLeft: X, Y
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        double x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;

        // 8 vertices
        double p0x = x + leftTop,        p0y = y;
        double p1x = x + w - rightTop,   p1y = y;
        double p2x = x + w,              p2y = y + topRight;
        double p3x = x + w,              p3y = y + h - bottomRight;
        double p4x = x + w - rightBottom, p4y = y + h;
        double p5x = x + leftBottom,     p5y = y + h;
        double p6x = x,                  p6y = y + h - bottomLeft;
        double p7x = x,                  p7y = y + topLeft;

        // Overlap resolution
        if (p0x > p1x) { double v = leftTop / (leftTop + rightTop) * w; p0x = x + v; p1x = x + v; }
        if (p2y > p3y) { double v = topRight / (topRight + bottomRight) * h; p2y = y + v; p3y = y + v; }
        if (p4x < p5x) { double v = leftBottom / (leftBottom + rightBottom) * w; p4x = x + v; p5x = x + v; }
        if (p6y < p7y) { double v = topLeft / (topLeft + bottomLeft) * h; p6y = y + v; p7y = y + v; }

        path.MoveTo(p0x, p0y);

        path.LineTo(p1x, p1y);
        ArcCW(path, p1x, p1y, p2x, p2y, (x + w) - p1x, p2y - y);

        path.LineTo(p3x, p3y);
        ArcCW(path, p3x, p3y, p4x, p4y, (x + w) - p4x, (y + h) - p3y);

        path.LineTo(p5x, p5y);
        ArcCW(path, p5x, p5y, p6x, p6y, p5x - x, (y + h) - p6y);

        path.LineTo(p7x, p7y);
        ArcCW(path, p7x, p7y, p0x, p0y, p0x - x, p7y - y);

        path.Close();
    }

    /// <summary>
    /// CCW contour — same vertices, reverse traversal.
    /// </summary>
    private static void GenerateContourReversed(
        PathGeometry path, Rect rect,
        double leftTop, double topLeft,
        double rightTop, double topRight,
        double rightBottom, double bottomRight,
        double leftBottom, double bottomLeft)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        double x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;

        double p0x = x + leftTop,        p0y = y;
        double p1x = x + w - rightTop,   p1y = y;
        double p2x = x + w,              p2y = y + topRight;
        double p3x = x + w,              p3y = y + h - bottomRight;
        double p4x = x + w - rightBottom, p4y = y + h;
        double p5x = x + leftBottom,     p5y = y + h;
        double p6x = x,                  p6y = y + h - bottomLeft;
        double p7x = x,                  p7y = y + topLeft;

        if (p0x > p1x) { double v = leftTop / (leftTop + rightTop) * w; p0x = x + v; p1x = x + v; }
        if (p2y > p3y) { double v = topRight / (topRight + bottomRight) * h; p2y = y + v; p3y = y + v; }
        if (p4x < p5x) { double v = leftBottom / (leftBottom + rightBottom) * w; p4x = x + v; p5x = x + v; }
        if (p6y < p7y) { double v = topLeft / (topLeft + bottomLeft) * h; p6y = y + v; p7y = y + v; }

        // Reverse: p0 → p7 → p6 → p5 → p4 → p3 → p2 → p1 → close
        path.MoveTo(p0x, p0y);

        ArcCCW(path, p0x, p0y, p7x, p7y, p0x - x, p7y - y);

        path.LineTo(p6x, p6y);
        ArcCCW(path, p6x, p6y, p5x, p5y, p5x - x, (y + h) - p6y);

        path.LineTo(p4x, p4y);
        ArcCCW(path, p4x, p4y, p3x, p3y, (x + w) - p4x, (y + h) - p3y);

        path.LineTo(p2x, p2y);
        ArcCCW(path, p2x, p2y, p1x, p1y, (x + w) - p1x, p2y - y);

        path.LineTo(p0x, p0y);
        path.Close();
    }

    private static void ArcCW(PathGeometry path, double sx, double sy, double ex, double ey, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) { if (sx != ex || sy != ey) path.LineTo(ex, ey); return; }

        double dx = ex - sx, dy = ey - sy;
        double c1x, c1y, c2x, c2y;

        if (dx >= 0 && dy >= 0)      { c1x = sx + rx * K; c1y = sy;          c2x = ex;          c2y = ey - ry * K; }
        else if (dx <= 0 && dy >= 0) { c1x = sx;          c1y = sy + ry * K; c2x = ex + rx * K; c2y = ey;          }
        else if (dx <= 0 && dy <= 0) { c1x = sx - rx * K; c1y = sy;          c2x = ex;          c2y = ey + ry * K; }
        else                         { c1x = sx;          c1y = sy - ry * K; c2x = ex - rx * K; c2y = ey;          }

        path.BezierTo(c1x, c1y, c2x, c2y, ex, ey);
    }

    private static void ArcCCW(PathGeometry path, double sx, double sy, double ex, double ey, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) { if (sx != ex || sy != ey) path.LineTo(ex, ey); return; }

        // CCW = reversed CW: for CW arc from end→start, swap its control points.
        double dx = ex - sx, dy = ey - sy;
        double c1x, c1y, c2x, c2y;

        // Reverse of CW(-dx, -dy): swap c1↔c2
        if (dx <= 0 && dy <= 0)      { c1x = sx;          c1y = sy + ry * K; c2x = ex + rx * K; c2y = ey;          }
        else if (dx >= 0 && dy <= 0) { c1x = sx - rx * K; c1y = sy;          c2x = ex;          c2y = ey + ry * K; }
        else if (dx >= 0 && dy >= 0) { c1x = sx;          c1y = sy - ry * K; c2x = ex - rx * K; c2y = ey;          }
        else                         { c1x = sx + rx * K; c1y = sy;          c2x = ex;          c2y = ey - ry * K; }

        path.BezierTo(c1x, c1y, c2x, c2y, ex, ey);
    }
}
