namespace Aprillz.MewUI.Rendering.Gdi.Sdf;

/// <summary>
/// SDF calculator for ellipses.
/// The ellipse is centered at (cx, cy) with radii (rx, ry).
/// </summary>
internal sealed class EllipseSdf : SdfCalculatorBase
{
    private readonly float _cx;
    private readonly float _cy;
    private readonly float _rx;
    private readonly float _ry;
    private readonly float _rx2;
    private readonly float _ry2;

    /// <summary>
    /// Creates an SDF calculator for an ellipse.
    /// </summary>
    /// <param name="cx">Center X coordinate.</param>
    /// <param name="cy">Center Y coordinate.</param>
    /// <param name="rx">Radius in X direction.</param>
    /// <param name="ry">Radius in Y direction.</param>
    public EllipseSdf(float cx, float cy, float rx, float ry)
    {
        _cx = cx;
        _cy = cy;
        _rx = MathF.Max(0.001f, rx);
        _ry = MathF.Max(0.001f, ry);
        _rx2 = _rx * _rx;
        _ry2 = _ry * _ry;
    }

    /// <summary>
    /// Creates an SDF calculator for an ellipse from bounds.
    /// </summary>
    public static EllipseSdf FromBounds(float left, float top, float width, float height)
    {
        return new EllipseSdf(
            left + width / 2f,
            top + height / 2f,
            width / 2f,
            height / 2f);
    }

    public override float GetSignedDistance(float x, float y)
    {
        // For circles (rx == ry), use simple distance formula
        if (MathF.Abs(_rx - _ry) < 0.001f)
        {
            float dx = x - _cx;
            float dy = y - _cy;
            return MathF.Sqrt(dx * dx + dy * dy) - _rx;
        }

        // For ellipses, use approximate SDF
        // This uses Inigo Quilez's ellipse SDF approximation
        return GetEllipseSdfApproximate(x, y);
    }

    /// <summary>
    /// Approximate SDF for ellipse using iterative method.
    /// Based on Inigo Quilez's technique.
    /// </summary>
    private float GetEllipseSdfApproximate(float x, float y)
    {
        // Translate to ellipse center and take absolute values (exploit symmetry)
        float px = MathF.Abs(x - _cx);
        float py = MathF.Abs(y - _cy);

        // Handle degenerate cases
        if (_rx < 0.001f)
        {
            return MathF.Sqrt(px * px + MathF.Max(0, py - _ry) * MathF.Max(0, py - _ry)) * MathF.Sign(py - _ry);
        }

        if (_ry < 0.001f)
        {
            return MathF.Sqrt(py * py + MathF.Max(0, px - _rx) * MathF.Max(0, px - _rx)) * MathF.Sign(px - _rx);
        }

        // Use Newton iteration to find closest point on ellipse
        // p(t) = (rx * cos(t), ry * sin(t))
        // We want to minimize |p(t) - (px, py)|²

        float t = MathF.Atan2(_ry * py, _rx * px);

        // Newton iterations (2-3 iterations are usually enough)
        for (int i = 0; i < 3; i++)
        {
            float cos = MathF.Cos(t);
            float sin = MathF.Sin(t);

            float ex = _rx * cos;
            float ey = _ry * sin;

            float dx = px - ex;
            float dy = py - ey;

            float dex = -_rx * sin;
            float dey = _ry * cos;

            float num = dx * dex + dy * dey;
            float den = dx * (-_rx * cos) + dy * (-_ry * sin) + dex * dex + dey * dey;

            if (MathF.Abs(den) > 0.0001f)
            {
                t -= num / den;
            }
        }

        float closestX = _rx * MathF.Cos(t);
        float closestY = _ry * MathF.Sin(t);

        float distX = px - closestX;
        float distY = py - closestY;
        float dist = MathF.Sqrt(distX * distX + distY * distY);

        // Determine sign: inside or outside
        float normalizedDist = (px * px) / _rx2 + (py * py) / _ry2;
        return normalizedDist < 1f ? -dist : dist;
    }

    /// <summary>
    /// Fast approximate inside/outside check.
    /// </summary>
    public bool IsInside(float x, float y)
    {
        float dx = x - _cx;
        float dy = y - _cy;
        return (dx * dx) / _rx2 + (dy * dy) / _ry2 <= 1f;
    }

    public override bool IsInAaZone(float x, float y, float aaWidth)
    {
        // Quick check using normalized distance
        float dx = x - _cx;
        float dy = y - _cy;
        float normalizedDist = (dx * dx) / _rx2 + (dy * dy) / _ry2;

        // Approximate AA zone check
        float minR = MathF.Min(_rx, _ry);
        float relAa = aaWidth / minR;

        float inner = (1f - relAa) * (1f - relAa);
        float outer = (1f + relAa) * (1f + relAa);

        return normalizedDist >= inner && normalizedDist <= outer;
    }
}
