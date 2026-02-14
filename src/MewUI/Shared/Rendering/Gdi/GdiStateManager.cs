using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// Manages GDI graphics state including save/restore and clipping.
/// </summary>
internal sealed class GdiStateManager
{
    private readonly nint _hdc;
    private readonly Stack<SavedState> _savedStates = new();

    public double TranslateX { get; private set; }
    public double TranslateY { get; private set; }
    public double DpiScale { get; }

    private readonly struct SavedState
    {
        public required int DcState { get; init; }
        public required double TranslateX { get; init; }
        public required double TranslateY { get; init; }
    }

    public GdiStateManager(nint hdc, double dpiScale)
    {
        _hdc = hdc;
        DpiScale = dpiScale;
    }

    /// <summary>
    /// Saves the current graphics state.
    /// </summary>
    public void Save()
    {
        int state = Gdi32.SaveDC(_hdc);
        _savedStates.Push(new SavedState
        {
            DcState = state,
            TranslateX = TranslateX,
            TranslateY = TranslateY,
        });
    }

    /// <summary>
    /// Restores the previously saved graphics state.
    /// </summary>
    public void Restore()
    {
        if (_savedStates.Count > 0)
        {
            var saved = _savedStates.Pop();
            Gdi32.RestoreDC(_hdc, saved.DcState);
            TranslateX = saved.TranslateX;
            TranslateY = saved.TranslateY;
        }
    }

    /// <summary>
    /// Sets the clipping region.
    /// </summary>
    public void SetClip(Rect rect)
    {
        var r = ToDeviceRect(rect);
        Gdi32.IntersectClipRect(_hdc, r.left, r.top, r.right, r.bottom);
    }

    /// <summary>
    /// Translates the origin of the coordinate system.
    /// </summary>
    public void Translate(double dx, double dy)
    {
        TranslateX += dx;
        TranslateY += dy;
    }

    /// <summary>
    /// Resets the translation to zero.
    /// </summary>
    public void ResetTranslation()
    {
        TranslateX = 0;
        TranslateY = 0;
    }

    /// <summary>
    /// Converts a logical point to device coordinates.
    /// </summary>
    public POINT ToDevicePoint(Point pt)
    {
        var (x, y) = RenderingUtil.ToDevicePoint(pt, TranslateX, TranslateY, DpiScale);
        return new POINT(x, y);
    }

    /// <summary>
    /// Converts a logical rectangle to device coordinates.
    /// </summary>
    public RECT ToDeviceRect(Rect rect)
    {
        var (left, top, right, bottom) = RenderingUtil.ToDeviceRect(rect, TranslateX, TranslateY, DpiScale);
        return new RECT(left, top, right, bottom);
    }

    /// <summary>
    /// Quantizes a thickness value to device pixels.
    /// </summary>
    public int QuantizePenWidthPx(double thicknessDip)
    {
        if (thicknessDip <= 0 || double.IsNaN(thicknessDip) || double.IsInfinity(thicknessDip))
        {
            return 0;
        }

        var px = thicknessDip * DpiScale;
        var snapped = (int)Math.Round(px, MidpointRounding.AwayFromZero);
        return Math.Max(1, snapped);
    }

    /// <summary>
    /// Quantizes a length value to device pixels.
    /// </summary>
    public int QuantizeLengthPx(double lengthDip)
    {
        if (lengthDip <= 0 || double.IsNaN(lengthDip) || double.IsInfinity(lengthDip))
        {
            return 0;
        }

        return LayoutRounding.RoundToPixelInt(lengthDip, DpiScale);
    }

    /// <summary>
    /// Converts logical coordinates to device coordinates (double precision).
    /// </summary>
    public (double x, double y) ToDeviceCoords(double x, double y)
    {
        return ((x + TranslateX) * DpiScale, (y + TranslateY) * DpiScale);
    }

    /// <summary>
    /// Converts a logical value to device pixels.
    /// </summary>
    public double ToDevicePx(double logicalValue) => logicalValue * DpiScale;
}
