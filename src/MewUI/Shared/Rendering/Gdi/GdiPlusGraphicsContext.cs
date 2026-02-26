using System.Numerics;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering.Gdi.Core;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI+ graphics context (vector/clip quality), while keeping GDI text measurement/rendering.
/// </summary>
internal sealed class GdiPlusGraphicsContext : IGraphicsContext
{
    private readonly nint _hwnd;
    private readonly bool _ownsDc;
    private readonly ImageScaleQuality _imageScaleQuality;
    private readonly GdiBitmapRenderTarget? _bitmapTarget;

    private readonly int _pixelWidth;
    private readonly int _pixelHeight;

    private readonly GdiStateManager _textStateManager;
    private readonly GdiPrimitiveRenderer _primitiveRenderer;
    private readonly AaSurfacePool _surfacePool = new();

    private nint _graphics;
    private readonly Stack<GraphicsStateSnapshot> _states = new();
    private bool _disposed;
    private readonly double _dpiScale;
    private Matrix3x2 _transform = Matrix3x2.Identity;

    public double DpiScale => _dpiScale;

    public ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    internal nint Hdc { get; }

    public GdiPlusGraphicsContext(
        nint hwnd,
        nint hdc,
        double dpiScale,
        ImageScaleQuality imageScaleQuality,
        bool ownsDc = false)
        : this(hwnd, hdc, 0, 0, dpiScale, imageScaleQuality, ownsDc)
    {
    }

    internal GdiPlusGraphicsContext(
        nint hwnd,
        nint hdc,
        int pixelWidth,
        int pixelHeight,
        double dpiScale,
        ImageScaleQuality imageScaleQuality,
        bool ownsDc = false,
        GdiBitmapRenderTarget? bitmapTarget = null)
    {
        _hwnd = hwnd;
        Hdc = hdc;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _ownsDc = ownsDc;
        _imageScaleQuality = imageScaleQuality;
        _bitmapTarget = bitmapTarget;

        _dpiScale = dpiScale;
        _textStateManager = new GdiStateManager(hdc, dpiScale);
        _primitiveRenderer = new GdiPrimitiveRenderer(hdc, _textStateManager);

        Gdi32.SetBkMode(Hdc, GdiConstants.TRANSPARENT);
        GdiPlusInterop.EnsureInitialized();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _surfacePool.Dispose();

        if (_graphics != 0)
        {
            GdiPlusInterop.GdipDeleteGraphics(_graphics);
            _graphics = 0;
        }

        if (_ownsDc && Hdc != 0)
        {
            User32.ReleaseDC(_hwnd, Hdc);
        }
    }

    #region State Management

    public void Save()
    {
        _textStateManager.Save();

        uint state = 0;
        if (EnsureGraphics())
        {
            GdiPlusInterop.GdipSaveGraphics(_graphics, out state);
        }

        _states.Push(new GraphicsStateSnapshot
        {
            GdiPlusState = state,
            Transform = _transform,
        });
    }

    public void Restore()
    {
        _textStateManager.Restore();

        if (_states.Count == 0)
        {
            return;
        }

        var state = _states.Pop();
        _transform = state.Transform;

        if (_graphics != 0 && state.GdiPlusState != 0)
        {
            GdiPlusInterop.GdipRestoreGraphics(_graphics, state.GdiPlusState);
        }
    }

    public void SetClip(Rect rect)
    {
        // Keep HDC clip in sync for GDI text rendering.
        _textStateManager.SetClip(rect);

        if (!EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        GdiPlusInterop.GdipSetClipRectI(_graphics, r.left, r.top, r.Width, r.Height, GdiPlusInterop.CombineMode.Intersect);
    }

    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY)
    {
        // GDI text can only do rectangle clip; keep it close to bounds.
        _textStateManager.SetClip(rect);

        if (!EnsureGraphics())
        {
            return;
        }

        if (radiusX <= 0 && radiusY <= 0)
        {
            SetClip(rect);
            return;
        }

        var r = ToDeviceRect(rect);
        int ellipseW = Math.Max(1, QuantizeLengthPx(radiusX * 2));
        int ellipseH = Math.Max(1, QuantizeLengthPx(radiusY * 2));

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0)
        {
            return;
        }

        try
        {
            AddRoundedRectPathI(path, r.left, r.top, r.Width, r.Height, ellipseW, ellipseH);
            GdiPlusInterop.GdipClosePathFigure(path);
            GdiPlusInterop.GdipSetClipPath(_graphics, path, GdiPlusInterop.CombineMode.Intersect);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(path);
        }
    }

    public void Translate(double dx, double dy)
    {
        _transform = Matrix3x2.CreateTranslation((float)dx, (float)dy) * _transform;
        _textStateManager.Translate(dx, dy);
        SyncWorldTransform();
    }

    public void Rotate(double angleRadians)
    {
        _transform = Matrix3x2.CreateRotation((float)angleRadians) * _transform;
        SyncWorldTransform();
    }

    public void Scale(double sx, double sy)
    {
        _transform = Matrix3x2.CreateScale((float)sx, (float)sy) * _transform;
        SyncWorldTransform();
    }

    public void SetTransform(Matrix3x2 matrix)
    {
        _transform = matrix;
        SyncWorldTransform();
    }

    public Matrix3x2 GetTransform() => _transform;

    public void ResetTransform()
    {
        _transform = Matrix3x2.Identity;
        SyncWorldTransform();
    }

    private void SyncWorldTransform()
    {
        if (!EnsureGraphics()) return;
        // Drawing coordinates are already in device pixels (via ToDevice*), so the world transform's
        // scale/rotation components (M11-M22) are dimensionless and work as-is. But translation
        // components (M31, M32) are in logical units and must be converted to device pixels.
        var m = _transform;
        float dpi = (float)_dpiScale;
        if (GdiPlusInterop.GdipCreateMatrix2(
                m.M11, m.M12,
                m.M21, m.M22,
                m.M31 * dpi, m.M32 * dpi,
                out nint matrix) == 0 && matrix != 0)
        {
            GdiPlusInterop.GdipSetWorldTransform(_graphics, matrix);
            GdiPlusInterop.GdipDeleteMatrix(matrix);
        }
    }

    public void ResetClip()
    {
        if (EnsureGraphics())
        {
            GdiPlusInterop.GdipResetClip(_graphics);
        }
    }

    #endregion

    #region Drawing Primitives (GDI+)

    public void Clear(Color color)
    {
        if (_bitmapTarget != null)
        {
            _bitmapTarget.Clear(color);
        }
        else if (_hwnd != 0)
        {
            _primitiveRenderer.Clear(_hwnd, color);
        }
        else if (_pixelWidth > 0 && _pixelHeight > 0)
        {
            _primitiveRenderer.Clear(_pixelWidth, _pixelHeight, color);
        }
    }

    public void DrawLine(Point start, Point end, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        float widthPx = (int)Math.Round(ToDevicePx(thickness));
        if (widthPx <= 0)
        {
            return;
        }

        var (ax, ay) = ToDeviceCoords(start.X, start.Y);
        var (bx, by) = ToDeviceCoords(end.X, end.Y);

        if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipDrawLineI(_graphics, pen, (int)Math.Round(ax), (int)Math.Round(ay), (int)Math.Round(bx), (int)Math.Round(by));
        }
        finally
        {
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    public void DrawRectangle(Rect rect, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        float widthPx = (float)ToDevicePx(thickness);
        if (widthPx <= 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipDrawRectangleI(_graphics, pen, r.left, r.top, r.Width, r.Height);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    public void FillRectangle(Rect rect, Color color)
    {
        if (color.A == 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreateSolidFill(ToArgb(color), out var brush) != 0 || brush == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipFillRectangleI(_graphics, brush, r.left, r.top, r.Width, r.Height);
        }
        finally
        {
            GdiPlusInterop.GdipDeleteBrush(brush);
        }
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        float widthPx = (float)ToDevicePx(thickness);
        int ew = Math.Max(1, QuantizeLengthPx(radiusX * 2));
        int eh = Math.Max(1, QuantizeLengthPx(radiusY * 2));

        if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0)
        {
            GdiPlusInterop.GdipDeletePen(pen);
            return;
        }

        try
        {
            AddRoundedRectPathI(path, r.left, r.top, r.Width, r.Height, ew, eh);
            GdiPlusInterop.GdipClosePathFigure(path);
            GdiPlusInterop.GdipDrawPath(_graphics, pen, path);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(path);
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    {
        if (color.A == 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        int ew = Math.Max(1, QuantizeLengthPx(radiusX * 2));
        int eh = Math.Max(1, QuantizeLengthPx(radiusY * 2));

        if (GdiPlusInterop.GdipCreateSolidFill(ToArgb(color), out var brush) != 0 || brush == 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0)
        {
            GdiPlusInterop.GdipDeleteBrush(brush);
            return;
        }

        try
        {
            AddRoundedRectPathI(path, r.left, r.top, r.Width, r.Height, ew, eh);
            GdiPlusInterop.GdipClosePathFigure(path);
            GdiPlusInterop.GdipFillPath(_graphics, brush, path);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(path);
            GdiPlusInterop.GdipDeleteBrush(brush);
        }
    }

    public void DrawEllipse(Rect bounds, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(bounds);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        float widthPx = (float)ToDevicePx(thickness);
        if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipDrawEllipseI(_graphics, pen, r.left, r.top, r.Width, r.Height);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    public void FillEllipse(Rect bounds, Color color)
    {
        if (color.A == 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(bounds);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreateSolidFill(ToArgb(color), out var brush) != 0 || brush == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipFillEllipseI(_graphics, brush, r.left, r.top, r.Width, r.Height);
        }
        finally
        {
            GdiPlusInterop.GdipDeleteBrush(brush);
        }
    }

    public void DrawPath(PathGeometry path, Color color, double thickness = 1)
    {
        if (path == null || color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        float widthPx = (float)ToDevicePx(thickness);
        if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var gdipPath) != 0 || gdipPath == 0)
        {
            GdiPlusInterop.GdipDeletePen(pen);
            return;
        }

        try
        {
            BuildGdipPath(gdipPath, path);
            GdiPlusInterop.GdipDrawPath(_graphics, pen, gdipPath);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(gdipPath);
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    public void FillPath(PathGeometry path, Color color)
        => FillPath(path, color, path?.FillRule ?? FillRule.NonZero);

    public void FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        if (path == null || color.A == 0 || !EnsureGraphics())
        {
            return;
        }

        if (GdiPlusInterop.GdipCreateSolidFill(ToArgb(color), out var brush) != 0 || brush == 0)
        {
            return;
        }

        var fillMode = fillRule == FillRule.EvenOdd ? GdiPlusInterop.FillMode.Alternate : GdiPlusInterop.FillMode.Winding;
        if (GdiPlusInterop.GdipCreatePath(fillMode, out var gdipPath) != 0 || gdipPath == 0)
        {
            GdiPlusInterop.GdipDeleteBrush(brush);
            return;
        }

        try
        {
            BuildGdipPath(gdipPath, path);
            GdiPlusInterop.GdipFillPath(_graphics, brush, gdipPath);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(gdipPath);
            GdiPlusInterop.GdipDeleteBrush(brush);
        }
    }

    public void FillRectangle(Rect rect, IBrush brush)
    {
        if (brush is ISolidColorBrush solid) { FillRectangle(rect, solid.Color); return; }
        if (brush is IGradientBrush gradient && EnsureGraphics())
        {
            var (x, y) = ToDeviceCoords(rect.X, rect.Y);
            float w = (float)(rect.Width * _dpiScale);
            float h = (float)(rect.Height * _dpiScale);
            if (w <= 0 || h <= 0) return;
            // Use float path for proper anti-aliasing with gradient brush.
            if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0) return;
            try
            {
                GdiPlusInterop.GdipAddPathRectangle(path, (float)x, (float)y, w, h);
                FillWithGradient(gradient, rect, b => GdiPlusInterop.GdipFillPath(_graphics, b, path), rect);
            }
            finally { GdiPlusInterop.GdipDeletePath(path); }
        }
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush)
    {
        if (brush is ISolidColorBrush solid) { FillRoundedRectangle(rect, radiusX, radiusY, solid.Color); return; }
        if (brush is IGradientBrush gradient && EnsureGraphics())
        {
            var (x, y) = ToDeviceCoords(rect.X, rect.Y);
            float w = (float)(rect.Width * _dpiScale);
            float h = (float)(rect.Height * _dpiScale);
            if (w <= 0 || h <= 0) return;
            float ew = Math.Max(1f, (float)(radiusX * 2 * _dpiScale));
            float eh = Math.Max(1f, (float)(radiusY * 2 * _dpiScale));
            if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0) return;
            try
            {
                AddRoundedRectPathF(path, (float)x, (float)y, w, h, ew, eh);
                GdiPlusInterop.GdipClosePathFigure(path);
                FillWithGradient(gradient, rect, b => GdiPlusInterop.GdipFillPath(_graphics, b, path), rect);
            }
            finally { GdiPlusInterop.GdipDeletePath(path); }
        }
    }

    public void FillEllipse(Rect bounds, IBrush brush)
    {
        if (brush is ISolidColorBrush solid) { FillEllipse(bounds, solid.Color); return; }
        if (brush is IGradientBrush gradient && EnsureGraphics())
        {
            var (x, y) = ToDeviceCoords(bounds.X, bounds.Y);
            float w = (float)(bounds.Width * _dpiScale);
            float h = (float)(bounds.Height * _dpiScale);
            if (w <= 0 || h <= 0) return;
            // Use float path for proper anti-aliasing with gradient brush.
            if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0) return;
            try
            {
                GdiPlusInterop.GdipAddPathEllipse(path, (float)x, (float)y, w, h);
                FillWithGradient(gradient, bounds, b => GdiPlusInterop.GdipFillPath(_graphics, b, path), bounds);
            }
            finally { GdiPlusInterop.GdipDeletePath(path); }
        }
    }

    public void FillPath(PathGeometry path, IBrush brush)
        => FillPath(path, brush, path?.FillRule ?? FillRule.NonZero);

    public void FillPath(PathGeometry path, IBrush brush, FillRule fillRule)
    {
        if (path == null) return;
        if (brush is ISolidColorBrush solid) { FillPath(path, solid.Color, fillRule); return; }
        if (brush is IGradientBrush gradient && EnsureGraphics())
        {
            var fillMode = fillRule == FillRule.EvenOdd ? GdiPlusInterop.FillMode.Alternate : GdiPlusInterop.FillMode.Winding;
            if (GdiPlusInterop.GdipCreatePath(fillMode, out var gdipPath) != 0 || gdipPath == 0) return;
            try
            {
                BuildGdipPath(gdipPath, path);
                // OBB requires path bounds; fall back to representative color for path+OBB.
                if (gradient.GradientUnits == GradientUnits.ObjectBoundingBox)
                { FillPath(path, gradient.GetRepresentativeColor()); return; }
                var pathBounds = ComputeApproximateBounds(path);
                FillWithGradient(gradient, default, b => GdiPlusInterop.GdipFillPath(_graphics, b, gdipPath), pathBounds);
            }
            finally { GdiPlusInterop.GdipDeletePath(gdipPath); }
        }
    }

    public void DrawLine(Point start, Point end, IPen pen)
    {
        if (pen.Thickness <= 0 || !EnsureGraphics()) return;

        float widthPx = (float)ToDevicePx(pen.Thickness);
        if (widthPx <= 0) return;

        var (ax, ay) = ToDeviceCoords(start.X, start.Y);
        var (bx, by) = ToDeviceCoords(end.X, end.Y);

        if (!TryCreateStyledPen(pen, widthPx, out var gdipPen)) return;
        try
        {
            GdiPlusInterop.GdipDrawLineI(_graphics, gdipPen, (int)Math.Round(ax), (int)Math.Round(ay), (int)Math.Round(bx), (int)Math.Round(by));
        }
        finally { GdiPlusInterop.GdipDeletePen(gdipPen); }
    }

    public void DrawRectangle(Rect rect, IPen pen)
    {
        if (pen.Thickness <= 0 || !EnsureGraphics()) return;

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0) return;

        float widthPx = (float)ToDevicePx(pen.Thickness);
        if (!TryCreateStyledPen(pen, widthPx, out var gdipPen)) return;
        try
        {
            GdiPlusInterop.GdipDrawRectangleI(_graphics, gdipPen, r.left, r.top, r.Width, r.Height);
        }
        finally { GdiPlusInterop.GdipDeletePen(gdipPen); }
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen)
    {
        if (pen.Thickness <= 0 || !EnsureGraphics()) return;

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0) return;

        float widthPx = (float)ToDevicePx(pen.Thickness);
        int ew = Math.Max(1, QuantizeLengthPx(radiusX * 2));
        int eh = Math.Max(1, QuantizeLengthPx(radiusY * 2));

        if (!TryCreateStyledPen(pen, widthPx, out var gdipPen)) return;
        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0)
        {
            GdiPlusInterop.GdipDeletePen(gdipPen);
            return;
        }

        try
        {
            AddRoundedRectPathI(path, r.left, r.top, r.Width, r.Height, ew, eh);
            GdiPlusInterop.GdipClosePathFigure(path);
            GdiPlusInterop.GdipDrawPath(_graphics, gdipPen, path);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(path);
            GdiPlusInterop.GdipDeletePen(gdipPen);
        }
    }

    public void DrawEllipse(Rect bounds, IPen pen)
    {
        if (pen.Thickness <= 0 || !EnsureGraphics()) return;

        var r = ToDeviceRect(bounds);
        if (r.Width <= 0 || r.Height <= 0) return;

        float widthPx = (float)ToDevicePx(pen.Thickness);
        if (!TryCreateStyledPen(pen, widthPx, out var gdipPen)) return;
        try
        {
            GdiPlusInterop.GdipDrawEllipseI(_graphics, gdipPen, r.left, r.top, r.Width, r.Height);
        }
        finally { GdiPlusInterop.GdipDeletePen(gdipPen); }
    }

    public void DrawPath(PathGeometry path, IPen pen)
    {
        if (path == null || pen.Thickness <= 0 || !EnsureGraphics()) return;

        float widthPx = (float)ToDevicePx(pen.Thickness);
        if (!TryCreateStyledPen(pen, widthPx, out var gdipPen)) return;

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var gdipPath) != 0 || gdipPath == 0)
        {
            GdiPlusInterop.GdipDeletePen(gdipPen);
            return;
        }

        try
        {
            BuildGdipPath(gdipPath, path);
            GdiPlusInterop.GdipDrawPath(_graphics, gdipPen, gdipPath);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(gdipPath);
            GdiPlusInterop.GdipDeletePen(gdipPen);
        }
    }

    private bool TryCreateStyledPen(IPen pen, float widthPx, out nint gdipPen)
    {
        gdipPen = 0;

        if (pen.Brush is IGradientBrush gradient)
        {
            // Create a temporary GDI+ gradient brush, then create pen from it.
            // GDI+ pen copies the brush state, so we can release the brush immediately.
            nint tempBrush = CreateGdipGradientBrush(gradient, default);
            if (tempBrush == 0)
                return false;
            int hr = GdiPlusInterop.GdipCreatePen2(tempBrush, widthPx, GdiPlusInterop.Unit.Pixel, out gdipPen);
            GdiPlusInterop.GdipDeleteBrush(tempBrush);
            if (hr != 0 || gdipPen == 0)
                return false;
        }
        else
        {
            uint argb = pen.Brush is ISolidColorBrush solid ? ToArgb(solid.Color) : 0xFF000000;
            if (GdiPlusInterop.GdipCreatePen1(argb, widthPx, GdiPlusInterop.Unit.Pixel, out gdipPen) != 0 || gdipPen == 0)
                return false;
        }

        var style = pen.StrokeStyle;

        var cap = style.LineCap switch
        {
            StrokeLineCap.Round => GdiPlusInterop.GpLineCap.Round,
            StrokeLineCap.Square => GdiPlusInterop.GpLineCap.Square,
            _ => GdiPlusInterop.GpLineCap.Flat,
        };
        var dashCap = style.LineCap == StrokeLineCap.Round
            ? GdiPlusInterop.GpDashCap.Round
            : GdiPlusInterop.GpDashCap.Flat;
        GdiPlusInterop.GdipSetPenLineCap197819(gdipPen, cap, cap, dashCap);

        GdiPlusInterop.GdipSetPenLineJoin(gdipPen, style.LineJoin switch
        {
            StrokeLineJoin.Round => GdiPlusInterop.GpLineJoin.Round,
            StrokeLineJoin.Bevel => GdiPlusInterop.GpLineJoin.Bevel,
            _ => GdiPlusInterop.GpLineJoin.Miter,
        });

        if (style.MiterLimit > 0)
            GdiPlusInterop.GdipSetPenMiterLimit(gdipPen, (float)style.MiterLimit);

        if (style.IsDashed && style.DashArray is { Count: > 0 } dashes)
        {
            GdiPlusInterop.GdipSetPenDashStyle(gdipPen, GdiPlusInterop.GpDashStyle.Custom);
            var arr = new float[dashes.Count];
            for (int i = 0; i < dashes.Count; i++)
                arr[i] = (float)dashes[i];
            GdiPlusInterop.SetPenDashArray(gdipPen, arr);
            if (style.DashOffset != 0)
                GdiPlusInterop.GdipSetPenDashOffset(gdipPen, (float)style.DashOffset);
        }

        return true;
    }

    private void BuildGdipPath(nint gdipPath, PathGeometry path)
    {
        float currentX = 0, currentY = 0;
        bool hasCurrent = false;

        foreach (var cmd in path.Commands)
        {
            switch (cmd.Type)
            {
                case PathCommandType.MoveTo:
                {
                    var (px, py) = ToDeviceCoords(cmd.X0, cmd.Y0);
                    GdiPlusInterop.GdipStartPathFigure(gdipPath);
                    currentX = (float)px;
                    currentY = (float)py;
                    hasCurrent = true;
                    break;
                }
                case PathCommandType.LineTo:
                {
                    if (!hasCurrent) break;
                    var (px, py) = ToDeviceCoords(cmd.X0, cmd.Y0);
                    GdiPlusInterop.GdipAddPathLine(gdipPath, currentX, currentY, (float)px, (float)py);
                    currentX = (float)px;
                    currentY = (float)py;
                    break;
                }
                case PathCommandType.BezierTo:
                {
                    if (!hasCurrent) break;
                    var (c1x, c1y) = ToDeviceCoords(cmd.X0, cmd.Y0);
                    var (c2x, c2y) = ToDeviceCoords(cmd.X1, cmd.Y1);
                    var (epx, epy) = ToDeviceCoords(cmd.X2, cmd.Y2);
                    GdiPlusInterop.GdipAddPathBezier(gdipPath,
                        currentX, currentY,
                        (float)c1x, (float)c1y,
                        (float)c2x, (float)c2y,
                        (float)epx, (float)epy);
                    currentX = (float)epx;
                    currentY = (float)epy;
                    break;
                }
                case PathCommandType.Close:
                    GdiPlusInterop.GdipClosePathFigure(gdipPath);
                    hasCurrent = false;
                    break;
            }
        }
    }

    #endregion

    #region Text Rendering (GDI)

    public unsafe void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color)
    {
        if (font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(font));
        }

        if (text.IsEmpty || color.A == 0)
        {
            return;
        }

        // Early cull: skip if the text row is entirely outside the current clip region.
        // Use font size as an estimated height; a bit generous to avoid false culling.
        var cullPt = ToDevicePoint(location);
        int cullH = Math.Max(4, (int)Math.Ceiling(font.Size * _dpiScale) * 2);
        var cullRect = RECT.FromLTRB(cullPt.x, cullPt.y, 32767, cullPt.y + cullH);
        if (Gdi32.RectVisible(Hdc, ref cullRect) == 0)
        {
            return;
        }

        // For layered/bitmap targets, legacy GDI text APIs do not produce usable per-pixel alpha.
        // Use coverage-based rendering and synthesize premultiplied alpha for smooth edges.
        if (_bitmapTarget != null)
        {
            var sizeDip = MeasureText(text, gdiFont);
            int w = Math.Max(1, QuantizeLengthPx(sizeDip.Width));
            int h = Math.Max(1, QuantizeLengthPx(sizeDip.Height));
            var pt = ToDevicePoint(location);
            var r = RECT.FromLTRB(pt.x, pt.y, pt.x + w, pt.y + h);
            uint format = GdiConstants.DT_NOPREFIX | GdiConstants.DT_LEFT | GdiConstants.DT_TOP | GdiConstants.DT_SINGLELINE;
            PerPixelAlphaTextRenderer.DrawText(Hdc, _bitmapTarget, _surfacePool, text, r, gdiFont, color, format);
            return;
        }

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.GetHandle(GdiFontRenderMode.Default));
        var oldColor = Gdi32.SetTextColor(Hdc, color.ToCOLORREF());

        try
        {
            var pt = ToDevicePoint(location);
            fixed (char* pText = text)
            {
                Gdi32.TextOut(Hdc, pt.x, pt.y, pText, text.Length);
            }
        }
        finally
        {
            Gdi32.SetTextColor(Hdc, oldColor);
            Gdi32.SelectObject(Hdc, oldFont);
        }
    }

    public unsafe void DrawText(
        ReadOnlySpan<char> text,
        Rect bounds,
        IFont font,
        Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap)
    {
        if (font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(font));
        }

        if (text.IsEmpty || color.A == 0)
        {
            return;
        }

        // Early cull: skip if the bounds rect is entirely outside the current clip region.
        var cullR = ToDeviceRect(bounds);
        if (Gdi32.RectVisible(Hdc, ref cullR) == 0)
        {
            return;
        }

        if (_bitmapTarget != null)
        {
            var r = GetTextLayoutRect(bounds, wrapping);
            uint format = BuildTextFormat(horizontalAlignment, verticalAlignment, wrapping);
            int yOffsetPx = 0;
            int textHeightPx = 0;
            if (wrapping != TextWrapping.NoWrap)
            {
                ComputeWrappedTextOffsetsPx(
                    text,
                    gdiFont.GetHandle(GdiFontRenderMode.Coverage),
                    r.Width,
                    r.Height,
                    verticalAlignment,
                    out yOffsetPx,
                    out textHeightPx);
            }

            PerPixelAlphaTextRenderer.DrawText(
                Hdc,
                _bitmapTarget,
                _surfacePool,
                text,
                r,
                gdiFont,
                color,
                format,
                yOffsetPx,
                textHeightPx);
            return;
        }

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.GetHandle(GdiFontRenderMode.Default));
        var oldColor = Gdi32.SetTextColor(Hdc, color.ToCOLORREF());

        try
        {
            var r = GetTextLayoutRect(bounds, wrapping);
            uint format = BuildTextFormat(horizontalAlignment, verticalAlignment, wrapping);
            int yOffsetPx = 0;
            int textHeightPx = 0;
            if (wrapping != TextWrapping.NoWrap)
            {
                ComputeWrappedTextOffsetsPx(
                    text,
                    gdiFont.GetHandle(GdiFontRenderMode.Default),
                    r.Width,
                    r.Height,
                    verticalAlignment,
                    out yOffsetPx,
                    out textHeightPx);
            }

            int clipState = ApplyTextClip(r, wrapping);

            fixed (char* pText = text)
            {
                ApplyVerticalOffset(ref r, yOffsetPx, textHeightPx);
                Gdi32.DrawText(Hdc, pText, text.Length, ref r, format);
            }

            RestoreTextClip(clipState);
        }
        finally
        {
            Gdi32.SetTextColor(Hdc, oldColor);
            Gdi32.SelectObject(Hdc, oldFont);
        }
    }

    private int ApplyTextClip(RECT boundsPx, TextWrapping wrapping)
    {
        if (wrapping == TextWrapping.NoWrap)
        {
            return 0;
        }

        int clipState = Gdi32.SaveDC(Hdc);
        if (clipState != 0)
        {
            Gdi32.IntersectClipRect(Hdc, boundsPx.left, boundsPx.top, boundsPx.right, boundsPx.bottom);
        }

        return clipState;
    }

    private void RestoreTextClip(int clipState)
    {
        if (clipState != 0)
        {
            Gdi32.RestoreDC(Hdc, clipState);
        }
    }

    private RECT GetTextLayoutRect(Rect bounds, TextWrapping wrapping)
    {
        if (wrapping == TextWrapping.NoWrap)
        {
        return ToDeviceRect(bounds);
        }

        var tl = ToDevicePoint(bounds.TopLeft);
        int w = QuantizeLengthPx(bounds.Width);
        int h = QuantizeLengthPx(bounds.Height);
        if (w <= 0)
        {
            w = 1;
        }
        if (h <= 0)
        {
            h = 1;
        }

        return RECT.FromLTRB(tl.x, tl.y, tl.x + w, tl.y + h);
    }

    private unsafe void ComputeWrappedTextOffsetsPx(
        ReadOnlySpan<char> text,
        nint fontHandle,
        int widthPx,
        int heightPx,
        TextAlignment verticalAlignment,
        out int yOffsetPx,
        out int textHeightPx)
    {
        if (verticalAlignment == TextAlignment.Top)
        {
            yOffsetPx = 0;
            textHeightPx = 0;
            return;
        }

        if (widthPx <= 0 || heightPx <= 0 || text.IsEmpty || fontHandle == 0 || Hdc == 0)
        {
            yOffsetPx = 0;
            textHeightPx = 0;
            return;
        }

        var oldFont = Gdi32.SelectObject(Hdc, fontHandle);
        try
        {
            var rect = new RECT(0, 0, widthPx, 0);
            fixed (char* pText = text)
            {
                Gdi32.DrawText(Hdc, pText, text.Length, ref rect,
                    GdiConstants.DT_CALCRECT | GdiConstants.DT_WORDBREAK | GdiConstants.DT_NOPREFIX);
            }

            textHeightPx = rect.Height;
            int remaining = heightPx - textHeightPx;
            if (remaining <= 0)
            {
                yOffsetPx = 0;
                return;
            }

            yOffsetPx = verticalAlignment == TextAlignment.Bottom
                ? remaining
                : remaining / 2;
        }
        finally
        {
            Gdi32.SelectObject(Hdc, oldFont);
        }
    }

    private static uint BuildTextFormat(TextAlignment horizontalAlignment, TextAlignment verticalAlignment, TextWrapping wrapping)
    {
        uint format = GdiConstants.DT_NOPREFIX;
        format |= horizontalAlignment switch
        {
            TextAlignment.Left => GdiConstants.DT_LEFT,
            TextAlignment.Center => GdiConstants.DT_CENTER,
            TextAlignment.Right => GdiConstants.DT_RIGHT,
            _ => GdiConstants.DT_LEFT
        };

        if (wrapping == TextWrapping.NoWrap)
        {
            format |= GdiConstants.DT_SINGLELINE;
            format |= verticalAlignment switch
            {
                TextAlignment.Top => GdiConstants.DT_TOP,
                TextAlignment.Center => GdiConstants.DT_VCENTER,
                TextAlignment.Bottom => GdiConstants.DT_BOTTOM,
                _ => GdiConstants.DT_TOP
            };
        }
        else
        {
            format |= GdiConstants.DT_WORDBREAK;
        }

        return format;
    }

    private static void ApplyVerticalOffset(ref RECT rect, int yOffsetPx, int textHeightPx)
    {
        if (yOffsetPx != 0)
        {
            rect.top += yOffsetPx;
            rect.bottom += yOffsetPx;
        }
        if (textHeightPx > 0)
        {
            rect.bottom = rect.top + textHeightPx;
        }
    }

    public unsafe Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        if (font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(font));
        }

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.Handle);

        try
        {
            if (text.IsEmpty)
            {
                return Size.Empty;
            }

            var hasLineBreaks = text.IndexOfAny('\r', '\n') >= 0;
            var rect = hasLineBreaks
                ? new RECT(0, 0, QuantizeLengthPx(1_000_000), 0)
                : new RECT(0, 0, 0, 0);

            uint format = hasLineBreaks
                ? GdiConstants.DT_CALCRECT | GdiConstants.DT_WORDBREAK | GdiConstants.DT_NOPREFIX
                : GdiConstants.DT_CALCRECT | GdiConstants.DT_SINGLELINE | GdiConstants.DT_NOPREFIX;

            fixed (char* pText = text)
            {
                Gdi32.DrawText(Hdc, pText, text.Length, ref rect, format);
            }

            return new Size(TextMeasurePolicy.ApplyWidthPadding(rect.Width) / DpiScale, rect.Height / DpiScale);
        }
        finally
        {
            Gdi32.SelectObject(Hdc, oldFont);
        }
    }

    public unsafe Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(font));
        }

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.Handle);

        try
        {
            if (double.IsNaN(maxWidth) || maxWidth <= 0 || double.IsInfinity(maxWidth))
            {
                maxWidth = 1_000_000;
            }

            var rect = new RECT(0, 0, QuantizeLengthPx(maxWidth), 0);

            fixed (char* pText = text)
            {
                Gdi32.DrawText(Hdc, pText, text.Length, ref rect,
                    GdiConstants.DT_CALCRECT | GdiConstants.DT_WORDBREAK | GdiConstants.DT_NOPREFIX);
            }

            return new Size(TextMeasurePolicy.ApplyWidthPadding(rect.Width) / DpiScale, rect.Height / DpiScale);
        }
        finally
        {
            Gdi32.SelectObject(Hdc, oldFont);
        }
    }

    #endregion

    #region Image Rendering (GDI)

    public void DrawImage(IImage image, Point location)
    {
        if (image is not GdiImage gdiImage)
        {
            throw new ArgumentException("Image must be a GdiImage", nameof(image));
        }

        DrawImage(gdiImage, new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight));
    }

    public void DrawImage(IImage image, Rect destRect)
    {
        if (image is not GdiImage gdiImage)
        {
            throw new ArgumentException("Image must be a GdiImage", nameof(image));
        }

        DrawImage(gdiImage, destRect, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
    }

    public void DrawImage(IImage image, Rect destRect, Rect sourceRect)
    {
        if (image is not GdiImage gdiImage)
        {
            throw new ArgumentException("Image must be a GdiImage", nameof(image));
        }

        DrawImageCore(gdiImage, destRect, sourceRect);
    }

    private void DrawImageCore(GdiImage gdiImage, Rect destRect, Rect sourceRect)
    {
        gdiImage.EnsureUpToDate();

        var destPx = ToDeviceRect(destRect);
        if (destPx.Width <= 0 || destPx.Height <= 0)
        {
            return;
        }

        // Resolve backend default:
        // - Default => factory default (which is Linear by default to match other backends)
        // - NearestNeighbor => GDI stretch with COLORONCOLOR (fast, pixelated)
        // - Linear => cached bilinear resample
        // - HighQuality => cached prefiltered downscale + bilinear
        var effective = ImageScaleQuality == ImageScaleQuality.Default
            ? (_imageScaleQuality == ImageScaleQuality.Default ? ImageScaleQuality.Normal : _imageScaleQuality)
            : ImageScaleQuality;

        var memDc = Gdi32.CreateCompatibleDC(Hdc);
        var oldBitmap = Gdi32.SelectObject(memDc, gdiImage.Handle);

        try
        {
            int srcX = (int)sourceRect.X;
            int srcY = (int)sourceRect.Y;
            int srcW = (int)sourceRect.Width;
            int srcH = (int)sourceRect.Height;

            if (effective == ImageScaleQuality.Fast)
            {
                // Nearest: rely on GDI stretch + alpha blend (COLORONCOLOR).
                int oldStretchMode = Gdi32.SetStretchBltMode(Hdc, GdiConstants.COLORONCOLOR);
                try
                {
                    var blend = BLENDFUNCTION.SourceOver(255);
                    Gdi32.AlphaBlend(
                        Hdc, destPx.left, destPx.top, destPx.Width, destPx.Height,
                        memDc, srcX, srcY, srcW, srcH,
                        blend);
                }
                finally
                {
                    if (oldStretchMode != 0)
                    {
                        Gdi32.SetStretchBltMode(Hdc, oldStretchMode);
                    }
                }

                return;
            }

            // Linear/HighQuality: try cached scaled bitmap for deterministic, backend-independent resampling.
            // This trades memory for speed when the same image is drawn repeatedly at the same scaled size
            // (common in UI).
            //
            // For HighQuality, allow rounding the source rect to whole pixels so ViewBox/UniformToFill
            // cases can still take the resample-cache path (otherwise we'd fall back to GDI stretch).
            bool srcAligned =
                IsNearInt(sourceRect.X) && IsNearInt(sourceRect.Y) &&
                IsNearInt(sourceRect.Width) && IsNearInt(sourceRect.Height);

            int scaledSrcX = srcX;
            int scaledSrcY = srcY;
            int scaledSrcW = srcW;
            int scaledSrcH = srcH;

            if (!srcAligned && effective == ImageScaleQuality.HighQuality)
            {
                int left = (int)Math.Round(sourceRect.X);
                int top = (int)Math.Round(sourceRect.Y);
                int right = (int)Math.Round(sourceRect.Right);
                int bottom = (int)Math.Round(sourceRect.Bottom);

                if (right > left && bottom > top)
                {
                    scaledSrcX = left;
                    scaledSrcY = top;
                    scaledSrcW = right - left;
                    scaledSrcH = bottom - top;
                    srcAligned = true;
                }
            }

            if (srcAligned &&
                gdiImage.TryGetOrCreateScaledBitmap(scaledSrcX, scaledSrcY, scaledSrcW, scaledSrcH, destPx.Width, destPx.Height, effective, out var scaledBmp))
            {
                var scaledDc = Gdi32.CreateCompatibleDC(Hdc);
                var oldScaled = Gdi32.SelectObject(scaledDc, scaledBmp);
                try
                {
                    var blendScaled = BLENDFUNCTION.SourceOver(255);
                    Gdi32.AlphaBlend(
                        Hdc, destPx.left, destPx.top, destPx.Width, destPx.Height,
                        scaledDc, 0, 0, destPx.Width, destPx.Height,
                        blendScaled);
                }
                finally
                {
                    Gdi32.SelectObject(scaledDc, oldScaled);
                    Gdi32.DeleteDC(scaledDc);
                }

                return;
            }

            // Fallback: if we can't use the cache (e.g. fractional sourceRect), use GDI stretch + alpha blend.
            // Prefer linear as the "Default" behavior to match other backends.
            // NOTE: GDI has no true "linear" filter; HALFTONE is the best available built-in option.
            int stretch = GdiConstants.HALFTONE;
            int oldMode = Gdi32.SetStretchBltMode(Hdc, stretch);
            var oldBrushOrg = default(POINT);
            bool hasBrushOrg = stretch == GdiConstants.HALFTONE;
            if (hasBrushOrg)
            {
                Gdi32.SetBrushOrgEx(Hdc, 0, 0, out oldBrushOrg);
            }

            try
            {
                var blend = BLENDFUNCTION.SourceOver(255);
                Gdi32.AlphaBlend(
                    Hdc, destPx.left, destPx.top, destPx.Width, destPx.Height,
                    memDc, srcX, srcY, srcW, srcH,
                    blend);
            }
            finally
            {
                if (oldMode != 0)
                {
                    Gdi32.SetStretchBltMode(Hdc, oldMode);
                }

                if (hasBrushOrg)
                {
                    Gdi32.SetBrushOrgEx(Hdc, oldBrushOrg.x, oldBrushOrg.y, out _);
                }
            }
        }
        finally
        {
            Gdi32.SelectObject(memDc, oldBitmap);
            Gdi32.DeleteDC(memDc);
        }
    }

    private static bool IsNearInt(double value) => Math.Abs(value - Math.Round(value)) <= 0.0001;

    #endregion

    private bool EnsureGraphics()
    {
        if (_graphics != 0)
        {
            return true;
        }

        if (GdiPlusInterop.GdipCreateFromHDC(Hdc, out _graphics) != 0 || _graphics == 0)
        {
            return false;
        }

        GdiPlusInterop.GdipSetSmoothingMode(_graphics, GdiPlusInterop.SmoothingMode.AntiAlias);
        GdiPlusInterop.GdipSetPixelOffsetMode(_graphics, GdiPlusInterop.PixelOffsetMode.Half);
        GdiPlusInterop.GdipSetCompositingMode(_graphics, GdiPlusInterop.CompositingMode.SourceOver);
        GdiPlusInterop.GdipSetCompositingQuality(_graphics, GdiPlusInterop.CompositingQuality.HighQuality);

        return true;
    }

    private static uint ToArgb(Color color)
        => (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);

    private static void AddRoundedRectPathF(nint path, float x, float y, float width, float height, float ellipseW, float ellipseH)
    {
        float w = Math.Max(0, width);
        float h = Math.Max(0, height);
        if (w == 0 || h == 0) return;

        float ew = Math.Min(ellipseW, w);
        float eh = Math.Min(ellipseH, h);
        float right = x + w;
        float bottom = y + h;

        GdiPlusInterop.GdipAddPathArc(path, x, y, ew, eh, 180, 90);
        GdiPlusInterop.GdipAddPathArc(path, right - ew, y, ew, eh, 270, 90);
        GdiPlusInterop.GdipAddPathArc(path, right - ew, bottom - eh, ew, eh, 0, 90);
        GdiPlusInterop.GdipAddPathArc(path, x, bottom - eh, ew, eh, 90, 90);
    }

    private static void AddRoundedRectPathI(nint path, int x, int y, int width, int height, int ellipseW, int ellipseH)
    {
        int w = Math.Max(0, width);
        int h = Math.Max(0, height);
        if (w == 0 || h == 0)
        {
            return;
        }

        int ew = Math.Min(ellipseW, w);
        int eh = Math.Min(ellipseH, h);

        int right = x + w;
        int bottom = y + h;

        GdiPlusInterop.GdipAddPathArcI(path, x, y, ew, eh, 180, 90);
        GdiPlusInterop.GdipAddPathArcI(path, right - ew, y, ew, eh, 270, 90);
        GdiPlusInterop.GdipAddPathArcI(path, right - ew, bottom - eh, ew, eh, 0, 90);
        GdiPlusInterop.GdipAddPathArcI(path, x, bottom - eh, ew, eh, 90, 90);
    }

    private POINT ToDevicePoint(Point pt)
    {
        // _transform is applied by GDI+ World Transform; only DPI scale here.
        return new POINT(
            (int)Math.Round(pt.X * _dpiScale),
            (int)Math.Round(pt.Y * _dpiScale));
    }

    private RECT ToDeviceRect(Rect rect)
    {
        // _transform is applied by GDI+ World Transform; only DPI scale here.
        var (left, top, right, bottom) = RenderingUtil.ToDeviceRect(rect, 0, 0, _dpiScale);
        return new RECT(left, top, right, bottom);
    }

    private int QuantizeLengthPx(double lengthDip)
    {
        if (lengthDip <= 0 || double.IsNaN(lengthDip) || double.IsInfinity(lengthDip))
        {
            return 0;
        }

        return LayoutRounding.RoundToPixelInt(lengthDip, _dpiScale);
    }

    private (double x, double y) ToDeviceCoords(double x, double y)
    {
        // _transform is applied by GDI+ World Transform; only DPI scale here.
        return (x * _dpiScale, y * _dpiScale);
    }

    private double ToDevicePx(double logicalValue) => logicalValue * _dpiScale;

    /// <summary>
    /// Creates a GDI+ gradient brush for use with pen creation.
    /// Caller must release the returned handle. Returns 0 on failure.
    /// </summary>
    private nint CreateGdipGradientBrush(IGradientBrush brush, Rect objectBounds)
    {
        var stops = brush.Stops;
        if (stops == null || stops.Count == 0) return 0;

        var (colors, positions) = EnsureEndpointStops(stops);
        var wrapMode = brush.SpreadMethod switch
        {
            SpreadMethod.Reflect => GdiPlusInterop.WrapMode.TileFlipXY,
            SpreadMethod.Repeat  => GdiPlusInterop.WrapMode.Tile,
            _                    => GdiPlusInterop.WrapMode.TileFlipXY,
        };

        if (brush is ILinearGradientBrush linear)
        {
            var p1 = ResolveGradientPoint(linear.StartPoint, brush.GradientUnits, objectBounds);
            var p2 = ResolveGradientPoint(linear.EndPoint,   brush.GradientUnits, objectBounds);
            var (ax, ay) = ToDeviceCoords(p1.X, p1.Y);
            var (bx, by) = ToDeviceCoords(p2.X, p2.Y);
            var gp1 = new GdiPlusInterop.PointF((float)ax, (float)ay);
            var gp2 = new GdiPlusInterop.PointF((float)bx, (float)by);

            if (GdiPlusInterop.GdipCreateLineBrush(ref gp1, ref gp2,
                    colors[0], colors[^1], wrapMode, out nint gradBrush) != 0 || gradBrush == 0) return 0;
            GdiPlusInterop.SetLinePresetBlend(gradBrush, colors, positions);
            return gradBrush;
        }

        if (brush is IRadialGradientBrush radial)
        {
            var center = ResolveGradientPoint(radial.Center, brush.GradientUnits, objectBounds);
            var (cx, cy) = ToDeviceCoords(center.X, center.Y);
            double rx = radial.RadiusX * (brush.GradientUnits == GradientUnits.ObjectBoundingBox ? objectBounds.Width : 1.0) * _dpiScale;
            double ry = radial.RadiusY * (brush.GradientUnits == GradientUnits.ObjectBoundingBox ? objectBounds.Height : 1.0) * _dpiScale;
            if (rx <= 0 || ry <= 0) return 0;

            const float pad = 3f;
            if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out nint ellipsePath) != 0 || ellipsePath == 0) return 0;
            GdiPlusInterop.GdipAddPathEllipse(ellipsePath,
                (float)(cx - rx - pad), (float)(cy - ry - pad), (float)(rx * 2 + pad * 2), (float)(ry * 2 + pad * 2));

            if (GdiPlusInterop.GdipCreatePathGradientFromPath(ellipsePath, out nint gradBrush) != 0 || gradBrush == 0)
            {
                GdiPlusInterop.GdipDeletePath(ellipsePath);
                return 0;
            }
            GdiPlusInterop.GdipDeletePath(ellipsePath);

            var origin = ResolveGradientPoint(radial.GradientOrigin, brush.GradientUnits, objectBounds);
            var (ox, oy) = ToDeviceCoords(origin.X, origin.Y);
            var centerPt = new GdiPlusInterop.PointF((float)ox, (float)oy);
            GdiPlusInterop.GdipSetPathGradientCenterPoint(gradBrush, ref centerPt);
            // GDI+ PathGradient preset blend: position 0 = boundary, 1 = center (opposite of D2D/SVG).
            ReverseStops(colors, positions);
            GdiPlusInterop.SetPathGradientPresetBlend(gradBrush, colors, positions);
            GdiPlusInterop.GdipSetPathGradientWrapMode(gradBrush, wrapMode);
            return gradBrush;
        }

        return 0;
    }

    private static void ReverseStops(uint[] colors, float[] positions)
    {
        Array.Reverse(colors);
        Array.Reverse(positions);
        for (int i = 0; i < positions.Length; i++)
            positions[i] = 1f - positions[i];
    }

    private void FillWithGradient(IGradientBrush brush, Rect objectBounds, Action<nint> fillAction, Rect fillBounds = default)
    {
        var stops = brush.Stops;
        if (stops == null || stops.Count == 0) return;

        var (colors, positions) = EnsureEndpointStops(stops);

        // GDI+ LinearGradientBrush does NOT support WrapMode.Clamp;
        // use TileFlipXY as the closest approximation for SpreadMethod.Pad.
        var wrapMode = brush.SpreadMethod switch
        {
            SpreadMethod.Reflect => GdiPlusInterop.WrapMode.TileFlipXY,
            SpreadMethod.Repeat  => GdiPlusInterop.WrapMode.Tile,
            _                    => GdiPlusInterop.WrapMode.TileFlipXY,
        };

        if (brush is ILinearGradientBrush linear)
        {
            var p1 = ResolveGradientPoint(linear.StartPoint, brush.GradientUnits, objectBounds);
            var p2 = ResolveGradientPoint(linear.EndPoint,   brush.GradientUnits, objectBounds);
            var (ax, ay) = ToDeviceCoords(p1.X, p1.Y);
            var (bx, by) = ToDeviceCoords(p2.X, p2.Y);
            var gp1 = new GdiPlusInterop.PointF((float)ax, (float)ay);
            var gp2 = new GdiPlusInterop.PointF((float)bx, (float)by);

            if (GdiPlusInterop.GdipCreateLineBrush(ref gp1, ref gp2,
                    colors[0], colors[^1], wrapMode, out nint gradBrush) != 0 || gradBrush == 0) return;
            try
            {
                GdiPlusInterop.SetLinePresetBlend(gradBrush, colors, positions);
                fillAction(gradBrush);
            }
            finally { GdiPlusInterop.GdipDeleteBrush(gradBrush); }
        }
        else if (brush is IRadialGradientBrush radial)
        {
            var center = ResolveGradientPoint(radial.Center, brush.GradientUnits, objectBounds);
            var (cx, cy) = ToDeviceCoords(center.X, center.Y);
            double rx = radial.RadiusX * (brush.GradientUnits == GradientUnits.ObjectBoundingBox ? objectBounds.Width : 1.0) * _dpiScale;
            double ry = radial.RadiusY * (brush.GradientUnits == GradientUnits.ObjectBoundingBox ? objectBounds.Height : 1.0) * _dpiScale;
            if (rx <= 0 || ry <= 0) return;

            // PathGradientBrush is transparent outside its boundary ellipse.
            // If the fill shape extends beyond the gradient ellipse, edge pixels get
            // transparent brush values → no AA.  Compute the expansion factor needed
            // so the boundary fully encloses the fill shape, then remap stops to
            // preserve the gradient appearance within the original radius.
            double expansion = 1.0;
            if (fillBounds.Width > 0 && fillBounds.Height > 0)
            {
                var (fL, fT) = ToDeviceCoords(fillBounds.X, fillBounds.Y);
                var (fR, fB) = ToDeviceCoords(fillBounds.Right, fillBounds.Bottom);
                double maxT = 0;
                foreach (var (fx, fy) in new[] { (fL, fT), (fR, fT), (fR, fB), (fL, fB) })
                {
                    double dx = (fx - cx) / rx;
                    double dy = (fy - cy) / ry;
                    double t = dx * dx + dy * dy;
                    if (t > maxT) maxT = t;
                }
                if (maxT > 1.0)
                    expansion = Math.Sqrt(maxT) + 0.05; // small extra margin
            }

            const float pad = 3f;
            double effRx = rx * expansion + pad;
            double effRy = ry * expansion + pad;

            if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out nint ellipsePath) != 0 || ellipsePath == 0) return;
            try
            {
                GdiPlusInterop.GdipAddPathEllipse(ellipsePath,
                    (float)(cx - effRx), (float)(cy - effRy), (float)(effRx * 2), (float)(effRy * 2));

                if (GdiPlusInterop.GdipCreatePathGradientFromPath(ellipsePath, out nint gradBrush) != 0 || gradBrush == 0) return;
                try
                {
                    var origin = ResolveGradientPoint(radial.GradientOrigin, brush.GradientUnits, objectBounds);
                    var (ox, oy) = ToDeviceCoords(origin.X, origin.Y);
                    var centerPt = new GdiPlusInterop.PointF((float)ox, (float)oy);
                    GdiPlusInterop.GdipSetPathGradientCenterPoint(gradBrush, ref centerPt);

                    // GDI+ PathGradient preset blend: position 0 = boundary, 1 = center.
                    var revColors = (uint[])colors.Clone();
                    var revPositions = (float[])positions.Clone();
                    ReverseStops(revColors, revPositions);

                    // When expanded, remap stops so the gradient looks the same within
                    // the original radius; fill the expanded zone with the boundary color.
                    if (expansion > 1.0)
                    {
                        double totalF = expansion + pad / Math.Max(rx, 1.0);
                        var newColors = new uint[revColors.Length + 1];
                        var newPositions = new float[revPositions.Length + 1];
                        newColors[0] = revColors[0]; // boundary color fills expanded zone
                        newPositions[0] = 0f;
                        for (int i = 0; i < revColors.Length; i++)
                        {
                            newColors[i + 1] = revColors[i];
                            newPositions[i + 1] = (float)(1.0 - (1.0 - revPositions[i]) / totalF);
                        }
                        revColors = newColors;
                        revPositions = newPositions;
                    }

                    GdiPlusInterop.SetPathGradientPresetBlend(gradBrush, revColors, revPositions);
                    GdiPlusInterop.GdipSetPathGradientWrapMode(gradBrush, wrapMode);
                    fillAction(gradBrush);
                }
                finally { GdiPlusInterop.GdipDeleteBrush(gradBrush); }
            }
            finally { GdiPlusInterop.GdipDeletePath(ellipsePath); }
        }
    }

    private static Rect ComputeApproximateBounds(PathGeometry path)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var cmd in path.Commands)
        {
            if (cmd.Type == PathCommandType.Close) continue;
            Expand(cmd.X0, cmd.Y0);
            if (cmd.Type == PathCommandType.BezierTo)
            {
                Expand(cmd.X1, cmd.Y1);
                Expand(cmd.X2, cmd.Y2);
            }
        }

        if (minX > maxX) return default;
        return new Rect(minX, minY, maxX - minX, maxY - minY);

        void Expand(double x, double y)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
    }

    /// <summary>
    /// Ensures the stop list has an entry at offset 0.0 (first) and 1.0 (last),
    /// as required by GDI+ preset blend APIs.
    /// Returns parallel color (ARGB uint) and position (float) arrays.
    /// </summary>
    private static (uint[] colors, float[] positions) EnsureEndpointStops(IReadOnlyList<GradientStop> stops)
    {
        var list = new List<GradientStop>(stops);
        list.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        if (list[0].Offset > 0.0)
            list.Insert(0, new GradientStop(0.0, list[0].Color));
        if (list[^1].Offset < 1.0)
            list.Add(new GradientStop(1.0, list[^1].Color));

        var colors    = new uint[list.Count];
        var positions = new float[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            colors[i]    = (uint)(s.Color.A << 24 | s.Color.R << 16 | s.Color.G << 8 | s.Color.B);
            positions[i] = (float)Math.Clamp(s.Offset, 0f, 1f);
        }
        return (colors, positions);
    }

    private static Point ResolveGradientPoint(Point p, GradientUnits units, Rect objectBounds)
        => units == GradientUnits.ObjectBoundingBox
            ? new Point(objectBounds.X + p.X * objectBounds.Width, objectBounds.Y + p.Y * objectBounds.Height)
            : p;

    private readonly struct GraphicsStateSnapshot
    {
        public required uint GdiPlusState { get; init; }
        public required Matrix3x2 Transform { get; init; }
    }
}
