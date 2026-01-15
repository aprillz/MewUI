using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// Manages GDI graphics state including save/restore and clipping.
/// </summary>
internal sealed class GdiStateManager
{
    private readonly nint _hdc;
    private readonly Stack<int> _savedStates = new();
    private double _translateX;
    private double _translateY;

    public double TranslateX => _translateX;
    public double TranslateY => _translateY;
    public double DpiScale { get; }

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
        _savedStates.Push(state);
    }

    /// <summary>
    /// Restores the previously saved graphics state.
    /// </summary>
    public void Restore()
    {
        if (_savedStates.Count > 0)
        {
            int state = _savedStates.Pop();
            Gdi32.RestoreDC(_hdc, state);
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
        _translateX += dx;
        _translateY += dy;
    }

    /// <summary>
    /// Resets the translation to zero.
    /// </summary>
    public void ResetTranslation()
    {
        _translateX = 0;
        _translateY = 0;
    }

    /// <summary>
    /// Converts a logical point to device coordinates.
    /// </summary>
    public POINT ToDevicePoint(Point pt) => new POINT(
        LayoutRounding.RoundToPixelInt(pt.X + _translateX, DpiScale),
        LayoutRounding.RoundToPixelInt(pt.Y + _translateY, DpiScale)
    );

    /// <summary>
    /// Converts a logical rectangle to device coordinates.
    /// </summary>
    public RECT ToDeviceRect(Rect rect) => RECT.FromLTRB(
        LayoutRounding.RoundToPixelInt(rect.X + _translateX, DpiScale),
        LayoutRounding.RoundToPixelInt(rect.Y + _translateY, DpiScale),
        LayoutRounding.RoundToPixelInt(rect.Right + _translateX, DpiScale),
        LayoutRounding.RoundToPixelInt(rect.Bottom + _translateY, DpiScale)
    );

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
        return ((x + _translateX) * DpiScale, (y + _translateY) * DpiScale);
    }

    /// <summary>
    /// Converts a logical value to device pixels.
    /// </summary>
    public double ToDevicePx(double logicalValue) => logicalValue * DpiScale;
}
