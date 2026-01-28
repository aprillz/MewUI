namespace Aprillz.MewUI.Controls;

internal sealed class ScrollController
{
    private readonly int[] _extentPx = new int[2];
    private readonly int[] _viewportPx = new int[2];
    private readonly int[] _offsetPx = new int[2];

    public double DpiScale
    {
        get;
        set => field = value > 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : 1;
    } = 1;

    public int GetExtentPx(int axis) => axis == 0 ? _extentPx[0] : _extentPx[1];
    public int GetViewportPx(int axis) => axis == 0 ? _viewportPx[0] : _viewportPx[1];
    public int GetOffsetPx(int axis) => axis == 0 ? _offsetPx[0] : _offsetPx[1];

    public double GetExtentDip(int axis) => PxToDip(GetExtentPx(axis));
    public double GetViewportDip(int axis) => PxToDip(GetViewportPx(axis));
    public double GetOffsetDip(int axis) => PxToDip(GetOffsetPx(axis));

    public double GetMaxDip(int axis)
    {
        int maxPx = GetMaxPx(axis);
        return PxToDip(maxPx);
    }

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

    public bool SetOffsetDip(int axis, double offsetDip) => SetOffsetPx(axis, DipToPx(offsetDip));

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

