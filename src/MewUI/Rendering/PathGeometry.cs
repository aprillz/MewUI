using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Describes the type of a path command.
/// </summary>
public enum PathCommandType : byte
{
    MoveTo,
    LineTo,
    BezierTo,
    Close,
}

/// <summary>
/// Represents a single command in a <see cref="PathGeometry"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct PathCommand
{
    public PathCommandType Type { get; }

    // MoveTo/LineTo: X0,Y0 = target point
    // BezierTo: X0,Y0 = control point 1; X1,Y1 = control point 2; X2,Y2 = end point
    // Close: (no coordinates used)
    public double X0 { get; }
    public double Y0 { get; }
    public double X1 { get; }
    public double Y1 { get; }
    public double X2 { get; }
    public double Y2 { get; }

    public PathCommand(PathCommandType type, double x0 = 0, double y0 = 0,
        double x1 = 0, double y1 = 0, double x2 = 0, double y2 = 0)
    {
        Type = type;
        X0 = x0; Y0 = y0;
        X1 = x1; Y1 = y1;
        X2 = x2; Y2 = y2;
    }
}

/// <summary>
/// A path consisting of move, line, bezier, and close commands.
/// Coordinates are in device-independent pixels (DIPs) in the local coordinate space.
/// </summary>
public sealed class PathGeometry : IFreezable
{
    // Cubic Bézier approximation constant for a 90° arc: κ ≈ 4*(√2-1)/3
    private const double ArcK = 0.5522847498;

    private readonly List<PathCommand> _commands = new();

    // Current pen position (needed for relative QuadTo, ArcTo continuation).
    private double _lastX;
    private double _lastY;

    private bool _isFrozen;

    /// <inheritdoc/>
    public bool IsFrozen => _isFrozen;

    /// <inheritdoc/>
    public void Freeze() => _isFrozen = true;

    /// <summary>
    /// Gets or sets the fill rule used when this path is filled without an explicit rule.
    /// Defaults to <see cref="FillRule.NonZero"/>.
    /// </summary>
    public FillRule FillRule
    {
        get => _fillRule;
        set
        {
            FreezableHelper.ThrowIfFrozen(this);
            _fillRule = value;
        }
    }
    private FillRule _fillRule = FillRule.NonZero;

    /// <summary>Gets the list of path commands.</summary>
    public ReadOnlySpan<PathCommand> Commands => CollectionsMarshal.AsSpan(_commands);

    /// <summary>Returns <see langword="true"/> when the path contains no commands.</summary>
    public bool IsEmpty => _commands.Count == 0;

    /// <summary>Gets the number of commands in the path.</summary>
    public int Count => _commands.Count;

    // ── Builders ──────────────────────────────────────────────────────────────

    /// <summary>Starts a new sub-path at the given point.</summary>
    public void MoveTo(Point to) => MoveTo(to.X, to.Y);
    /// <summary>Starts a new sub-path at the given point.</summary>
    public void MoveTo(double x, double y)
    {
        FreezableHelper.ThrowIfFrozen(this);
        _commands.Add(new PathCommand(PathCommandType.MoveTo, x, y));
        _lastX = x; _lastY = y;
    }

    /// <summary>Adds a straight line to the given point.</summary>
    public void LineTo(Point to) => LineTo(to.X, to.Y);
    /// <summary>Adds a straight line to the given point.</summary>
    public void LineTo(double x, double y)
    {
        FreezableHelper.ThrowIfFrozen(this);
        _commands.Add(new PathCommand(PathCommandType.LineTo, x, y));
        _lastX = x; _lastY = y;
    }

    /// <summary>Adds a cubic Bézier curve. c1 and c2 are control points; (x, y) is the end point.</summary>
    public void BezierTo(double c1x, double c1y, double c2x, double c2y, double x, double y)
    {
        FreezableHelper.ThrowIfFrozen(this);
        _commands.Add(new PathCommand(PathCommandType.BezierTo, c1x, c1y, c2x, c2y, x, y));
        _lastX = x; _lastY = y;
    }

    /// <summary>Closes the current sub-path with a straight line back to the starting point.</summary>
    public void Close()
    {
        FreezableHelper.ThrowIfFrozen(this);
        _commands.Add(new PathCommand(PathCommandType.Close));
    }

    /// <summary>
    /// Adds a quadratic Bézier curve, eagerly converting to a cubic Bézier.
    /// <paramref name="cpx"/>/<paramref name="cpy"/> is the quadratic control point;
    /// (<paramref name="x"/>, <paramref name="y"/>) is the end point.
    /// </summary>
    public void QuadTo(double cpx, double cpy, double x, double y)
    {
        // Degree elevation: quadratic → cubic
        // cp1 = current + 2/3 * (cp – current)
        // cp2 = end    + 2/3 * (cp – end)
        double c1x = _lastX + 2.0 / 3.0 * (cpx - _lastX);
        double c1y = _lastY + 2.0 / 3.0 * (cpy - _lastY);
        double c2x = x + 2.0 / 3.0 * (cpx - x);
        double c2y = y + 2.0 / 3.0 * (cpy - y);
        BezierTo(c1x, c1y, c2x, c2y, x, y);
    }

    /// <summary>
    /// Adds a circular arc from the current point to (<paramref name="x2"/>, <paramref name="y2"/>)
    /// via the control point (<paramref name="x1"/>, <paramref name="y1"/>)
    /// with the given <paramref name="radius"/>.  This is a canvas-style tangential arc; it draws
    /// the arc of the circle of the given radius that is tangent to both the incoming direction
    /// (from the previous point to the corner) and the outgoing direction (to the end point).
    /// Internally converted to cubic Bézier curves.
    /// </summary>
    public void ArcTo(double x1, double y1, double x2, double y2, double radius)
    {
        double x0 = _lastX, y0 = _lastY;
        if (radius < 1e-6 ||
            (Math.Abs(x0 - x1) < 1e-6 && Math.Abs(y0 - y1) < 1e-6) ||
            (Math.Abs(x1 - x2) < 1e-6 && Math.Abs(y1 - y2) < 1e-6))
        {
            LineTo(x1, y1);
            return;
        }

        // Direction vectors of the two line segments.
        double dx0 = x0 - x1, dy0 = y0 - y1;
        double dx1 = x2 - x1, dy1 = y2 - y1;

        double len0 = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
        double len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
        if (len0 < 1e-6 || len1 < 1e-6) { LineTo(x1, y1); return; }

        dx0 /= len0; dy0 /= len0;
        dx1 /= len1; dy1 /= len1;

        double cosAngle = dx0 * dx1 + dy0 * dy1;
        cosAngle = Math.Clamp(cosAngle, -1.0, 1.0);
        double sinHalf = Math.Sqrt((1.0 - cosAngle) / 2.0);
        if (sinHalf < 1e-6) { LineTo(x1, y1); return; }

        double dist = radius / sinHalf * Math.Sqrt((1.0 + cosAngle) / 2.0);
        dist = Math.Min(dist, Math.Min(len0 * 0.5, len1 * 0.5) / (1e-6 + sinHalf));

        // Tangent points
        double tp0x = x1 + dx0 * dist;
        double tp0y = y1 + dy0 * dist;
        double tp1x = x1 + dx1 * dist;
        double tp1y = y1 + dy1 * dist;

        // Centre of arc
        // Perpendicular to dx0/dy0 pointing inward
        double cross = dx0 * dy1 - dy0 * dx1;
        double perpSign = cross > 0 ? -1.0 : 1.0;
        double cx = tp0x + perpSign * (-dy0) * radius;
        double cy = tp0y + perpSign * (dx0) * radius;

        double startAngle = Math.Atan2(tp0y - cy, tp0x - cx);
        double endAngle = Math.Atan2(tp1y - cy, tp1x - cx);

        LineTo(tp0x, tp0y);
        Arc(cx, cy, radius, radius, startAngle, endAngle, cross > 0);
    }

    /// <summary>
    /// Adds an axis-aligned elliptical arc centred at (<paramref name="cx"/>, <paramref name="cy"/>)
    /// with radii <paramref name="rx"/>/<paramref name="ry"/>, sweeping from
    /// <paramref name="startAngle"/> to <paramref name="endAngle"/> (radians, CCW positive).
    /// Internally converted to cubic Bézier curves.
    /// </summary>
    public void Arc(double cx, double cy, double rx, double ry,
        double startAngle, double endAngle, bool counterClockwise = false)
    {
        // Normalise sweep direction.
        const double TwoPi = Math.PI * 2;
        if (counterClockwise)
        {
            while (endAngle > startAngle) endAngle -= TwoPi;
        }
        else
        {
            while (endAngle < startAngle) endAngle += TwoPi;
        }

        double sweep = endAngle - startAngle;
        if (Math.Abs(sweep) < 1e-10) { LineTo(cx + rx * Math.Cos(endAngle), cy + ry * Math.Sin(endAngle)); return; }

        // Split into segments ≤ π/2
        int n = (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2));
        double stepAngle = sweep / n;

        for (int i = 0; i < n; i++)
        {
            double a0 = startAngle + i * stepAngle;
            double a1 = a0 + stepAngle;
            ArcSegment(cx, cy, rx, ry, a0, a1);
        }
    }

    /// <summary>
    /// Appends an SVG-style arc (as in the 'A'/'a' path command), converting it to one or more
    /// cubic Bézier curves using the standard endpoint-to-center parameterisation.
    /// </summary>
    /// <param name="rx">X radius (absolute).</param>
    /// <param name="ry">Y radius (absolute).</param>
    /// <param name="xRotationDeg">X-axis rotation in degrees.</param>
    /// <param name="largeArc">Whether to take the large arc.</param>
    /// <param name="sweep">Whether to sweep in the positive-angle direction.</param>
    /// <param name="x">End point X.</param>
    /// <param name="y">End point Y.</param>
    public void SvgArcTo(double rx, double ry, double xRotationDeg, bool largeArc, bool sweep, double x, double y)
    {
        double x1 = _lastX, y1 = _lastY;
        double x2 = x, y2 = y;

        if (Math.Abs(x1 - x2) < 1e-10 && Math.Abs(y1 - y2) < 1e-10)
            return; // Degenerate: zero-length arc

        rx = Math.Abs(rx); ry = Math.Abs(ry);
        if (rx < 1e-10 || ry < 1e-10)
        {
            LineTo(x2, y2);
            return;
        }

        double phi = xRotationDeg * (Math.PI / 180.0);
        double cosPhi = Math.Cos(phi);
        double sinPhi = Math.Sin(phi);

        // Step 1: Compute (x1', y1') — rotated midpoint.
        double mx = (x1 - x2) / 2.0;
        double my = (y1 - y2) / 2.0;
        double x1p = cosPhi * mx + sinPhi * my;
        double y1p = -sinPhi * mx + cosPhi * my;

        // Step 2: Clamp radii.
        double x1pSq = x1p * x1p, y1pSq = y1p * y1p;
        double rxSq = rx * rx, rySq = ry * ry;
        double lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1.0)
        {
            double sqrtL = Math.Sqrt(lambda);
            rx *= sqrtL; ry *= sqrtL;
            rxSq = rx * rx; rySq = ry * ry;
        }

        // Step 3: Compute centre in rotated frame.
        double num = rxSq * rySq - rxSq * y1pSq - rySq * x1pSq;
        double den = rxSq * y1pSq + rySq * x1pSq;
        double sq = den > 1e-14 ? Math.Sqrt(Math.Max(0, num / den)) : 0;
        if (largeArc == sweep) sq = -sq;

        double cxp = sq * rx * y1p / ry;
        double cyp = -sq * ry * x1p / rx;

        // Step 4: Compute centre in original frame.
        double cx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2.0;
        double cy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2.0;

        // Step 5: Compute angles.
        double ux = (x1p - cxp) / rx, uy = (y1p - cyp) / ry;
        double vx = (-x1p - cxp) / rx, vy = (-y1p - cyp) / ry;

        double startAngle = Math.Atan2(uy, ux);
        double sweepAngle = AngleBetween(ux, uy, vx, vy);

        if (!sweep && sweepAngle > 0) sweepAngle -= 2 * Math.PI;
        else if (sweep && sweepAngle < 0) sweepAngle += 2 * Math.PI;

        // Step 6: Split into ≤ π/2 segments in the rotated frame and output Béziers.
        int n = (int)Math.Ceiling(Math.Abs(sweepAngle) / (Math.PI / 2));
        if (n < 1) n = 1;
        double stepAngle = sweepAngle / n;

        for (int i = 0; i < n; i++)
        {
            double a0 = startAngle + i * stepAngle;
            double a1 = a0 + stepAngle;
            // Arc segment in rotated frame then un-rotate.
            ArcSegmentRotated(cx, cy, rx, ry, cosPhi, sinPhi, a0, a1);
        }
    }

    // ── Mutation ──────────────────────────────────────────────────────────────

    /// <summary>Removes all commands from the path.</summary>
    public void Clear()
    {
        FreezableHelper.ThrowIfFrozen(this);
        _commands.Clear();
        _lastX = _lastY = 0;
    }

    /// <summary>
    /// Appends all commands from <paramref name="other"/> to the end of this path.
    /// The <see cref="FillRule"/> of this path is not changed.
    /// </summary>
    public void AddPath(PathGeometry other)
    {
        FreezableHelper.ThrowIfFrozen(this);
        _commands.AddRange(other._commands);
        // Update _lastX/Y from the last command in other (if any).
        if (other._commands.Count > 0)
        {
            _lastX = other._lastX;
            _lastY = other._lastY;
        }
    }

    // ── Static factories ──────────────────────────────────────────────────────

    /// <summary>Creates a closed rectangular path.</summary>
    public static PathGeometry FromRect(Rect rect)
        => FromRect(rect.X, rect.Y, rect.Width, rect.Height);

    /// <summary>Creates a closed rectangular path.</summary>
    public static PathGeometry FromRect(double x, double y, double width, double height)
    {
        var p = new PathGeometry();
        p.MoveTo(x, y);
        p.LineTo(x + width, y);
        p.LineTo(x + width, y + height);
        p.LineTo(x, y + height);
        p.Close();
        p.Freeze();
        return p;
    }

    /// <summary>Creates a closed rounded-rectangle path with equal corner radii.</summary>
    public static PathGeometry FromRoundedRect(Rect rect, double radius)
        => FromRoundedRect(rect.X, rect.Y, rect.Width, rect.Height,
            radius, radius, radius, radius, radius, radius, radius, radius);

    /// <summary>Creates a closed rounded-rectangle path with independent X/Y corner radii.</summary>
    public static PathGeometry FromRoundedRect(Rect rect, double radiusX, double radiusY)
        => FromRoundedRect(rect.X, rect.Y, rect.Width, rect.Height,
            radiusX, radiusY, radiusX, radiusY, radiusX, radiusY, radiusX, radiusY);

    /// <summary>
    /// Creates a closed rounded-rectangle path with four independently specified corner radii.
    /// Corner radii are specified as (topLeft, topRight, bottomRight, bottomLeft) X radii;
    /// Y radii default to the same value as X radii.
    /// </summary>
    public static PathGeometry FromRoundedRect(
        Rect rect,
        double topLeftRadius, double topRightRadius,
        double bottomRightRadius, double bottomLeftRadius)
        => FromRoundedRect(
            rect.X, rect.Y, rect.Width, rect.Height,
            topLeftRadius, topLeftRadius,
            topRightRadius, topRightRadius,
            bottomRightRadius, bottomRightRadius,
            bottomLeftRadius, bottomLeftRadius);

    /// <summary>Creates a closed rounded-rectangle path with independent X/Y corner radii.</summary>
    public static PathGeometry FromRoundedRect(
        double x, double y, double w, double h, double rx, double ry)
        => FromRoundedRect(x, y, w, h, rx, ry, rx, ry, rx, ry, rx, ry);

    /// <summary>
    /// Creates a closed rounded-rectangle path with four independent per-corner radii.
    /// </summary>
    public static PathGeometry FromRoundedRect(
        double x, double y, double w, double h,
        double tlx, double tly,   // top-left
        double trx, double tRy,   // top-right
        double brx, double bry,   // bottom-right
        double blx, double bly)   // bottom-left
    {
        // Clamp each radius so corners never overlap.
        tlx = Math.Min(tlx, w / 2); tly = Math.Min(tly, h / 2);
        trx = Math.Min(trx, w / 2); tRy = Math.Min(tRy, h / 2);
        brx = Math.Min(brx, w / 2); bry = Math.Min(bry, h / 2);
        blx = Math.Min(blx, w / 2); bly = Math.Min(bly, h / 2);

        double ktlx = tlx * ArcK, ktly = tly * ArcK;
        double ktrx = trx * ArcK, ktry = tRy * ArcK;
        double kbrx = brx * ArcK, kbry = bry * ArcK;
        double kblx = blx * ArcK, kbly = bly * ArcK;

        var p = new PathGeometry();
        // Top edge
        p.MoveTo(x + tlx, y);
        p.LineTo(x + w - trx, y);
        // Top-right corner
        p.BezierTo(x + w - trx + ktrx, y, x + w, y + ktry, x + w, y + tRy);
        // Right edge
        p.LineTo(x + w, y + h - bry);
        // Bottom-right corner
        p.BezierTo(x + w, y + h - bry + kbry, x + w - brx + kbrx, y + h, x + w - brx, y + h);
        // Bottom edge
        p.LineTo(x + blx, y + h);
        // Bottom-left corner
        p.BezierTo(x + blx - kblx, y + h, x, y + h - bly + kbly, x, y + h - bly);
        // Left edge
        p.LineTo(x, y + tly);
        // Top-left corner
        p.BezierTo(x, y + tly - ktly, x + tlx - ktlx, y, x + tlx, y);
        p.Close();
        p.Freeze();
        return p;
    }

    /// <summary>Creates a closed ellipse path fitted inside <paramref name="bounds"/>.</summary>
    public static PathGeometry FromEllipse(Rect bounds)
        => FromEllipse(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2,
                       bounds.Width / 2, bounds.Height / 2);

    /// <summary>Creates a closed ellipse path with the given center and radii.</summary>
    public static PathGeometry FromEllipse(double cx, double cy, double rx, double ry)
    {
        double kx = rx * ArcK;
        double ky = ry * ArcK;

        var p = new PathGeometry();
        p.MoveTo(cx + rx, cy);
        p.BezierTo(cx + rx, cy - ky, cx + kx, cy - ry, cx, cy - ry);
        p.BezierTo(cx - kx, cy - ry, cx - rx, cy - ky, cx - rx, cy);
        p.BezierTo(cx - rx, cy + ky, cx - kx, cy + ry, cx, cy + ry);
        p.BezierTo(cx + kx, cy + ry, cx + rx, cy + ky, cx + rx, cy);
        p.Close();
        p.Freeze();
        return p;
    }

    /// <summary>Creates a closed circle path with the given center and radius.</summary>
    public static PathGeometry FromCircle(double cx, double cy, double r)
        => FromEllipse(cx, cy, r, r);

    // ── Private arc helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Emits one cubic Bézier for a small arc (≤ π/2) of an axis-aligned ellipse
    /// from angle <paramref name="a0"/> to <paramref name="a1"/>.
    /// </summary>
    private void ArcSegment(double cx, double cy, double rx, double ry, double a0, double a1)
    {
        // Bézier approximation constant for the arc angle.
        double k = 4.0 / 3.0 * Math.Tan((a1 - a0) / 4.0);

        double cos0 = Math.Cos(a0), sin0 = Math.Sin(a0);
        double cos1 = Math.Cos(a1), sin1 = Math.Sin(a1);

        double p0x = cx + rx * cos0, p0y = cy + ry * sin0;
        double p3x = cx + rx * cos1, p3y = cy + ry * sin1;

        double c1x = p0x - k * rx * sin0, c1y = p0y + k * ry * cos0;
        double c2x = p3x + k * rx * sin1, c2y = p3y - k * ry * cos1;

        // Position at the arc start.  Skip the LineTo when the current point
        // is already (nearly) at p0 — this avoids a micro-segment caused by
        // the atan2→cos/sin roundtrip in ArcTo, which would create spurious
        // stroke joins in some backends (especially GDI+).
        if (_commands.Count == 0 || _commands[^1].Type == PathCommandType.Close)
        {
            MoveTo(p0x, p0y);
        }
        else
        {
            double dx = p0x - _lastX, dy = p0y - _lastY;
            if (dx * dx + dy * dy > 1e-6)
                LineTo(p0x, p0y);
        }

        BezierTo(c1x, c1y, c2x, c2y, p3x, p3y);
    }

    /// <summary>
    /// Like <see cref="ArcSegment"/> but with an additional rotation applied (for SVG arcs).
    /// </summary>
    private void ArcSegmentRotated(
        double cx, double cy, double rx, double ry,
        double cosPhi, double sinPhi,
        double a0, double a1)
    {
        double k = 4.0 / 3.0 * Math.Tan((a1 - a0) / 4.0);

        double cos0 = Math.Cos(a0), sin0 = Math.Sin(a0);
        double cos1 = Math.Cos(a1), sin1 = Math.Sin(a1);

        // Points and tangent handles in the ellipse's own frame.
        double ex0 = rx * cos0, ey0 = ry * sin0;
        double ex3 = rx * cos1, ey3 = ry * sin1;
        double ec1x = ex0 - k * rx * sin0, ec1y = ey0 + k * ry * cos0;
        double ec2x = ex3 + k * rx * sin1, ec2y = ey3 - k * ry * cos1;

        // Rotate and translate to world space.
        double p0x = cosPhi * ex0 - sinPhi * ey0 + cx;
        double p0y = sinPhi * ex0 + cosPhi * ey0 + cy;
        double p3x = cosPhi * ex3 - sinPhi * ey3 + cx;
        double p3y = sinPhi * ex3 + cosPhi * ey3 + cy;
        double c1x = cosPhi * ec1x - sinPhi * ec1y + cx;
        double c1y = sinPhi * ec1x + cosPhi * ec1y + cy;
        double c2x = cosPhi * ec2x - sinPhi * ec2y + cx;
        double c2y = sinPhi * ec2x + cosPhi * ec2y + cy;

        if (_commands.Count == 0 || _commands[^1].Type == PathCommandType.Close)
        {
            MoveTo(p0x, p0y);
        }
        else
        {
            double dx = p0x - _lastX, dy = p0y - _lastY;
            if (dx * dx + dy * dy > 1e-6)
                LineTo(p0x, p0y);
        }

        BezierTo(c1x, c1y, c2x, c2y, p3x, p3y);
    }

    private static double AngleBetween(double ux, double uy, double vx, double vy)
    {
        double dot = ux * vx + uy * vy;
        double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        double cos = len > 1e-14 ? Math.Clamp(dot / len, -1.0, 1.0) : 0;
        double angle = Math.Acos(cos);
        return ux * vy - uy * vx < 0 ? -angle : angle;
    }
}
