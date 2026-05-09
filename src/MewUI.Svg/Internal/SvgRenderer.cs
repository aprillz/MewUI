using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Svg.Internal;

/// <summary>
/// Renders a parsed SVG document tree into an <see cref="IGraphicsContext"/>.
/// Paths are built in raw SVG user-space coordinates; the current transform
/// matrix (ctm) is applied via <see cref="IGraphicsContext.SetTransform"/> so
/// gradient brushes can reference the same raw coordinate system.
/// </summary>
internal sealed class SvgRenderer
{
    private readonly IGraphicsContext _ctx;
    private readonly SvgDocumentNode _doc;

    public SvgRenderer(IGraphicsContext ctx, SvgDocumentNode doc)
    {
        _ctx = ctx;
        _doc = doc;
    }

    /// <summary>
    /// Renders the document scaled and positioned to fit <paramref name="destRect"/>.
    /// </summary>
    public void Render(Rect destRect)
    {
        if (destRect.IsEmpty) return;

        // Compute the viewBox → destRect mapping
        double vx = _doc.VbX ?? 0;
        double vy = _doc.VbY ?? 0;
        double vw = _doc.VbW ?? _doc.Width ?? 100;
        double vh = _doc.VbH ?? _doc.Height ?? 100;

        if (vw <= 0 || vh <= 0) return;

        double sx = destRect.Width  / vw;
        double sy = destRect.Height / vh;
        double tx = destRect.X - vx * sx;
        double ty = destRect.Y - vy * sy;
        var rootCtm = new SvgMatrix(sx, 0, 0, sy, tx, ty);

        var baseStyle = SvgResolvedStyle.FromDefault();

        _ctx.Save();
        _ctx.SetClip(destRect);
        RenderChildren(_doc.Children, rootCtm, baseStyle);
        _ctx.Restore();
    }

    // ──────────────────────────────────────────────
    // Tree traversal
    // ──────────────────────────────────────────────

    private void RenderChildren(List<SvgNode> children, SvgMatrix ctm, SvgResolvedStyle parentStyle)
    {
        foreach (var node in children)
            RenderNode(node, ctm, parentStyle);
    }

    private void RenderNode(SvgNode node, SvgMatrix ctm, SvgResolvedStyle parentStyle)
    {
        // Resolve style (inherit from parent, override with own)
        var style = SvgResolvedStyle.Cascade(parentStyle, node.Style);

        if (!style.Display) return;

        // Concatenate local transform
        if (node.LocalTransform.HasValue)
            ctm = ctm.Append(node.LocalTransform.Value);

        switch (node)
        {
            case SvgGroupNode g:
                RenderChildren(g.Children, ctm, style);
                break;

            case SvgDefsNode:
                // defs are not rendered directly
                break;

            case SvgSymbolNode:
                // symbols are only rendered via <use>
                break;

            case SvgUseNode use:
                RenderUse(use, ctm, style);
                break;

            case SvgPathNode path:
                if (style.Visibility) RenderPath(path, ctm, style);
                break;

            case SvgRectNode rect:
                if (style.Visibility) RenderRect(rect, ctm, style);
                break;

            case SvgCircleNode circle:
                if (style.Visibility) RenderCircle(circle, ctm, style);
                break;

            case SvgEllipseNode ellipse:
                if (style.Visibility) RenderEllipse(ellipse, ctm, style);
                break;

            case SvgLineNode line:
                if (style.Visibility) RenderLine(line, ctm, style);
                break;

            case SvgPolylineNode poly:
                if (style.Visibility) RenderPolyline(poly, ctm, style);
                break;
        }
    }

    private void RenderUse(SvgUseNode use, SvgMatrix ctm, SvgResolvedStyle style)
    {
        if (use.Href == null || !_doc.Defs.TryGetValue(use.Href, out var target))
            return;

        // Apply x/y offset for <use>
        var localCtm = ctm.Append(SvgMatrix.Translate(use.X, use.Y));

        if (target is SvgSymbolNode sym)
        {
            // If symbol has a viewBox and use has width/height, scale accordingly
            SvgMatrix symCtm = localCtm;
            if (sym.VbW.HasValue && sym.VbH.HasValue && sym.VbW > 0 && sym.VbH > 0 &&
                use.Width > 0 && use.Height > 0)
            {
                double sx = use.Width  / sym.VbW.Value;
                double sy = use.Height / sym.VbH.Value;
                var scale = new SvgMatrix(sx, 0, 0, sy,
                    -(sym.VbX ?? 0) * sx, -(sym.VbY ?? 0) * sy);
                symCtm = localCtm.Append(scale);
            }
            var symStyle = SvgResolvedStyle.Cascade(style, target.Style);
            RenderChildren(sym.Children, symCtm, symStyle);
        }
        else
        {
            RenderNode(target, localCtm, style);
        }
    }

    // ──────────────────────────────────────────────
    // Shape rendering — paths are built in raw SVG coords (Identity).
    // ──────────────────────────────────────────────

    private void RenderPath(SvgPathNode node, SvgMatrix ctm, SvgResolvedStyle style)
    {
        if (string.IsNullOrEmpty(node.D)) return;
        var path = SvgPathParser.Parse(node.D!, SvgMatrix.Identity);
        FillAndStroke(path, ctm, style);
    }

    private void RenderRect(SvgRectNode node, SvgMatrix ctm, SvgResolvedStyle style)
    {
        if (node.Width <= 0 || node.Height <= 0) return;

        var path = BuildRectPath(node.X, node.Y, node.Width, node.Height, node.Rx, node.Ry, SvgMatrix.Identity);
        FillAndStroke(path, ctm, style);
    }

    private void RenderCircle(SvgCircleNode node, SvgMatrix ctm, SvgResolvedStyle style)
    {
        if (node.R <= 0) return;
        var path = BuildEllipsePath(node.Cx, node.Cy, node.R, node.R, SvgMatrix.Identity);
        FillAndStroke(path, ctm, style);
    }

    private void RenderEllipse(SvgEllipseNode node, SvgMatrix ctm, SvgResolvedStyle style)
    {
        if (node.Rx <= 0 || node.Ry <= 0) return;
        var path = BuildEllipsePath(node.Cx, node.Cy, node.Rx, node.Ry, SvgMatrix.Identity);
        FillAndStroke(path, ctm, style);
    }

    private void RenderLine(SvgLineNode node, SvgMatrix ctm, SvgResolvedStyle style)
    {
        if (style.Stroke.Kind == SvgPaintKind.None) return;
        var color = ResolveColor(style.Stroke, style, forStroke: true);
        if (color.A == 0) return;

        var ctmMatrix = ctm.ToMatrix3x2();
        _ctx.Save();
        try
        {
            _ctx.SetTransform(ctmMatrix);
            _ctx.DrawLine(new Point(node.X1, node.Y1), new Point(node.X2, node.Y2), color, Math.Max(style.StrokeWidth, 0.5));
        }
        finally { _ctx.Restore(); }
    }

    private void RenderPolyline(SvgPolylineNode node, SvgMatrix ctm, SvgResolvedStyle style)
    {
        if (node.Points.Count == 0) return;
        var path = new PathGeometry();
        path.MoveTo(node.Points[0].x, node.Points[0].y);
        for (int i = 1; i < node.Points.Count; i++)
            path.LineTo(node.Points[i].x, node.Points[i].y);
        if (node.Closed) path.Close();
        FillAndStroke(path, ctm, style);
    }

    // ──────────────────────────────────────────────
    // Fill + stroke dispatch — applies ctm via SetTransform
    // ──────────────────────────────────────────────

    private void FillAndStroke(PathGeometry path, SvgMatrix ctm, SvgResolvedStyle style)
    {
        bool hasFill = style.Fill.Kind != SvgPaintKind.None;
        bool hasStroke = style.Stroke.Kind != SvgPaintKind.None && style.StrokeWidth > 0;
        if (!hasFill && !hasStroke) return;

        var ctmMatrix = ctm.ToMatrix3x2();
        _ctx.Save();
        try
        {
            _ctx.SetTransform(ctmMatrix);

            if (hasFill)
            {
                var fillBrush = ResolveBrush(style.Fill, style, forStroke: false);
                if (fillBrush != null)
                {
                    try { _ctx.FillPath(path, fillBrush); }
                    finally { fillBrush.Dispose(); }
                }
                else
                {
                    var fillColor = ResolveColor(style.Fill, style, forStroke: false);
                    if (fillColor.A > 0)
                        _ctx.FillPath(path, fillColor);
                }
            }

            if (hasStroke)
            {
                var strokeColor = ResolveColor(style.Stroke, style, forStroke: true);
                if (strokeColor.A > 0)
                    _ctx.DrawPath(path, strokeColor, Math.Max(style.StrokeWidth, 0.5));
            }
        }
        finally { _ctx.Restore(); }
    }

    // ──────────────────────────────────────────────
    // Paint resolution
    // ──────────────────────────────────────────────

    private IBrush? ResolveBrush(SvgPaint paint, SvgResolvedStyle style, bool forStroke)
    {
        if (paint.Kind != SvgPaintKind.Url || paint.Url == null) return null;
        if (!_doc.Defs.TryGetValue(paint.Url, out var defNode)) return null;
        if (defNode is not SvgGradientNode grad || grad.Stops.Count == 0) return null;

        double opacity = Math.Clamp(style.Opacity * (forStroke ? style.StrokeOpacity : style.FillOpacity), 0, 1);
        var transform = grad.Transform?.ToMatrix3x2();

        var factory = Application.DefaultGraphicsFactory;
        if (grad is SvgLinearGradientNode lin)
        {
            var stops = BuildStops(grad, opacity, reverse: false);
            return factory.CreateLinearGradientBrush(
                new Point(lin.X1, lin.Y1), new Point(lin.X2, lin.Y2),
                stops, grad.Spread, grad.Units, transform);
        }
        if (grad is SvgRadialGradientNode rad)
        {
            var stops = BuildStops(grad, opacity, reverse: true);
            return factory.CreateRadialGradientBrush(
                new Point(rad.Cx, rad.Cy), new Point(rad.Fx, rad.Fy),
                rad.R, rad.R, stops, grad.Spread, grad.Units, transform);
        }
        return null;
    }

    private static IReadOnlyList<GradientStop> BuildStops(SvgGradientNode grad, double elementOpacity, bool reverse)
    {
        int count = grad.Stops.Count;
        var stops = new GradientStop[count];
        for (int i = 0; i < count; i++)
        {
            var src = grad.Stops[i];
            double alphaFraction = Math.Clamp(src.Opacity * elementOpacity, 0, 1);
            byte alpha = (byte)Math.Round(src.Color.A * alphaFraction);
            var color = new Color(alpha, src.Color.R, src.Color.G, src.Color.B);
            if (reverse)
                stops[count - 1 - i] = new GradientStop(1.0 - src.Offset, color);
            else
                stops[i] = new GradientStop(src.Offset, color);
        }
        return stops;
    }

    private Color ResolveColor(SvgPaint paint, SvgResolvedStyle style, bool forStroke)
    {
        Color color;
        switch (paint.Kind)
        {
            case SvgPaintKind.Color:
                color = paint.Color;
                break;

            case SvgPaintKind.CurrentColor:
                // Resolve to the fill color if it's a solid color, else black
                color = style.Fill.Kind == SvgPaintKind.Color ? style.Fill.Color : new Color(0, 0, 0);
                break;

            case SvgPaintKind.Url:
                // Resolve gradient → representative solid color
                if (paint.Url != null && _doc.Defs.TryGetValue(paint.Url, out var defNode)
                    && defNode is SvgGradientNode grad)
                    color = grad.GetRepresentativeColor();
                else
                    color = new Color(0, 0, 0);
                break;

            default:
                return Color.Transparent;
        }

        // Apply opacity: element opacity * fill/stroke opacity
        double opacity = style.Opacity * (forStroke ? style.StrokeOpacity : style.FillOpacity);
        opacity = Math.Clamp(opacity, 0, 1);
        byte a = (byte)Math.Round(color.A * opacity);
        return new Color(a, color.R, color.G, color.B);
    }

    // ──────────────────────────────────────────────
    // Geometry builders
    // ──────────────────────────────────────────────

    private static PathGeometry BuildRectPath(
        double x, double y, double w, double h,
        double rx, double ry,
        SvgMatrix ctm)
    {
        // Clamp corner radii
        rx = Math.Min(rx, w / 2);
        ry = Math.Min(ry, h / 2);

        var path = new PathGeometry();

        if (rx <= 0 || ry <= 0)
        {
            // Simple rectangle: 4 corners
            var (x0, y0) = ctm.Apply(x,     y);
            var (x1, y1) = ctm.Apply(x + w, y);
            var (x2, y2) = ctm.Apply(x + w, y + h);
            var (x3, y3) = ctm.Apply(x,     y + h);
            path.MoveTo(x0, y0);
            path.LineTo(x1, y1);
            path.LineTo(x2, y2);
            path.LineTo(x3, y3);
            path.Close();
        }
        else
        {
            // Rounded rectangle — approximate arcs with cubic Béziers
            // κ ≈ 0.5522847498 for a 90° circular arc
            const double k = 0.5522847498;
            double krx = k * rx;
            double kry = k * ry;

            // Start at top-left after corner
            AppendRoundedRect(path, ctm, x, y, w, h, rx, ry, krx, kry);
        }

        return path;
    }

    private static void AppendRoundedRect(
        PathGeometry path, SvgMatrix ctm,
        double x, double y, double w, double h,
        double rx, double ry, double krx, double kry)
    {
        double r = x + rx, l = x + w - rx;
        double t = y + ry, b = y + h - ry;

        var (sx, sy) = ctm.Apply(r, y);
        path.MoveTo(sx, sy);
        // Top edge + top-right corner
        var (ex, ey) = ctm.Apply(l, y);
        path.LineTo(ex, ey);
        AddCorner(path, ctm, l, y, krx, 0, x + w, y, 0, kry, x + w, t);
        // Right edge + bottom-right corner
        var (fx, fy) = ctm.Apply(x + w, b);
        path.LineTo(fx, fy);
        AddCorner(path, ctm, x + w, b, 0, kry, x + w, y + h, -krx, 0, l, y + h);
        // Bottom edge + bottom-left corner
        var (gx, gy) = ctm.Apply(r, y + h);
        path.LineTo(gx, gy);
        AddCorner(path, ctm, r, y + h, -krx, 0, x, y + h, 0, -kry, x, b);
        // Left edge + top-left corner
        var (hx, hy) = ctm.Apply(x, t);
        path.LineTo(hx, hy);
        AddCorner(path, ctm, x, t, 0, -kry, x, y, krx, 0, r, y);
        path.Close();
    }

    private static void AddCorner(
        PathGeometry path, SvgMatrix ctm,
        double ax, double ay, double adx, double ady,  // control point 1 offset from (ax,ay)
        double bx, double by, double bdx, double bdy,  // control point 2 offset from (bx,by)
        double ex, double ey)                           // end point
    {
        var (c1x, c1y) = ctm.Apply(ax + adx, ay + ady);
        var (c2x, c2y) = ctm.Apply(bx + bdx, by + bdy);
        var (epx, epy) = ctm.Apply(ex, ey);
        path.BezierTo(c1x, c1y, c2x, c2y, epx, epy);
    }

    private static PathGeometry BuildEllipsePath(
        double cx, double cy, double rx, double ry,
        SvgMatrix ctm)
    {
        // Approximate circle/ellipse with 4 cubic Bézier curves
        const double k = 0.5522847498;
        double krx = k * rx;
        double kry = k * ry;

        var path = new PathGeometry();
        var (sx, sy) = ctm.Apply(cx + rx, cy);
        path.MoveTo(sx, sy);

        // Top-right
        var (c1x, c1y) = ctm.Apply(cx + rx, cy - kry);
        var (c2x, c2y) = ctm.Apply(cx + krx, cy - ry);
        var (epx, epy) = ctm.Apply(cx,        cy - ry);
        path.BezierTo(c1x, c1y, c2x, c2y, epx, epy);

        // Top-left
        (c1x, c1y) = ctm.Apply(cx - krx, cy - ry);
        (c2x, c2y) = ctm.Apply(cx - rx,  cy - kry);
        (epx, epy) = ctm.Apply(cx - rx,  cy);
        path.BezierTo(c1x, c1y, c2x, c2y, epx, epy);

        // Bottom-left
        (c1x, c1y) = ctm.Apply(cx - rx,  cy + kry);
        (c2x, c2y) = ctm.Apply(cx - krx, cy + ry);
        (epx, epy) = ctm.Apply(cx,        cy + ry);
        path.BezierTo(c1x, c1y, c2x, c2y, epx, epy);

        // Bottom-right
        (c1x, c1y) = ctm.Apply(cx + krx, cy + ry);
        (c2x, c2y) = ctm.Apply(cx + rx,  cy + kry);
        (epx, epy) = ctm.Apply(cx + rx,  cy);
        path.BezierTo(c1x, c1y, c2x, c2y, epx, epy);

        path.Close();
        return path;
    }
}
