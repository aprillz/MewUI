using System.Numerics;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// Manages GDI graphics state including save/restore, transform, and clipping.
/// </summary>
internal sealed class GdiStateManager
{
    private readonly nint _hdc;
    private readonly Stack<SavedState> _savedStates = new();

    public Matrix3x2 Transform { get; private set; } = Matrix3x2.Identity;
    public double DpiScale { get; }

    private readonly struct SavedState
    {
        public required int DcState { get; init; }
        public required Matrix3x2 Transform { get; init; }
    }

    public GdiStateManager(nint hdc, double dpiScale)
    {
        _hdc = hdc;
        DpiScale = dpiScale;
    }

    /// <summary>Saves the current graphics state.</summary>
    public void Save()
    {
        int state = Gdi32.SaveDC(_hdc);
        _savedStates.Push(new SavedState { DcState = state, Transform = Transform });
    }

    /// <summary>Restores the previously saved graphics state.</summary>
    public void Restore()
    {
        if (_savedStates.Count > 0)
        {
            var saved = _savedStates.Pop();
            Gdi32.RestoreDC(_hdc, saved.DcState);
            Transform = saved.Transform;
        }
    }

    /// <summary>Sets the clipping region (intersects with the current clip).</summary>
    public void SetClip(Rect rect)
    {
        var r = ToDeviceRect(rect);
        Gdi32.IntersectClipRect(_hdc, r.left, r.top, r.right, r.bottom);
    }

    /// <summary>Sets a rounded-rectangle clipping region.</summary>
    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY)
    {
        if (radiusX <= 0 && radiusY <= 0)
        {
            SetClip(rect);
            return;
        }

        var r = ToDeviceRect(rect);
        int ellipseW = Math.Max(1, QuantizeLengthPx(radiusX * 2));
        int ellipseH = Math.Max(1, QuantizeLengthPx(radiusY * 2));

        var hrgn = Gdi32.CreateRoundRectRgn(r.left, r.top, r.right, r.bottom, ellipseW, ellipseH);
        if (hrgn != 0)
        {
            // RGN_AND (1) intersects the new region with the existing clip.
            Gdi32.ExtSelectClipRgn(_hdc, hrgn, 1);
            Gdi32.DeleteObject(hrgn);
        }
    }

    /// <summary>Removes all clipping, restoring to the full DC surface.</summary>
    public void ResetClip() => Gdi32.SelectClipRgn(_hdc, 0);

    /// <summary>Translates the coordinate system.</summary>
    public void Translate(double dx, double dy)
        => Transform = Matrix3x2.CreateTranslation((float)dx, (float)dy) * Transform;

    /// <summary>Rotates the coordinate system by <paramref name="angleRadians"/> around the current origin.</summary>
    public void Rotate(double angleRadians)
        => Transform = Matrix3x2.CreateRotation((float)angleRadians) * Transform;

    /// <summary>Scales the coordinate system.</summary>
    public void Scale(double sx, double sy)
        => Transform = Matrix3x2.CreateScale((float)sx, (float)sy) * Transform;

    /// <summary>Replaces the current transform with the given matrix.</summary>
    public void SetTransform(Matrix3x2 matrix) => Transform = matrix;

    /// <summary>Resets the transform to identity.</summary>
    public void ResetTransform() => Transform = Matrix3x2.Identity;

    /// <summary>Converts a logical point to device coordinates (integer pixels).</summary>
    public POINT ToDevicePoint(Point pt)
    {
        var v = Vector2.Transform(new Vector2((float)pt.X, (float)pt.Y), Transform);
        return new POINT(
            (int)Math.Round(v.X * DpiScale, MidpointRounding.AwayFromZero),
            (int)Math.Round(v.Y * DpiScale, MidpointRounding.AwayFromZero));
    }

    /// <summary>Converts a logical rectangle to device coordinates (integer pixels).</summary>
    public RECT ToDeviceRect(Rect rect)
    {
        var tl = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Y), Transform);
        var br = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Bottom), Transform);
        return new RECT(
            (int)Math.Round(tl.X * DpiScale, MidpointRounding.AwayFromZero),
            (int)Math.Round(tl.Y * DpiScale, MidpointRounding.AwayFromZero),
            (int)Math.Round(br.X * DpiScale, MidpointRounding.AwayFromZero),
            (int)Math.Round(br.Y * DpiScale, MidpointRounding.AwayFromZero));
    }

    /// <summary>Quantizes a stroke thickness to device pixels.</summary>
    public int QuantizePenWidthPx(double thicknessDip)
    {
        if (thicknessDip <= 0 || double.IsNaN(thicknessDip) || double.IsInfinity(thicknessDip))
            return 0;
        var px = thicknessDip * DpiScale;
        var snapped = (int)Math.Round(px, MidpointRounding.AwayFromZero);
        return Math.Max(1, snapped);
    }

    /// <summary>Quantizes a length to device pixels.</summary>
    public int QuantizeLengthPx(double lengthDip)
    {
        if (lengthDip <= 0 || double.IsNaN(lengthDip) || double.IsInfinity(lengthDip))
            return 0;
        return LayoutRounding.RoundToPixelInt(lengthDip, DpiScale);
    }

    /// <summary>
    /// Transforms a logical point through the current matrix and scales to device pixels.
    /// Returns floating-point for sub-pixel accuracy.
    /// </summary>
    public (double x, double y) ToDeviceCoords(double x, double y)
    {
        var v = Vector2.Transform(new Vector2((float)x, (float)y), Transform);
        return (v.X * DpiScale, v.Y * DpiScale);
    }

    /// <summary>Scales a logical value to device pixels (no matrix transform, only DPI scale).</summary>
    public double ToDevicePx(double logicalValue) => logicalValue * DpiScale;
}
