namespace Aprillz.MewUI.Controls;

/// <summary>
/// Maintains scroll metrics (extent/viewport/offset) and provides clamped scrolling helpers.
/// </summary>
/// <remarks>
/// Axis convention: 0 = horizontal (X), 1 = vertical (Y). Values are stored in pixels for stable layout rounding.
/// </remarks>
internal sealed class ScrollController
{
    private readonly int[] _extentPx = new int[2];
    private readonly int[] _viewportPx = new int[2];
    private readonly int[] _offsetPx = new int[2];

    /// <summary>
    /// Gets or sets the current DPI scale factor used for DIP↔pixel conversion.
    /// </summary>
    public double DpiScale
    {
        get;
        set => field = value > 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : 1;
    } = 1;

    /// <summary>
    /// Gets the extent in pixels for the specified axis.
    /// </summary>
    public int GetExtentPx(int axis) => axis == 0 ? _extentPx[0] : _extentPx[1];

    /// <summary>
    /// Gets the viewport size in pixels for the specified axis.
    /// </summary>
    public int GetViewportPx(int axis) => axis == 0 ? _viewportPx[0] : _viewportPx[1];

    /// <summary>
    /// Gets the scroll offset in pixels for the specified axis.
    /// </summary>
    public int GetOffsetPx(int axis) => axis == 0 ? _offsetPx[0] : _offsetPx[1];

    /// <summary>
    /// Gets the extent in DIPs for the specified axis.
    /// </summary>
    public double GetExtentDip(int axis) => PxToDip(GetExtentPx(axis));

    /// <summary>
    /// Gets the viewport size in DIPs for the specified axis.
    /// </summary>
    public double GetViewportDip(int axis) => PxToDip(GetViewportPx(axis));

    /// <summary>
    /// Gets the scroll offset in DIPs for the specified axis.
    /// </summary>
    public double GetOffsetDip(int axis) => PxToDip(GetOffsetPx(axis));

    /// <summary>
    /// Gets the maximum scroll offset in DIPs for the specified axis.
    /// </summary>
    public double GetMaxDip(int axis)
    {
        int maxPx = GetMaxPx(axis);
        return PxToDip(maxPx);
    }

    /// <summary>
    /// Gets the maximum scroll offset in pixels for the specified axis.
    /// </summary>
    public int GetMaxPx(int axis)
    {
        int extent = GetExtentPx(axis);
        int viewport = GetViewportPx(axis);
        return Math.Max(0, extent - viewport);
    }

    public void SetMetricsDip(int axis, double extentDip, double viewportDip)
    {
        int extentPx = DipToPx(extentDip);
        int viewportPx = DipToPx(viewportDip);
        SetMetricsPx(axis, extentPx, viewportPx);
    }

    /// <summary>
    /// Sets extent and viewport metrics in pixels and clamps the existing offset.
    /// </summary>
    public void SetMetricsPx(int axis, int extentPx, int viewportPx)
    {
        if (axis != 0 && axis != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }

        _extentPx[axis] = Math.Max(0, extentPx);
        _viewportPx[axis] = Math.Max(0, viewportPx);
        _offsetPx[axis] = ClampOffsetPx(axis, _offsetPx[axis]);
    }

    /// <summary>
    /// Sets the scroll offset in DIPs (clamped).
    /// </summary>
    public bool SetOffsetDip(int axis, double offsetDip) => SetOffsetPx(axis, DipToPx(offsetDip));

    /// <summary>
    /// Sets the scroll offset in pixels (clamped).
    /// </summary>
    public bool SetOffsetPx(int axis, int offsetPx)
    {
        if (axis != 0 && axis != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }

        int clamped = ClampOffsetPx(axis, offsetPx);
        if (_offsetPx[axis] == clamped)
        {
            return false;
        }

        _offsetPx[axis] = clamped;
        return true;
    }

    /// <summary>
    /// Scrolls by a number of mouse-wheel notches (clamped).
    /// </summary>
    public bool ScrollByNotches(int axis, int notches, double stepDip)
    {
        if (notches == 0)
        {
            return false;
        }

        int stepPx = Math.Max(1, DipToPx(stepDip));
        int deltaPx = checked(notches * stepPx);
        return SetOffsetPx(axis, checked(GetOffsetPx(axis) + deltaPx));
    }

    private int ClampOffsetPx(int axis, int valuePx)
    {
        if (valuePx <= 0)
        {
            return 0;
        }

        int max = GetMaxPx(axis);
        if (valuePx >= max)
        {
            return max;
        }

        return valuePx;
    }

    private int DipToPx(double dip) => LayoutRounding.RoundToPixelInt(dip, DpiScale);

    private double PxToDip(int px) => px / DpiScale;
}
