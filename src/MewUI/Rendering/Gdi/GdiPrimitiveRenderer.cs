using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Rendering.Gdi.Core;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// Renders basic GDI primitives without anti-aliasing.
/// Used for fast-path rendering of simple shapes.
/// </summary>
internal sealed class GdiPrimitiveRenderer : IDisposable
{
    private readonly nint _hdc;
    private readonly GdiStateManager _stateManager;
    private readonly GdiResourceCache _resourceCache;
    private bool _disposed;

    public GdiPrimitiveRenderer(nint hdc, GdiStateManager stateManager)
    {
        _hdc = hdc;
        _stateManager = stateManager;
        _resourceCache = new GdiResourceCache();
    }

    /// <summary>
    /// Clears the entire client area with the specified color.
    /// </summary>
    public void Clear(nint hwnd, Color color)
    {
        var brush = Gdi32.CreateSolidBrush(color.ToCOLORREF());
        try
        {
            User32.GetClientRect(hwnd, out var rect);
            Gdi32.FillRect(_hdc, ref rect, brush);
        }
        finally
        {
            Gdi32.DeleteObject(brush);
        }
    }

    /// <summary>
    /// Draws a line using GDI (no AA).
    /// </summary>
    public void DrawLine(Point start, Point end, Color color, double thickness)
    {
        int penWidth = _stateManager.QuantizePenWidthPx(thickness);
        var pen = _resourceCache.GetOrCreatePen(color.ToCOLORREF(), penWidth);
        var oldPen = Gdi32.SelectObject(_hdc, pen);

        try
        {
            var p1 = _stateManager.ToDevicePoint(start);
            var p2 = _stateManager.ToDevicePoint(end);
            Gdi32.MoveToEx(_hdc, p1.x, p1.y, out _);
            Gdi32.LineTo(_hdc, p2.x, p2.y);
        }
        finally
        {
            Gdi32.SelectObject(_hdc, oldPen);
        }
    }

    /// <summary>
    /// Draws a rectangle outline using GDI (no AA).
    /// </summary>
    public void DrawRectangle(Rect rect, Color color, double thickness)
    {
        int penWidth = _stateManager.QuantizePenWidthPx(thickness);
        var pen = _resourceCache.GetOrCreatePen(color.ToCOLORREF(), penWidth);
        var nullBrush = Gdi32.GetStockObject(GdiConstants.NULL_BRUSH);
        var oldPen = Gdi32.SelectObject(_hdc, pen);
        var oldBrush = Gdi32.SelectObject(_hdc, nullBrush);

        try
        {
            var r = _stateManager.ToDeviceRect(rect);
            Gdi32.Rectangle(_hdc, r.left, r.top, r.right, r.bottom);
        }
        finally
        {
            Gdi32.SelectObject(_hdc, oldPen);
            Gdi32.SelectObject(_hdc, oldBrush);
        }
    }

    /// <summary>
    /// Fills a rectangle using GDI.
    /// </summary>
    public void FillRectangle(Rect rect, Color color)
    {
        var brush = _resourceCache.GetOrCreateBrush(color.ToCOLORREF());
        var r = _stateManager.ToDeviceRect(rect);
        Gdi32.FillRect(_hdc, ref r, brush);
    }

    /// <summary>
    /// Draws a rounded rectangle using GDI (no AA).
    /// </summary>
    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness)
    {
        int penWidth = _stateManager.QuantizePenWidthPx(thickness);
        var pen = _resourceCache.GetOrCreatePen(color.ToCOLORREF(), penWidth);
        var nullBrush = Gdi32.GetStockObject(GdiConstants.NULL_BRUSH);
        var oldPen = Gdi32.SelectObject(_hdc, pen);
        var oldBrush = Gdi32.SelectObject(_hdc, nullBrush);

        try
        {
            var r = _stateManager.ToDeviceRect(rect);
            int rx = _stateManager.QuantizeLengthPx(radiusX);
            int ry = _stateManager.QuantizeLengthPx(radiusY);
            Gdi32.RoundRect(_hdc, r.left, r.top, r.right, r.bottom, rx * 2, ry * 2);
        }
        finally
        {
            Gdi32.SelectObject(_hdc, oldPen);
            Gdi32.SelectObject(_hdc, oldBrush);
        }
    }

    /// <summary>
    /// Fills a rounded rectangle using GDI (no AA).
    /// </summary>
    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    {
        var brush = _resourceCache.GetOrCreateBrush(color.ToCOLORREF());
        var nullPen = Gdi32.GetStockObject(GdiConstants.NULL_PEN);
        var oldBrush = Gdi32.SelectObject(_hdc, brush);
        var oldPen = Gdi32.SelectObject(_hdc, nullPen);

        try
        {
            var r = _stateManager.ToDeviceRect(rect);
            int rx = _stateManager.QuantizeLengthPx(radiusX);
            int ry = _stateManager.QuantizeLengthPx(radiusY);
            // Add 1 to compensate for NULL_PEN
            Gdi32.RoundRect(_hdc, r.left, r.top, r.right + 1, r.bottom + 1, rx * 2, ry * 2);
        }
        finally
        {
            Gdi32.SelectObject(_hdc, oldPen);
            Gdi32.SelectObject(_hdc, oldBrush);
        }
    }

    /// <summary>
    /// Draws an ellipse using GDI (no AA).
    /// </summary>
    public void DrawEllipse(Rect bounds, Color color, double thickness)
    {
        int penWidth = _stateManager.QuantizePenWidthPx(thickness);
        var pen = _resourceCache.GetOrCreatePen(color.ToCOLORREF(), penWidth);
        var nullBrush = Gdi32.GetStockObject(GdiConstants.NULL_BRUSH);
        var oldPen = Gdi32.SelectObject(_hdc, pen);
        var oldBrush = Gdi32.SelectObject(_hdc, nullBrush);

        try
        {
            var r = _stateManager.ToDeviceRect(bounds);
            Gdi32.Ellipse(_hdc, r.left, r.top, r.right, r.bottom);
        }
        finally
        {
            Gdi32.SelectObject(_hdc, oldPen);
            Gdi32.SelectObject(_hdc, oldBrush);
        }
    }

    /// <summary>
    /// Fills an ellipse using GDI (no AA).
    /// </summary>
    public void FillEllipse(Rect bounds, Color color)
    {
        var brush = _resourceCache.GetOrCreateBrush(color.ToCOLORREF());
        var nullPen = Gdi32.GetStockObject(GdiConstants.NULL_PEN);
        var oldBrush = Gdi32.SelectObject(_hdc, brush);
        var oldPen = Gdi32.SelectObject(_hdc, nullPen);

        try
        {
            var r = _stateManager.ToDeviceRect(bounds);
            // Add 1 to compensate for NULL_PEN
            Gdi32.Ellipse(_hdc, r.left, r.top, r.right + 1, r.bottom + 1);
        }
        finally
        {
            Gdi32.SelectObject(_hdc, oldPen);
            Gdi32.SelectObject(_hdc, oldBrush);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _resourceCache.Dispose();
            _disposed = true;
        }
    }
}
