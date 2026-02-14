namespace Aprillz.MewUI.Rendering.Gdi.Sdf;

/// <summary>
/// SDF calculator for rounded rectangles.
/// </summary>
internal sealed class RoundedRectSdf : SdfCalculatorBase
{
    private readonly float _width;
    private readonly float _height;
    private readonly float _rx;
    private readonly float _ry;
    private readonly float _halfWidth;
    private readonly float _halfHeight;

    /// <summary>
    /// Creates an SDF calculator for a rounded rectangle.
    /// The rectangle is centered at origin (0, 0).
    /// </summary>
    /// <param name="width">Width of the rectangle.</param>
    /// <param name="height">Height of the rectangle.</param>
    /// <param name="rx">Corner radius in X direction.</param>
    /// <param name="ry">Corner radius in Y direction.</param>
    public RoundedRectSdf(float width, float height, float rx, float ry)
    {
        _width = MathF.Max(0.001f, width);
        _height = MathF.Max(0.001f, height);
        _halfWidth = _width / 2f;
        _halfHeight = _height / 2f;

        // Clamp radii to half dimensions
        _rx = Clamp(rx, 0f, _halfWidth);
        _ry = Clamp(ry, 0f, _halfHeight);
    }

    private static float Clamp(float value, float min, float max)
    {
        return MathF.Max(min, MathF.Min(max, value));
    }

    /// <summary>
    /// Creates an SDF calculator for a rounded rectangle with uniform corners.
    /// </summary>
    public static RoundedRectSdf WithUniformRadius(float width, float height, float radius)
    {
        return new RoundedRectSdf(width, height, radius, radius);
    }

    public override float GetSignedDistance(float x, float y)
    {
        // Use symmetry: work in the first quadrant
        float px = MathF.Abs(x);
        float py = MathF.Abs(y);

        // No corner radius - simple box SDF
        if (_rx <= 0.001f && _ry <= 0.001f)
        {
            float dx = px - _halfWidth;
            float dy = py - _halfHeight;

            float outside = MathF.Sqrt(
                MathF.Max(0, dx) * MathF.Max(0, dx) +
                MathF.Max(0, dy) * MathF.Max(0, dy));

            float inside = MathF.Max(dx, dy);

            return inside < 0 ? inside : outside;
        }

        // Uniform corner radius
        if (MathF.Abs(_rx - _ry) < 0.001f)
        {
            return GetUniformRoundedRectSdf(px, py);
        }

        // Non-uniform corner radius (elliptical corners)
        return GetEllipticalRoundedRectSdf(px, py);
    }

    /// <summary>
    /// SDF for rounded rectangle with uniform (circular) corners.
    /// </summary>
    private float GetUniformRoundedRectSdf(float px, float py)
    {
        // Canonical rounded-rectangle SDF (Inigo Quilez style):
        // p = abs(pos) - (halfSize - r)
        // return length(max(p,0)) + min(max(p.x,p.y),0) - r
        float r = _rx;
        float bx = _halfWidth - r;
        float by = _halfHeight - r;

        float qx = px - bx;
        float qy = py - by;

        float ox = MathF.Max(qx, 0);
        float oy = MathF.Max(qy, 0);
        float outside = MathF.Sqrt(ox * ox + oy * oy);
        float inside = MathF.Min(MathF.Max(qx, qy), 0);

        return outside + inside - r;
    }

    /// <summary>
    /// SDF for rounded rectangle with elliptical corners.
    /// </summary>
    private float GetEllipticalRoundedRectSdf(float px, float py)
    {
        // Inner rectangle bounds (without corner radius)
        float innerWidth = _halfWidth - _rx;
        float innerHeight = _halfHeight - _ry;

        // In the corner region (ellipse)
        if (px > innerWidth && py > innerHeight)
        {
            float dx = px - innerWidth;
            float dy = py - innerHeight;

            return GetEllipseCornerDistance(dx, dy, _rx, _ry);
        }

        // On the straight edges or inside
        float distX = px - _halfWidth;
        float distY = py - _halfHeight;

        if (distX < 0 && distY < 0)
        {
            // Inside the rectangle (not in corner)
            return MathF.Max(distX, distY);
        }

        if (px > innerWidth && distY < 0)
        {
            // In right edge zone, past the corner start
            return distX;
        }

        if (py > innerHeight && distX < 0)
        {
            // In bottom edge zone, past the corner start
            return distY;
        }

        return MathF.Max(distX, distY);
    }

    private static float GetEllipseCornerDistance(float dx, float dy, float rx, float ry)
    {
        // Match EllipseSdf's Newton iteration approach (first quadrant).
        rx = MathF.Max(0.001f, rx);
        ry = MathF.Max(0.001f, ry);
        float rx2 = rx * rx;
        float ry2 = ry * ry;

        float px = MathF.Abs(dx);
        float py = MathF.Abs(dy);

        if (rx < 0.001f)
        {
            float d = MathF.Sqrt(px * px + MathF.Max(0, py - ry) * MathF.Max(0, py - ry));
            return d * MathF.Sign(py - ry);
        }

        if (ry < 0.001f)
        {
            float d = MathF.Sqrt(py * py + MathF.Max(0, px - rx) * MathF.Max(0, px - rx));
            return d * MathF.Sign(px - rx);
        }

        float t = MathF.Atan2(ry * py, rx * px);

        for (int i = 0; i < 3; i++)
        {
            float cos = MathF.Cos(t);
            float sin = MathF.Sin(t);

            float ex = rx * cos;
            float ey = ry * sin;

            float ddx = px - ex;
            float ddy = py - ey;

            float dex = -rx * sin;
            float dey = ry * cos;

            float num = ddx * dex + ddy * dey;
            float den = ddx * (-rx * cos) + ddy * (-ry * sin) + dex * dex + dey * dey;

            if (MathF.Abs(den) > 0.0001f)
            {
                t -= num / den;
            }
        }

        float closestX = rx * MathF.Cos(t);
        float closestY = ry * MathF.Sin(t);

        float distX = px - closestX;
        float distY = py - closestY;
        float dist = MathF.Sqrt(distX * distX + distY * distY);

        float normalizedDist = (px * px) / rx2 + (py * py) / ry2;
        return normalizedDist < 1f ? -dist : dist;
    }

    /// <summary>
    /// Fast inside check for the rounded rectangle.
    /// </summary>
    public bool IsInside(float x, float y)
    {
        float px = MathF.Abs(x);
        float py = MathF.Abs(y);

        // Quick bounds check
        if (px > _halfWidth || py > _halfHeight)
        {
            return false;
        }

        // Inner rectangle bounds
        float innerWidth = _halfWidth - _rx;
        float innerHeight = _halfHeight - _ry;

        // If not in corner region, it's inside
        if (px <= innerWidth || py <= innerHeight)
        {
            return true;
        }

        // In corner region - check ellipse
        float dx = px - innerWidth;
        float dy = py - innerHeight;

        if (_rx > 0.001f && _ry > 0.001f)
        {
            float nx = dx / _rx;
            float ny = dy / _ry;
            return nx * nx + ny * ny <= 1f;
        }

        return false;
    }

    public override bool IsInAaZone(float x, float y, float aaWidth)
    {
        // Quick check: if clearly inside or outside, not in AA zone
        float dist = GetSignedDistance(x, y);
        return MathF.Abs(dist) <= aaWidth;
    }

    /// <summary>
    /// Gets the horizontal span at a given Y coordinate.
    /// Returns the left and right X coordinates of the shape boundary.
    /// </summary>
    public void GetSpanAtY(float y, out float xLeft, out float xRight)
    {
        float py = MathF.Abs(y);

        float innerHeight = _halfHeight - _ry;

        // In the straight part
        if (py <= innerHeight)
        {
            xLeft = -_halfWidth;
            xRight = _halfWidth;
            return;
        }

        // In the corner region
        float dy = py - innerHeight;

        if (_ry > 0.001f)
        {
            float t = 1f - (dy * dy) / (_ry * _ry);
            if (t <= 0)
            {
                xLeft = 0;
                xRight = 0;
                return;
            }

            float xOffset = _rx * MathF.Sqrt(t);
            float innerWidth = _halfWidth - _rx;

            xLeft = -(innerWidth + xOffset);
            xRight = innerWidth + xOffset;
        }
        else
        {
            xLeft = -_halfWidth;
            xRight = _halfWidth;
        }
    }
}
