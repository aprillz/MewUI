using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Shared helpers for mapping <see cref="StrokeStyle"/> and <see cref="IPen"/>
/// to NanoVG stroke state, and gradient brushes to NanoVG paint.
/// </summary>
internal static class NvgStrokeHelper
{
    public static void ApplyPenStyle(NanoVG vg, IPen pen)
    {
        vg.StrokeWidth((float)pen.Thickness);

        var style = pen.StrokeStyle;

        vg.LineCap(style.LineCap switch
        {
            StrokeLineCap.Round => NVGlineCap.Round,
            StrokeLineCap.Square => NVGlineCap.Square,
            _ => NVGlineCap.Butt,
        });

        vg.LineJoin(style.LineJoin switch
        {
            StrokeLineJoin.Round => NVGlineJoin.Round,
            StrokeLineJoin.Bevel => NVGlineJoin.Bevel,
            _ => NVGlineJoin.Miter,
        });

        if (style.MiterLimit > 0)
            vg.MiterLimit((float)style.MiterLimit);
    }

    /// <summary>
    /// Applies stroke color or gradient paint to the current NanoVG state.
    /// For gradient brushes, uses StrokePaint with a real gradient instead of a representative color.
    /// </summary>
    public static void ApplyStrokeBrush(NanoVG vg, IPen pen, Rect strokeBounds)
    {
        if (pen.Brush is ISolidColorBrush solid)
        {
            vg.StrokeColor(NVGcolor.RGBA(solid.Color.R, solid.Color.G, solid.Color.B, solid.Color.A));
        }
        else if (pen.Brush is IGradientBrush gradient)
        {
            var paint = CreateGradientPaint(vg, gradient, strokeBounds);
            if (paint.HasValue)
                vg.StrokePaint(paint.Value);
            else
            {
                var c = gradient.GetRepresentativeColor();
                vg.StrokeColor(NVGcolor.RGBA(c.R, c.G, c.B, c.A));
            }
        }
    }

    /// <summary>
    /// Creates an NVGpaint for a gradient brush. Used for both fill and stroke.
    /// </summary>
    private static NVGpaint? CreateGradientPaint(NanoVG vg, IGradientBrush gradient, Rect objectBounds)
    {
        var stops = gradient.Stops;
        var startColor = ToNvgColor(GradientBrushHelper.Sample(stops, 0));
        var endColor = ToNvgColor(GradientBrushHelper.Sample(stops, 1));

        if (gradient is ILinearGradientBrush linear)
        {
            var sp = ResolveGradientPoint(linear.StartPoint, linear.GradientUnits, objectBounds);
            var ep = ResolveGradientPoint(linear.EndPoint, linear.GradientUnits, objectBounds);
            return vg.LinearGradient((float)sp.X, (float)sp.Y, (float)ep.X, (float)ep.Y, startColor, endColor);
        }

        if (gradient is IRadialGradientBrush radial)
        {
            // Use GradientOrigin as the NanoVG center (where offset=0 color appears).
            // NanoVG doesn't support separate center vs focal point, so this is the best approximation.
            var origin = ResolveGradientPoint(radial.GradientOrigin, radial.GradientUnits, objectBounds);
            var center = ResolveGradientPoint(radial.Center, radial.GradientUnits, objectBounds);
            float outerRadius = (float)ResolveGradientLength(radial.RadiusX, radial.GradientUnits, objectBounds.Width);

            // If origin != center, extend the outer radius to cover the boundary ellipse from the origin.
            float dx = (float)(origin.X - center.X);
            float dy = (float)(origin.Y - center.Y);
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float adjustedRadius = outerRadius + dist;

            return vg.RadialGradient((float)origin.X, (float)origin.Y, 0f, adjustedRadius, startColor, endColor);
        }

        return null;
    }

    public static void ApplyGradientPaint(NanoVG vg, IGradientBrush gradient, Rect objectBounds)
    {
        var paint = CreateGradientPaint(vg, gradient, objectBounds);
        if (paint.HasValue)
            vg.FillPaint(paint.Value);
        else
            vg.FillColor(ToNvgColor(gradient.GetRepresentativeColor()));
    }

    /// <summary>
    /// Draws a stroke with software dashing. NanoVG has no native dash support,
    /// so we flatten the path to line segments and draw each dash individually.
    /// </summary>
    public static void DrawDashedStroke(NanoVG vg, PathGeometry path, IPen pen, Rect strokeBounds)
    {
        var style = pen.StrokeStyle;
        if (style.DashArray is not { Count: > 0 } dashes)
            return;

        var segments = FlattenPath(path);
        if (segments.Count == 0) return;

        float thickness = (float)pen.Thickness;
        float dashOffset = (float)style.DashOffset * thickness;

        // Build dash pattern in absolute units (multiply by stroke width, like SVG/WPF)
        var pattern = new float[dashes.Count];
        float patternLen = 0;
        for (int i = 0; i < dashes.Count; i++)
        {
            pattern[i] = (float)dashes[i] * thickness;
            patternLen += pattern[i];
        }
        if (patternLen <= 0) return;

        // Apply pen style (cap, join, etc.)
        ApplyPenStyle(vg, pen);
        ApplyStrokeBrush(vg, pen, strokeBounds);

        // Walk segments, emitting dash sub-paths
        int dashIndex = 0;
        float dashRemain = pattern[0];
        bool drawing = true; // starts with dash (not gap)

        // Consume offset
        if (dashOffset > 0)
        {
            float off = dashOffset % patternLen;
            while (off > 0)
            {
                if (off >= dashRemain)
                {
                    off -= dashRemain;
                    drawing = !drawing;
                    dashIndex = (dashIndex + 1) % pattern.Length;
                    dashRemain = pattern[dashIndex];
                }
                else
                {
                    dashRemain -= off;
                    off = 0;
                }
            }
        }

        bool inPath = false;

        foreach (var seg in segments)
        {
            if (seg.IsMoveTo)
            {
                // New subpath: flush previous dash stroke and reset
                if (inPath)
                {
                    vg.Stroke();
                    inPath = false;
                }
                // Reset dash state at each subpath start
                dashIndex = 0;
                dashRemain = pattern[0];
                drawing = true;
                if (dashOffset > 0)
                {
                    float off = dashOffset % patternLen;
                    while (off > 0)
                    {
                        if (off >= dashRemain)
                        {
                            off -= dashRemain;
                            drawing = !drawing;
                            dashIndex = (dashIndex + 1) % pattern.Length;
                            dashRemain = pattern[dashIndex];
                        }
                        else
                        {
                            dashRemain -= off;
                            off = 0;
                        }
                    }
                }
                continue;
            }

            float x0 = seg.X0, y0 = seg.Y0, x1 = seg.X1, y1 = seg.Y1;
            float segDx = x1 - x0, segDy = y1 - y0;
            float segLen = MathF.Sqrt(segDx * segDx + segDy * segDy);
            if (segLen <= 0) continue;

            float consumed = 0;
            while (consumed < segLen)
            {
                float remain = segLen - consumed;
                float take = MathF.Min(dashRemain, remain);
                float t0 = consumed / segLen;
                float t1 = (consumed + take) / segLen;
                float px0 = x0 + segDx * t0, py0 = y0 + segDy * t0;
                float px1 = x0 + segDx * t1, py1 = y0 + segDy * t1;

                if (drawing)
                {
                    if (!inPath)
                    {
                        vg.BeginPath();
                        vg.MoveTo(px0, py0);
                        inPath = true;
                    }
                    vg.LineTo(px1, py1);
                }
                else
                {
                    if (inPath)
                    {
                        vg.Stroke();
                        inPath = false;
                    }
                }

                consumed += take;
                dashRemain -= take;
                if (dashRemain <= 0.001f)
                {
                    if (inPath && drawing)
                    {
                        vg.Stroke();
                        inPath = false;
                    }
                    drawing = !drawing;
                    dashIndex = (dashIndex + 1) % pattern.Length;
                    dashRemain = pattern[dashIndex];
                }
            }
        }

        if (inPath)
        {
            vg.Stroke();
        }
    }

    /// <summary>
    /// Draws a dashed stroke for simple line segments (no PathGeometry).
    /// </summary>
    public static void DrawDashedLine(NanoVG vg, float x0, float y0, float x1, float y1, IPen pen, Rect strokeBounds)
    {
        var path = new PathGeometry();
        path.MoveTo(x0, y0);
        path.LineTo(x1, y1);
        DrawDashedStroke(vg, path, pen, strokeBounds);
    }

    /// <summary>
    /// Draws a dashed stroke for a rectangle.
    /// </summary>
    public static void DrawDashedRect(NanoVG vg, float x, float y, float w, float h, IPen pen, Rect strokeBounds)
    {
        var path = new PathGeometry();
        path.MoveTo(x, y);
        path.LineTo(x + w, y);
        path.LineTo(x + w, y + h);
        path.LineTo(x, y + h);
        path.Close();
        DrawDashedStroke(vg, path, pen, strokeBounds);
    }

    /// <summary>
    /// Draws a dashed stroke for a rounded rectangle.
    /// </summary>
    public static void DrawDashedRoundedRect(NanoVG vg, float x, float y, float w, float h, float r, IPen pen, Rect strokeBounds)
    {
        var path = PathGeometry.FromRoundedRect(new Rect(x, y, w, h), r);
        DrawDashedStroke(vg, path, pen, strokeBounds);
    }

    /// <summary>
    /// Draws a dashed stroke for an ellipse.
    /// </summary>
    public static void DrawDashedEllipse(NanoVG vg, float cx, float cy, float rx, float ry, IPen pen, Rect strokeBounds)
    {
        var path = new PathGeometry();
        // Approximate ellipse with 4 bezier curves (standard approach)
        const float kappa = 0.5522847498f;
        float ox = rx * kappa, oy = ry * kappa;
        path.MoveTo(cx - rx, cy);
        path.BezierTo(cx - rx, cy - oy, cx - ox, cy - ry, cx, cy - ry);
        path.BezierTo(cx + ox, cy - ry, cx + rx, cy - oy, cx + rx, cy);
        path.BezierTo(cx + rx, cy + oy, cx + ox, cy + ry, cx, cy + ry);
        path.BezierTo(cx - ox, cy + ry, cx - rx, cy + oy, cx - rx, cy);
        path.Close();
        DrawDashedStroke(vg, path, pen, strokeBounds);
    }

    /// <summary>
    /// Flattens a PathGeometry into line segments for dash computation.
    /// Bezier curves are subdivided into line approximations.
    /// </summary>
    private static List<DashSegment> FlattenPath(PathGeometry path)
    {
        var result = new List<DashSegment>();
        float curX = 0, curY = 0;
        float moveX = 0, moveY = 0;

        foreach (var cmd in path.Commands)
        {
            switch (cmd.Type)
            {
                case PathCommandType.MoveTo:
                    curX = (float)cmd.X0;
                    curY = (float)cmd.Y0;
                    moveX = curX;
                    moveY = curY;
                    result.Add(new DashSegment(curX, curY, curX, curY, true));
                    break;

                case PathCommandType.LineTo:
                {
                    float x = (float)cmd.X0, y = (float)cmd.Y0;
                    result.Add(new DashSegment(curX, curY, x, y, false));
                    curX = x; curY = y;
                    break;
                }

                case PathCommandType.BezierTo:
                {
                    float c1x = (float)cmd.X0, c1y = (float)cmd.Y0;
                    float c2x = (float)cmd.X1, c2y = (float)cmd.Y1;
                    float ex = (float)cmd.X2, ey = (float)cmd.Y2;
                    FlattenBezier(result, curX, curY, c1x, c1y, c2x, c2y, ex, ey, 0);
                    curX = ex; curY = ey;
                    break;
                }

                case PathCommandType.Close:
                    if (MathF.Abs(curX - moveX) > 0.001f || MathF.Abs(curY - moveY) > 0.001f)
                        result.Add(new DashSegment(curX, curY, moveX, moveY, false));
                    curX = moveX; curY = moveY;
                    break;
            }
        }

        return result;
    }

    private static void FlattenBezier(List<DashSegment> result,
        float x0, float y0, float c1x, float c1y, float c2x, float c2y, float x3, float y3, int depth)
    {
        if (depth > 8)
        {
            result.Add(new DashSegment(x0, y0, x3, y3, false));
            return;
        }

        // Check flatness: distance from control points to the line (x0,y0)→(x3,y3)
        float dx = x3 - x0, dy = y3 - y0;
        float d1 = MathF.Abs((c1x - x3) * dy - (c1y - y3) * dx);
        float d2 = MathF.Abs((c2x - x3) * dy - (c2y - y3) * dx);
        float lenSq = dx * dx + dy * dy;
        float threshold = 0.5f; // pixel tolerance
        if ((d1 + d2) * (d1 + d2) <= threshold * threshold * lenSq)
        {
            result.Add(new DashSegment(x0, y0, x3, y3, false));
            return;
        }

        // de Casteljau subdivision at t=0.5
        float m01x = (x0 + c1x) * 0.5f, m01y = (y0 + c1y) * 0.5f;
        float m12x = (c1x + c2x) * 0.5f, m12y = (c1y + c2y) * 0.5f;
        float m23x = (c2x + x3) * 0.5f, m23y = (c2y + y3) * 0.5f;
        float m012x = (m01x + m12x) * 0.5f, m012y = (m01y + m12y) * 0.5f;
        float m123x = (m12x + m23x) * 0.5f, m123y = (m12y + m23y) * 0.5f;
        float mx = (m012x + m123x) * 0.5f, my = (m012y + m123y) * 0.5f;

        FlattenBezier(result, x0, y0, m01x, m01y, m012x, m012y, mx, my, depth + 1);
        FlattenBezier(result, mx, my, m123x, m123y, m23x, m23y, x3, y3, depth + 1);
    }

    private readonly struct DashSegment
    {
        public readonly float X0, Y0, X1, Y1;
        public readonly bool IsMoveTo;

        public DashSegment(float x0, float y0, float x1, float y1, bool isMoveTo)
        {
            X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; IsMoveTo = isMoveTo;
        }
    }

    public static Rect ComputePathBounds(PathGeometry path)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var cmd in path.Commands)
        {
            if (cmd.Type == PathCommandType.Close) continue;
            Update(cmd.X0, cmd.Y0, ref minX, ref minY, ref maxX, ref maxY);
            if (cmd.Type == PathCommandType.BezierTo)
            {
                Update(cmd.X1, cmd.Y1, ref minX, ref minY, ref maxX, ref maxY);
                Update(cmd.X2, cmd.Y2, ref minX, ref minY, ref maxX, ref maxY);
            }
        }
        if (minX > maxX) return default;
        return new Rect(minX, minY, maxX - minX, maxY - minY);

        static void Update(double x, double y, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
    }

    private static Point ResolveGradientPoint(Point p, GradientUnits units, Rect bounds)
    {
        if (units == GradientUnits.ObjectBoundingBox)
            return new Point(bounds.X + p.X * bounds.Width, bounds.Y + p.Y * bounds.Height);
        return p;
    }

    private static double ResolveGradientLength(double value, GradientUnits units, double extent)
    {
        if (units == GradientUnits.ObjectBoundingBox)
            return value * extent;
        return value;
    }

    private static NVGcolor ToNvgColor(Color c) => NVGcolor.RGBA(c.R, c.G, c.B, c.A);
}
