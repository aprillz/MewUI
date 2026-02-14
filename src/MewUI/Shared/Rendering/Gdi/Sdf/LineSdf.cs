namespace Aprillz.MewUI.Rendering.Gdi.Sdf;

/// <summary>
/// SDF calculator for line segments with thickness.
/// </summary>
internal sealed class LineSdf : SdfCalculatorBase
{
    private readonly float _ax, _ay;
    private readonly float _bx, _by;
    private readonly float _halfThickness;
    private readonly float _vx, _vy;
    private readonly float _lenSq;

    /// <summary>
    /// Creates an SDF calculator for a line segment.
    /// </summary>
    /// <param name="ax">Start X coordinate.</param>
    /// <param name="ay">Start Y coordinate.</param>
    /// <param name="bx">End X coordinate.</param>
    /// <param name="by">End Y coordinate.</param>
    /// <param name="thickness">Line thickness (stroke width).</param>
    public LineSdf(float ax, float ay, float bx, float by, float thickness)
    {
        _ax = ax;
        _ay = ay;
        _bx = bx;
        _by = by;
        _halfThickness = MathF.Max(0.5f, thickness / 2f);

        _vx = bx - ax;
        _vy = by - ay;
        _lenSq = _vx * _vx + _vy * _vy;

        // Check for axis alignment
        IsHorizontal = MathF.Abs(_vy) < 0.0001f;
        IsVertical = MathF.Abs(_vx) < 0.0001f;
        IsAxisAligned = IsHorizontal || IsVertical;
    }

    /// <summary>
    /// Gets whether this line is axis-aligned (horizontal or vertical).
    /// Axis-aligned lines can use faster rendering paths.
    /// </summary>
    public bool IsAxisAligned { get; }

    /// <summary>
    /// Gets whether this line is horizontal.
    /// </summary>
    public bool IsHorizontal { get; }

    /// <summary>
    /// Gets whether this line is vertical.
    /// </summary>
    public bool IsVertical { get; }

    public override float GetSignedDistance(float x, float y)
    {
        // Calculate distance from point to line segment
        float distToSegment = DistanceToSegment(x, y);

        // The SDF is the distance minus half thickness
        return distToSegment - _halfThickness;
    }

    /// <summary>
    /// Calculates the distance from a point to the line segment.
    /// </summary>
    private float DistanceToSegment(float px, float py)
    {
        // Degenerate case: point
        if (_lenSq <= float.Epsilon)
        {
            float ddx = px - _ax;
            float ddy = py - _ay;
            return MathF.Sqrt(ddx * ddx + ddy * ddy);
        }

        // Vector from start to point
        float wx = px - _ax;
        float wy = py - _ay;

        // Project point onto line, clamped to segment
        float t = (wx * _vx + wy * _vy) / _lenSq;

        if (t <= 0)
        {
            // Closest to start point
            return MathF.Sqrt(wx * wx + wy * wy);
        }

        if (t >= 1)
        {
            // Closest to end point
            float dx = px - _bx;
            float dy = py - _by;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        // Closest to interior of segment
        float cx = _ax + t * _vx;
        float cy = _ay + t * _vy;
        float dx2 = px - cx;
        float dy2 = py - cy;
        return MathF.Sqrt(dx2 * dx2 + dy2 * dy2);
    }

    /// <summary>
    /// Calculates the squared distance from a point to the line segment.
    /// Useful for avoiding sqrt in inner loops.
    /// </summary>
    public float DistanceSqToSegment(float px, float py)
    {
        if (_lenSq <= float.Epsilon)
        {
            float ddx = px - _ax;
            float ddy = py - _ay;
            return ddx * ddx + ddy * ddy;
        }

        float wx = px - _ax;
        float wy = py - _ay;

        float t = (wx * _vx + wy * _vy) / _lenSq;

        if (t <= 0)
        {
            return wx * wx + wy * wy;
        }

        if (t >= 1)
        {
            float dx = px - _bx;
            float dy = py - _by;
            return dx * dx + dy * dy;
        }

        float cx = _ax + t * _vx;
        float cy = _ay + t * _vy;
        float dx2 = px - cx;
        float dy2 = py - cy;
        return dx2 * dx2 + dy2 * dy2;
    }

    /// <summary>
    /// Gets the bounding box of the line (including thickness and padding).
    /// </summary>
    public void GetBounds(float padding, out float minX, out float minY, out float maxX, out float maxY)
    {
        float expand = _halfThickness + padding;

        minX = MathF.Min(_ax, _bx) - expand;
        minY = MathF.Min(_ay, _by) - expand;
        maxX = MathF.Max(_ax, _bx) + expand;
        maxY = MathF.Max(_ay, _by) + expand;
    }

    /// <summary>
    /// Gets the integer bounding box suitable for pixel operations.
    /// </summary>
    public void GetPixelBounds(float padding, out int left, out int top, out int right, out int bottom)
    {
        GetBounds(padding, out var minX, out var minY, out var maxX, out var maxY);

        left = (int)MathF.Floor(minX);
        top = (int)MathF.Floor(minY);
        right = (int)MathF.Ceiling(maxX);
        bottom = (int)MathF.Ceiling(maxY);
    }

    public override bool IsInAaZone(float x, float y, float aaWidth)
    {
        float dist = DistanceToSegment(x, y);
        float innerDist = _halfThickness - aaWidth;
        float outerDist = _halfThickness + aaWidth;

        return dist >= innerDist && dist <= outerDist;
    }

    /// <summary>
    /// Checks if a point is inside the thick line (within half thickness of segment).
    /// </summary>
    public bool IsInside(float x, float y)
    {
        float distSq = DistanceSqToSegment(x, y);
        return distSq <= _halfThickness * _halfThickness;
    }
}
