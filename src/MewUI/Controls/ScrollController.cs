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
    // Canonical offsets are stored in DIPs so DPI changes preserve the logical scroll position.
    // We still compute pixel offsets on-demand for stable rounding when interacting with bars.
    private readonly double[] _offsetDip = new double[2];

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
    public int GetOffsetPx(int axis) => DipToPx(GetOffsetDip(axis));

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
    public double GetOffsetDip(int axis) => axis == 0 ? _offsetDip[0] : _offsetDip[1];

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
        // Clamp existing logical offset against the new metrics.
        SetOffsetDip(axis, GetOffsetDip(axis));
    }

    /// <summary>
    /// Sets the scroll offset in DIPs (clamped).
    /// </summary>
    public bool SetOffsetDip(int axis, double offsetDip)
    {
        if (axis != 0 && axis != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }

        if (double.IsNaN(offsetDip) || double.IsInfinity(offsetDip) || offsetDip <= 0)
        {
            offsetDip = 0;
        }

        double maxDip = GetMaxDip(axis);
        if (offsetDip >= maxDip)
        {
            offsetDip = maxDip;
        }

        double current = GetOffsetDip(axis);
        if (current.Equals(offsetDip))
        {
            return false;
        }

        _offsetDip[axis] = offsetDip;
        return true;
    }

    /// <summary>
    /// Sets the scroll offset in pixels (clamped).
    /// </summary>
    public bool SetOffsetPx(int axis, int offsetPx)
    {
        // Convert the pixel offset to a DIP offset for canonical storage.
        return SetOffsetDip(axis, PxToDip(offsetPx));
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

    private int DipToPx(double dip) => LayoutRounding.RoundToPixelInt(dip, DpiScale);

    private double PxToDip(int px) => px / DpiScale;
}
