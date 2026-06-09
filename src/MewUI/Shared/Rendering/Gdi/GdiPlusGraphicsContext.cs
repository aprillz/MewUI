using System.Numerics;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering.Gdi.Core;

using static Aprillz.MewUI.Rendering.GradientBrushHelper;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI+ graphics context (vector/clip quality), while keeping GDI text measurement/rendering.
/// </summary>
internal sealed class GdiPlusGraphicsContext : GraphicsContextBase
{
    private readonly nint _hwnd;
    private readonly bool _ownsDc;
    private readonly ImageScaleQuality _imageScaleQuality;
    private readonly GdiPixelRenderSurface? _pixelSurface;

    private readonly int _pixelWidth;
    private readonly int _pixelHeight;

    private readonly GdiStateManager _stateManager;
    private readonly GdiPrimitiveRenderer _primitiveRenderer;
    private readonly AaSurfacePool _surfacePool = new();

    private nint _graphics;
    private nint _gpBitmap;
    private bool _pixelSurfaceDirtied;
    private readonly Stack<GraphicsStateSnapshot> _states = CollectionPool<Stack<GraphicsStateSnapshot>>.Rent();
    private readonly double _dpiScale;

    // Double-buffering (set only via CreateDoubleBuffered factory)
    private nint _screenDc;
    private BackBuffer? _backBuffer;
    private bool _transparentComposition;

    public override double DpiScale => _dpiScale;

    public override ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    public override float GlobalAlpha
    {
        get => _stateManager.GlobalAlpha;
        set => _stateManager.GlobalAlpha = value;
    }

    public override bool TextPixelSnap
    {
        get => _stateManager.TextPixelSnap;
        set => _stateManager.TextPixelSnap = value;
    }
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

    internal static GdiPlusGraphicsContext CreateDoubleBuffered(
        nint hwnd, nint screenDc, double dpiScale, ImageScaleQuality imageScaleQuality,
        bool transparentComposition = false)
    {
        User32.GetClientRect(hwnd, out var clientRect);
        int width = Math.Max(1, clientRect.Width);
        int height = Math.Max(1, clientRect.Height);
        var backBuffer = BackBuffer.GetOrCreate(hwnd, screenDc, width, height);
        var ctx = new GdiPlusGraphicsContext(hwnd, backBuffer.MemDc, width, height, dpiScale, imageScaleQuality);
        ctx._screenDc = screenDc;
        ctx._backBuffer = backBuffer;
        ctx._transparentComposition = transparentComposition;
        return ctx;
    }

    internal static void ReleaseForWindow(nint hwnd) => BackBuffer.Release(hwnd);

    internal static void ReleaseAllBackBuffers() => BackBuffer.ReleaseAll();

    internal GdiPlusGraphicsContext(
        nint hwnd,
        nint hdc,
        int pixelWidth,
        int pixelHeight,
        double dpiScale,
        ImageScaleQuality imageScaleQuality,
        bool ownsDc = false,
        GdiPixelRenderSurface? pixelSurface = null)
    {
        _hwnd = hwnd;
        Hdc = hdc;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _ownsDc = ownsDc;
        _imageScaleQuality = imageScaleQuality;
        _pixelSurface = pixelSurface;

        _dpiScale = dpiScale;
        _stateManager = new GdiStateManager(hdc, dpiScale);
        _primitiveRenderer = new GdiPrimitiveRenderer(hdc, _stateManager);

        Gdi32.SetBkMode(Hdc, GdiConstants.TRANSPARENT);
        GdiPlusInterop.EnsureInitialized();
    }

    protected override void OnBeginFrame(IRenderTarget target)
    {
        if (_pixelSurface != null && _pixelSurface.DibBits != 0)
        {
            _pixelSurface.GetPixelSpan().Clear();
            _pixelSurfaceDirtied = true;
        }
    }

    protected override void OnEndFrame()
    {
        _surfacePool.Dispose();

        if (_graphics != 0)
        {
            GdiPlusInterop.GdipDeleteGraphics(_graphics);
            _graphics = 0;
        }

        // GpBitmap is the backing image of _graphics - must release after.
        if (_gpBitmap != 0)
        {
            GdiPlusInterop.GdipDisposeImage(_gpBitmap);
            _gpBitmap = 0;
        }

        if (_pixelSurfaceDirtied && _pixelSurface != null)
        {
            _pixelSurface.IncrementVersion();
            _pixelSurfaceDirtied = false;
        }

        // Double-buffered: blit back buffer to screen
        if (_backBuffer != null)
        {
            if (_transparentComposition)
            {
                // Use per-pixel alpha blit to preserve alpha channel for DWM backdrop composition.
                Gdi32.AlphaBlend(_screenDc, 0, 0, _pixelWidth, _pixelHeight,
                    _backBuffer.MemDc, 0, 0, _pixelWidth, _pixelHeight,
                    Native.Structs.BLENDFUNCTION.SourceOver(255));
            }
            else
            {
                Gdi32.BitBlt(_screenDc, 0, 0, _pixelWidth, _pixelHeight,
                    _backBuffer.MemDc, 0, 0, 0x00CC0020); // SRCCOPY
            }
        }

        _states.Clear();
    }

    protected override void OnDispose()
    {
        CollectionPool<Stack<GraphicsStateSnapshot>>.Return(_states);

        if (_ownsDc && Hdc != 0)
        {
            User32.ReleaseDC(_hwnd, Hdc);
        }
    }

    #region State Management

    protected override void SaveCore()
    {
        _stateManager.Save();

        uint state = 0;
        if (EnsureGraphics())
        {
            GdiPlusInterop.GdipSaveGraphics(_graphics, out state);
        }

        _states.Push(new GraphicsStateSnapshot
        {
            GdiPlusState = state,
        });
    }

    protected override void RestoreCore()
    {
        _stateManager.Restore();

        if (_states.Count == 0)
        {
            return;
        }

        var state = _states.Pop();

        if (_graphics != 0 && state.GdiPlusState != 0)
        {
            GdiPlusInterop.GdipRestoreGraphics(_graphics, state.GdiPlusState);
        }

        SyncWorldTransform();
    }

    protected override void SetClipCore(Rect rect)
    {
        // Keep HDC clip in sync for GDI text rendering.
        _stateManager.SetClip(rect);

        if (!EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        GdiPlusInterop.GdipSetClipRectI(_graphics, r.left, r.top, r.Width, r.Height, GdiPlusInterop.CombineMode.Intersect);
    }

    protected override void SetClipRoundedRectCore(Rect rect, double radiusX, double radiusY)
    {
        // GDI text can only do rectangle clip; keep it close to bounds.
        _stateManager.SetClip(rect);

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

    protected override void SetClipPathCore(PathGeometry path)
    {
        var bounds = path.GetBounds();

        // HDC text clip is rectangle-only; approximate with the path's bounding box.
        _stateManager.SetClip(bounds);

        if (!EnsureGraphics())
        {
            return;
        }

        var fillMode = path.FillRule == FillRule.EvenOdd
            ? GdiPlusInterop.FillMode.Alternate
            : GdiPlusInterop.FillMode.Winding;

        if (GdiPlusInterop.GdipCreatePath(fillMode, out var gdipPath) != 0 || gdipPath == 0)
        {
            return;
        }

        try
        {
            BuildGdipPath(gdipPath, path);
            GdiPlusInterop.GdipSetClipPath(_graphics, gdipPath, GdiPlusInterop.CombineMode.Intersect);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(gdipPath);
        }
    }

    protected override void TranslateCore(double dx, double dy)
    {
        _stateManager.Translate(dx, dy);
        SyncWorldTransform();
    }

    protected override void RotateCore(double angleRadians)
    {
        _stateManager.Rotate(angleRadians);
        SyncWorldTransform();
    }

    protected override void ScaleCore(double sx, double sy)
    {
        _stateManager.Scale(sx, sy);
        SyncWorldTransform();
    }

    protected override void SetTransformCore(Matrix3x2 matrix)
    {
        _stateManager.SetTransform(matrix);
        SyncWorldTransform();
    }

    protected override Matrix3x2 GetTransformCore() => _stateManager.Transform;

    protected override void ResetTransformCore()
    {
        _stateManager.ResetTransform();
        SyncWorldTransform();
    }

    private void SyncWorldTransform()
    {
        if (!EnsureGraphics()) return;
        // Drawing coordinates are already in device pixels (via ToDevice*), so the world transform's
        // scale/rotation components (M11-M22) are dimensionless and work as-is. But translation
        // components (M31, M32) are in logical units and must be converted to device pixels.
        var m = _stateManager.Transform;
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

    protected override void ResetClipCore()
    {
        if (EnsureGraphics())
        {
            GdiPlusInterop.GdipResetClip(_graphics);
        }
    }

    #endregion

    #region Drawing Primitives (GDI+)

    public override void Clear(Color color)
    {
        if (_pixelSurface != null)
        {
            _pixelSurface.Clear(color);
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

    protected override void DrawLineCore(Point start, Point end, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        float widthPx = QuantizeStrokePx(thickness);
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
            GdiPlusInterop.GdipDrawLine(_graphics, pen, (float)ax, (float)ay, (float)bx, (float)by);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    protected override void DrawRectangleCore(Rect rect, Color color, double thickness, bool strokeInset)
    {
        color = BlendGlobalAlpha(color);
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        var outer = ToDeviceRect(rect);
        if (outer.Width <= 0 || outer.Height <= 0)
        {
            return;
        }

        if (strokeInset)
        {
            // Fill-based inset: avoids sub-pixel deflate that GDI+'s integer coords can't handle.
            var snappedThickness = LayoutRounding.SnapThicknessToPixels(thickness, _dpiScale, 1);
            var inner = ToDeviceRect(rect.Deflate(new Thickness(snappedThickness)));

            if (GdiPlusInterop.GdipCreateSolidFill(ToArgb(color), out var brush) != 0 || brush == 0)
                return;

            try
            {
                GdiPlusInterop.GdipFillRectangleI(_graphics, brush, outer.left, outer.top, outer.Width, inner.top - outer.top);
                GdiPlusInterop.GdipFillRectangleI(_graphics, brush, outer.left, inner.bottom, outer.Width, outer.bottom - inner.bottom);
                GdiPlusInterop.GdipFillRectangleI(_graphics, brush, outer.left, inner.top, inner.left - outer.left, inner.Height);
                GdiPlusInterop.GdipFillRectangleI(_graphics, brush, inner.right, inner.top, outer.right - inner.right, inner.Height);
            }
            finally
            {
                GdiPlusInterop.GdipDeleteBrush(brush);
            }
        }
        else
        {
            float widthPx = QuantizeStrokePx(thickness);
            if (widthPx <= 0)
                return;

            if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
                return;

            try
            {
                GdiPlusInterop.GdipDrawRectangleI(_graphics, pen, outer.left, outer.top, outer.Width, outer.Height);
            }
            finally
            {
                GdiPlusInterop.GdipDeletePen(pen);
            }
        }
    }

    protected override void FillRectangleCore(Rect rect, Color color)
    {
        color = BlendGlobalAlpha(color);
        if (color.A == 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreateSolidFill(ToArgb((color)), out var brush) != 0 || brush == 0)
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

    protected override void DrawRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        color = BlendGlobalAlpha(color);
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        rect = new(rect.X * DpiScale, rect.Y * DpiScale, rect.Width * DpiScale, rect.Height * DpiScale);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        float widthPx = QuantizeStrokePx(thickness);
        int ew = Math.Max(1, QuantizeLengthPx(radiusX * 2));
        int eh = Math.Max(1, QuantizeLengthPx(radiusY * 2));

        if (GdiPlusInterop.GdipCreatePen1(ToArgb((color)), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
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
            int hr;
            //AddRoundedRectPathI(path, r.left, r.top, r.Width, r.Height, ew, eh);
            AddRoundedRectPathF(path, (float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height, ew, eh);
            hr = GdiPlusInterop.GdipClosePathFigure(path);
            hr = GdiPlusInterop.GdipDrawPath(_graphics, pen, path);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(path);
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    protected override void FillRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color)
    {
        color = BlendGlobalAlpha(color);
        if (color.A == 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        rect = new(rect.X * DpiScale, rect.Y * DpiScale, rect.Width * DpiScale, rect.Height * DpiScale);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        int ew = Math.Max(1, QuantizeLengthPx(radiusX * 2));
        int eh = Math.Max(1, QuantizeLengthPx(radiusY * 2));

        if (GdiPlusInterop.GdipCreateSolidFill(ToArgb((color)), out var brush) != 0 || brush == 0)
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
            AddRoundedRectPathF(path, (float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height, ew, eh);
            GdiPlusInterop.GdipClosePathFigure(path);
            GdiPlusInterop.GdipFillPath(_graphics, brush, path);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(path);
            GdiPlusInterop.GdipDeleteBrush(brush);
        }
    }

    protected override void DrawEllipseCore(Rect bounds, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(bounds);
        bounds = new(bounds.X * DpiScale, bounds.Y * DpiScale, bounds.Width * DpiScale, bounds.Height * DpiScale);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        float widthPx = QuantizeStrokePx(thickness);
        if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipDrawEllipse(_graphics, pen, (float)bounds.Left, (float)bounds.Top, (float)bounds.Width, (float)bounds.Height);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    protected override void FillEllipseCore(Rect bounds, Color color)
    {
        if (color.A == 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(bounds);
        bounds = new(bounds.X * DpiScale, bounds.Y * DpiScale, bounds.Width * DpiScale, bounds.Height * DpiScale);
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
            //GdiPlusInterop.GdipFillEllipseI(_graphics, brush, r.left, r.top, r.Width, r.Height);
            GdiPlusInterop.GdipFillEllipse(_graphics, brush, (float)bounds.Left, (float)bounds.Top, (float)bounds.Width, (float)bounds.Height);
        }
        finally
        {
            GdiPlusInterop.GdipDeleteBrush(brush);
        }
    }

    public override void DrawPath(PathGeometry path, Color color, double thickness = 1)
    {
        RecordDrawPath();
        if (path == null || color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        float widthPx = QuantizeStrokePx(thickness);
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

    public override void FillPath(PathGeometry path, Color color)
        => FillPath(path, color, path?.FillRule ?? FillRule.NonZero);

    public override void FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        RecordFillPath();
        color = BlendGlobalAlpha(color);
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

    public override void FillRectangle(Rect rect, IBrush brush)
    {
        if (brush is ISolidColorBrush solid)
        {
            FillRectangle(rect, solid.Color); return;
        }
        if (brush is IImageBrush imageBrush && EnsureGraphics())
        {
            var (x, y) = ToDeviceCoords(rect.X, rect.Y);
            float w = (float)(rect.Width * _dpiScale);
            float h = (float)(rect.Height * _dpiScale);
            if (w <= 0 || h <= 0) return;
            if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0) return;
            try
            {
                GdiPlusInterop.GdipAddPathRectangle(path, (float)x, (float)y, w, h);
                nint texBrush = CreateGdipTextureBrush(imageBrush);
                if (texBrush != 0)
                {
                    try { GdiPlusInterop.GdipFillPath(_graphics, texBrush, path); }
                    finally { GdiPlusInterop.GdipDeleteBrush(texBrush); }
                }
            }
            finally { GdiPlusInterop.GdipDeletePath(path); }
            return;
        }
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

    public override void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush)
    {
        if (brush is ISolidColorBrush solid)
        {
            FillRoundedRectangle(rect, radiusX, radiusY, solid.Color); return;
        }
        if (brush is IImageBrush imageBrush && EnsureGraphics())
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
                nint texBrush = CreateGdipTextureBrush(imageBrush);
                if (texBrush != 0)
                {
                    try { GdiPlusInterop.GdipFillPath(_graphics, texBrush, path); }
                    finally { GdiPlusInterop.GdipDeleteBrush(texBrush); }
                }
            }
            finally { GdiPlusInterop.GdipDeletePath(path); }
            return;
        }
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

    public override void FillEllipse(Rect bounds, IBrush brush)
    {
        if (brush is ISolidColorBrush solid)
        {
            FillEllipse(bounds, solid.Color); return;
        }
        if (brush is IImageBrush imageBrush && EnsureGraphics())
        {
            var (x, y) = ToDeviceCoords(bounds.X, bounds.Y);
            float w = (float)(bounds.Width * _dpiScale);
            float h = (float)(bounds.Height * _dpiScale);
            if (w <= 0 || h <= 0) return;
            if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0) return;
            try
            {
                GdiPlusInterop.GdipAddPathEllipse(path, (float)x, (float)y, w, h);
                nint texBrush = CreateGdipTextureBrush(imageBrush);
                if (texBrush != 0)
                {
                    try { GdiPlusInterop.GdipFillPath(_graphics, texBrush, path); }
                    finally { GdiPlusInterop.GdipDeleteBrush(texBrush); }
                }
            }
            finally { GdiPlusInterop.GdipDeletePath(path); }
            return;
        }
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

    public override void FillPath(PathGeometry path, IBrush brush)
        => FillPath(path, brush, path?.FillRule ?? FillRule.NonZero);

    public override void FillPath(PathGeometry path, IBrush brush, FillRule fillRule)
    {
        if (brush is not ISolidColorBrush)
        {
            RecordFillPath();
        }
        if (path == null) return;
        if (brush is ISolidColorBrush solid)
        {
            FillPath(path, solid.Color, fillRule); return;
        }
        if (brush is IImageBrush imageBrush && EnsureGraphics())
        {
            var fillMode = fillRule == FillRule.EvenOdd ? GdiPlusInterop.FillMode.Alternate : GdiPlusInterop.FillMode.Winding;
            if (GdiPlusInterop.GdipCreatePath(fillMode, out var gdipPath) != 0 || gdipPath == 0) return;
            try
            {
                BuildGdipPath(gdipPath, path);
                nint texBrush = CreateGdipTextureBrush(imageBrush);
                if (texBrush != 0)
                {
                    try { GdiPlusInterop.GdipFillPath(_graphics, texBrush, gdipPath); }
                    finally { GdiPlusInterop.GdipDeleteBrush(texBrush); }
                }
            }
            finally { GdiPlusInterop.GdipDeletePath(gdipPath); }
            return;
        }
        if (brush is IGradientBrush gradient && EnsureGraphics())
        {
            var fillMode = fillRule == FillRule.EvenOdd ? GdiPlusInterop.FillMode.Alternate : GdiPlusInterop.FillMode.Winding;
            if (GdiPlusInterop.GdipCreatePath(fillMode, out var gdipPath) != 0 || gdipPath == 0) return;
            try
            {
                BuildGdipPath(gdipPath, path);
                // OBB requires path bounds; fall back to representative color for path+OBB.
                if (gradient.GradientUnits == GradientUnits.ObjectBoundingBox)
                {
                    FillPath(path, gradient.GetRepresentativeColor()); return;
                }
                var pathBounds = ComputeApproximateBounds(path);
                FillWithGradient(gradient, default, b => GdiPlusInterop.GdipFillPath(_graphics, b, gdipPath), pathBounds);
            }
            finally { GdiPlusInterop.GdipDeletePath(gdipPath); }
        }
    }

    public override void DrawLine(Point start, Point end, IPen pen)
    {
        if (pen.Thickness <= 0 || !EnsureGraphics()) return;

        float widthPx = QuantizeStrokePx(pen.Thickness);
        if (widthPx <= 0) return;

        var (ax, ay) = ToDeviceCoords(start.X, start.Y);
        var (bx, by) = ToDeviceCoords(end.X, end.Y);

        if (!TryCreateStyledPen(pen, widthPx, out var gdipPen)) return;
        try
        {
            GdiPlusInterop.GdipDrawLine(_graphics, gdipPen, (float)ax, (float)ay, (float)bx, (float)by);
        }
        finally { GdiPlusInterop.GdipDeletePen(gdipPen); }
    }

    public override void DrawRectangle(Rect rect, IPen pen)
    {
        if (pen.Thickness <= 0 || !EnsureGraphics()) return;

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0) return;

        float widthPx = QuantizeStrokePx(pen.Thickness);
        if (!TryCreateStyledPen(pen, widthPx, out var gdipPen)) return;
        try
        {
            GdiPlusInterop.GdipDrawRectangleI(_graphics, gdipPen, r.left, r.top, r.Width, r.Height);
        }
        finally { GdiPlusInterop.GdipDeletePen(gdipPen); }
    }

    public override void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen)
    {
        if (pen.Thickness <= 0 || !EnsureGraphics()) return;

        var r = ToDeviceRect(rect);
        rect = new(rect.X * DpiScale, rect.Y * DpiScale, rect.Width * DpiScale, rect.Height * DpiScale);
        if (r.Width <= 0 || r.Height <= 0) return;

        float widthPx = QuantizeStrokePx(pen.Thickness);
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
            AddRoundedRectPathF(path, (float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height, ew, eh);
            GdiPlusInterop.GdipClosePathFigure(path);
            GdiPlusInterop.GdipDrawPath(_graphics, gdipPen, path);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(path);
            GdiPlusInterop.GdipDeletePen(gdipPen);
        }
    }

    public override void DrawEllipse(Rect bounds, IPen pen)
    {
        if (pen.Thickness <= 0 || !EnsureGraphics()) return;

        var r = ToDeviceRect(bounds);
        bounds = new(bounds.X * DpiScale, bounds.Y * DpiScale, bounds.Width * DpiScale, bounds.Height * DpiScale);
        if (r.Width <= 0 || r.Height <= 0) return;

        float widthPx = QuantizeStrokePx(pen.Thickness);
        if (!TryCreateStyledPen(pen, widthPx, out var gdipPen)) return;
        try
        {
            GdiPlusInterop.GdipDrawEllipse(_graphics, gdipPen, (float)bounds.Left, (float)bounds.Top, (float)bounds.Width, (float)bounds.Height);
        }
        finally { GdiPlusInterop.GdipDeletePen(gdipPen); }
    }

    public override void DrawPath(PathGeometry path, IPen pen)
    {
        RecordDrawPath();
        if (path == null || pen.Thickness <= 0 || !EnsureGraphics()) return;

        float widthPx = QuantizeStrokePx(pen.Thickness);
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
            uint argb = pen.Brush is ISolidColorBrush solid ? ToArgb(BlendGlobalAlpha(solid.Color)) : 0xFF000000;
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
            int dashCount = dashes.Count;
            Span<float> arr = stackalloc float[dashCount];
            for (int i = 0; i < dashCount; i++)
                arr[i] = (float)dashes[i];
            GdiPlusInterop.SetPenDashArray(gdipPen, arr);
            if (style.DashOffset != 0)
                GdiPlusInterop.GdipSetPenDashOffset(gdipPen, (float)style.DashOffset);
        }

        return true;
    }

    private Color BlendGlobalAlpha(Color color)
    {
        if (_stateManager.GlobalAlpha == 1)
        {
            return color;
        }

        return color.WithAlpha((byte)(color.A * _stateManager.GlobalAlpha));
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

    protected override unsafe void DrawTextCore(
        ReadOnlySpan<char> text,
        Rect bounds,
        IFont font,
        Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None)
    {
        if (font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(font));
        }

        color = BlendGlobalAlpha(color);

        if (text.IsEmpty || color.A == 0)
        {
            return;
        }

        // GDI text bypasses GDI+ WorldTransform — apply transform manually.
        bool hasTextTransform = TryGetTextWorldTransform(out var textTransform);
        var drawBounds = hasTextTransform ? bounds : TransformRect(bounds);
        var cullBounds = hasTextTransform ? TransformRect(bounds) : drawBounds;

        // Early cull: skip if the bounds rect is entirely outside the current clip region.
        var cullR = ToDeviceRect(cullBounds);
        if (Gdi32.RectVisible(Hdc, ref cullR) == 0)
        {
            return;
        }

        if (!hasTextTransform && (_pixelSurface != null || color.A < 255 || EnableAlphaTextHint))
        {
            var r = GetTextLayoutRect(drawBounds, wrapping);
            uint format = BuildTextFormat(horizontalAlignment, verticalAlignment, wrapping, trimming);
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
                _pixelSurface,
                _surfacePool,
                text,
                r,
                gdiFont,
                color,
                format,
                yOffsetPx,
                textHeightPx,
                wrapping,
                trimming,
                horizontalAlignment,
                verticalAlignment);
            return;
        }

        int textState = 0;
        nint oldFont = 0;
        uint oldColor = 0;

        try
        {
            if (hasTextTransform)
            {
                textState = Gdi32.SaveDC(Hdc);
                Gdi32.SetGraphicsMode(Hdc, GdiConstants.GM_ADVANCED);
                Gdi32.SetWorldTransform(Hdc, ref textTransform);
            }

            oldFont = Gdi32.SelectObject(Hdc, gdiFont.GetHandle(GdiFontRenderMode.Default));
            oldColor = Gdi32.SetTextColor(Hdc, color.ToCOLORREF());

            var r = GetTextLayoutRect(drawBounds, wrapping);
            uint format = BuildTextFormat(horizontalAlignment, verticalAlignment, wrapping, trimming);
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

            int clipState = ApplyTextClip(r);

            bool drawn = false;
            if (trimming == TextTrimming.CharacterEllipsis && wrapping != TextWrapping.NoWrap)
            {
                drawn = GdiWrappedEllipsisHelper.TryDrawWrappedWithEllipsis(Hdc, text, r, r.Width, r.Height, horizontalAlignment, verticalAlignment);
            }

            if (!drawn)
            {
                fixed (char* pText = text)
                {
                    ApplyVerticalOffset(ref r, yOffsetPx, textHeightPx);
                    Gdi32.DrawText(Hdc, pText, text.Length, ref r, format);
                }
            }

            RestoreTextClip(clipState);
        }
        finally
        {
            if (textState != 0)
            {
                Gdi32.RestoreDC(Hdc, textState);
            }
            else
            {
                Gdi32.SetTextColor(Hdc, oldColor);
                Gdi32.SelectObject(Hdc, oldFont);
            }
        }
    }

    public override TextLayout CreateTextLayout(ReadOnlySpan<char> text,
        TextFormat format, in TextLayoutConstraints constraints)
    {
        var bounds = constraints.Bounds;
        var safeBounds = new Rect(bounds.X, bounds.Y,
            double.IsPositiveInfinity(bounds.Width) ? 0 : bounds.Width,
            double.IsPositiveInfinity(bounds.Height) ? 0 : bounds.Height);

        Size measured;
        if (format.Wrapping == TextWrapping.NoWrap)
            measured = MeasureText(text, format.Font);
        else
        {
            double maxWidth = safeBounds.Width > 0 ? safeBounds.Width : MeasureText(text, format.Font).Width;
            measured = MeasureText(text, format.Font, maxWidth);
        }

        double effectiveMaxWidth = safeBounds.Width > 0 ? safeBounds.Width : measured.Width;

        return new TextLayout
        {
            MeasuredSize = measured,
            EffectiveBounds = safeBounds,
            EffectiveMaxWidth = effectiveMaxWidth,
            ContentHeight = measured.Height
        };
    }

    public override unsafe void DrawTextLayout(ReadOnlySpan<char> text,
        TextFormat format, TextLayout layout, Color color)
    {
        if (format.Font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(format));
        }

        color = BlendGlobalAlpha(color);

        if (text.IsEmpty || color.A == 0)
        {
            return;
        }

        var horizontalAlignment = format.HorizontalAlignment;
        var verticalAlignment = format.VerticalAlignment;
        var wrapping = format.Wrapping;
        var trimming = format.Trimming;

        // GDI text bypasses GDI+ WorldTransform — apply transform manually.
        bool hasTextTransform = TryGetTextWorldTransform(out var textTransform);
        var bounds = hasTextTransform ? layout.EffectiveBounds : TransformRect(layout.EffectiveBounds);
        var cullBounds = hasTextTransform ? TransformRect(layout.EffectiveBounds) : bounds;

        // Early cull: skip if the bounds rect is entirely outside the current clip region.
        var cullR = ToDeviceRect(cullBounds);
        if (Gdi32.RectVisible(Hdc, ref cullR) == 0)
        {
            return;
        }

        if (!hasTextTransform && (_pixelSurface != null || color.A < 255 || EnableAlphaTextHint))
        {
            var r = GetTextLayoutRect(bounds, wrapping);
            uint gdiFormat = BuildTextFormat(horizontalAlignment, verticalAlignment, wrapping, trimming);
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
                _pixelSurface,
                _surfacePool,
                text,
                r,
                gdiFont,
                color,
                gdiFormat,
                yOffsetPx,
                textHeightPx,
                wrapping,
                trimming,
                horizontalAlignment,
                verticalAlignment);
            return;
        }

        int textState = 0;
        nint oldFont = 0;
        uint oldColor = 0;

        try
        {
            if (hasTextTransform)
            {
                textState = Gdi32.SaveDC(Hdc);
                Gdi32.SetGraphicsMode(Hdc, GdiConstants.GM_ADVANCED);
                Gdi32.SetWorldTransform(Hdc, ref textTransform);
            }

            oldFont = Gdi32.SelectObject(Hdc, gdiFont.GetHandle(GdiFontRenderMode.Default));
            oldColor = Gdi32.SetTextColor(Hdc, color.ToCOLORREF());

            var r = GetTextLayoutRect(bounds, wrapping);
            uint gdiFormat = BuildTextFormat(horizontalAlignment, verticalAlignment, wrapping, trimming);
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

            int clipState = ApplyTextClip(r);

            bool drawn = false;
            if (trimming == TextTrimming.CharacterEllipsis && wrapping != TextWrapping.NoWrap)
            {
                drawn = GdiWrappedEllipsisHelper.TryDrawWrappedWithEllipsis(Hdc, text, r, r.Width, r.Height, horizontalAlignment, verticalAlignment);
            }

            if (!drawn)
            {
                fixed (char* pText = text)
                {
                    ApplyVerticalOffset(ref r, yOffsetPx, textHeightPx);
                    Gdi32.DrawText(Hdc, pText, text.Length, ref r, gdiFormat);
                }
            }

            RestoreTextClip(clipState);
        }
        finally
        {
            if (textState != 0)
            {
                Gdi32.RestoreDC(Hdc, textState);
            }
            else
            {
                Gdi32.SetTextColor(Hdc, oldColor);
                Gdi32.SelectObject(Hdc, oldFont);
            }
        }
    }

    private int ApplyTextClip(RECT boundsPx)
    {
        int clipState = Gdi32.SaveDC(Hdc);
        if (clipState != 0)
        {
            Gdi32.IntersectClipRect(Hdc, boundsPx.left, boundsPx.top, boundsPx.right, boundsPx.bottom);
        }

        return clipState;
    }

    private bool TryGetTextWorldTransform(out XFORM xform)
    {
        var m = _stateManager.Transform;
        if (m.M11 == 1f && m.M12 == 0f && m.M21 == 0f && m.M22 == 1f)
        {
            xform = default;
            return false;
        }

        xform = new XFORM
        {
            eM11 = m.M11,
            eM12 = m.M12,
            eM21 = m.M21,
            eM22 = m.M22,
            eDx = (float)(m.M31 * _dpiScale),
            eDy = (float)(m.M32 * _dpiScale)
        };
        return true;
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

    private static uint BuildTextFormat(TextAlignment horizontalAlignment, TextAlignment verticalAlignment, TextWrapping wrapping, TextTrimming trimming = TextTrimming.None)
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

        if (trimming == TextTrimming.CharacterEllipsis)
        {
            format |= GdiConstants.DT_END_ELLIPSIS;
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

    public override unsafe Size MeasureText(ReadOnlySpan<char> text, IFont font)
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

    public override unsafe Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
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

    public override void DrawImage(IImage image, Point location)
    {
        if (image is not GdiImage gdiImage)
        {
            throw new ArgumentException("Image must be a GdiImage", nameof(image));
        }

        DrawImage(gdiImage, new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight));
    }

    protected override void DrawImageCore(IImage image, Rect destRect)
    {
        if (image is not GdiImage gdiImage)
        {
            throw new ArgumentException("Image must be a GdiImage", nameof(image));
        }

        DrawImage(gdiImage, destRect, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
    }

    protected override void DrawImageCore(IImage image, Rect destRect, Rect sourceRect)
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

        // Check if the current transform requires GDI+ (rotation/skew).
        // GDI AlphaBlend only supports axis-aligned blitting.
        var m = _stateManager.Transform;
        bool isAxisAligned = m.M12 == 0 && m.M21 == 0;

        if (!isAxisAligned)
        {
            DrawImageGdiPlus(gdiImage, destRect, sourceRect);
            return;
        }

        // Axis-aligned transform: apply transform manually (GDI AlphaBlend ignores WorldTransform).
        var destPx = _stateManager.ToDeviceRect(destRect);
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

            // Large upscales (dest >> src) are expensive to resample — fall back to
            // GDI stretch which is hardware-accelerated and handles its own clipping.
            const int MaxResamplePixels = 2048 * 2048;
            if (effective == ImageScaleQuality.Fast ||
                ((long)destPx.Width * destPx.Height > MaxResamplePixels && destPx.Width > srcW && destPx.Height > srcH))
            {
                // Nearest / large-upscale fallback: rely on GDI stretch + alpha blend (COLORONCOLOR).
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

            // 1:1 fast path: direct blit from the source DIB, skips _scaled cache's
            // per-frame box-filter copy (identity scale still walks pixel-by-pixel there).
            if (destPx.Width == srcW && destPx.Height == srcH)
            {
                if (gdiImage.IsOpaque)
                {
                    // All pixels alpha == 255 → SRCCOPY (~2-3x faster than AlphaBlend).
                    Gdi32.BitBlt(
                        Hdc, destPx.left, destPx.top, destPx.Width, destPx.Height,
                        memDc, srcX, srcY, GdiConstants.SRCCOPY);
                }
                else
                {
                    var blend11 = BLENDFUNCTION.SourceOver(255);
                    Gdi32.AlphaBlend(
                        Hdc, destPx.left, destPx.top, destPx.Width, destPx.Height,
                        memDc, srcX, srcY, srcW, srcH,
                        blend11);
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

    /// <summary>
    /// Draws an image via GDI+ DrawImageRectRect, which respects the WorldTransform
    /// for rotation/skew. Used as fallback when GDI AlphaBlend cannot handle the transform.
    /// </summary>
    private void DrawImageGdiPlus(GdiImage gdiImage, Rect destRect, Rect sourceRect)
    {
        var m = _stateManager.Transform;
        float dpi = (float)_dpiScale;

        var effective = ImageScaleQuality == ImageScaleQuality.Default
            ? (_imageScaleQuality == ImageScaleQuality.Default ? ImageScaleQuality.Normal : _imageScaleQuality)
            : ImageScaleQuality;

        int destWPx = (int)Math.Round(destRect.Width * dpi);
        int destHPx = (int)Math.Round(destRect.Height * dpi);

        if (destWPx <= 0 || destHPx <= 0) return;

        // Try cached transformed bitmap — avoids per-frame GDI+ rendering in immediate mode.
        if (gdiImage.TryGetTransformedBitmap(
                m.M11, m.M12, m.M21, m.M22,
                sourceRect, destWPx, destHPx,
                effective,
                out nint txBmp, out int txW, out int txH))
        {
            // Compute the center of the dest rect in device pixels.
            float cx = (float)(destRect.X + destRect.Width * 0.5);
            float cy = (float)(destRect.Y + destRect.Height * 0.5);
            var center = Vector2.Transform(new Vector2(cx, cy), m);
            int blitX = (int)Math.Round(center.X * dpi - txW * 0.5);
            int blitY = (int)Math.Round(center.Y * dpi - txH * 0.5);

            // AlphaBlend the cached DIB directly (bypasses WorldTransform).
            var txDc = Gdi32.CreateCompatibleDC(Hdc);
            var oldTx = Gdi32.SelectObject(txDc, txBmp);
            try
            {
                var blend = BLENDFUNCTION.SourceOver(255);
                Gdi32.AlphaBlend(
                    Hdc, blitX, blitY, txW, txH,
                    txDc, 0, 0, txW, txH,
                    blend);
            }
            finally
            {
                Gdi32.SelectObject(txDc, oldTx);
                Gdi32.DeleteDC(txDc);
            }
            return;
        }

        // Fallback: GDI+ DrawImage with WorldTransform.
        if (!EnsureGraphics()) return;

        var gpBitmap = gdiImage.GetGdiPlusBitmap();
        if (gpBitmap == 0) return;

        var interpMode = effective switch
        {
            ImageScaleQuality.Fast => GdiPlusInterop.InterpolationMode.NearestNeighbor,
            ImageScaleQuality.HighQuality => GdiPlusInterop.InterpolationMode.HighQualityBicubic,
            _ => GdiPlusInterop.InterpolationMode.Bilinear,
        };
        GdiPlusInterop.GdipSetInterpolationMode(_graphics, interpMode);

        GdiPlusInterop.GdipDrawImageRectRect(
            _graphics, gpBitmap,
            (float)destRect.X * dpi, (float)destRect.Y * dpi,
            (float)destRect.Width * dpi, (float)destRect.Height * dpi,
            (float)sourceRect.X, (float)sourceRect.Y,
            (float)sourceRect.Width, (float)sourceRect.Height,
            GdiPlusInterop.Unit.Pixel, 0, 0, 0);
    }

    private static bool IsNearInt(double value) => Math.Abs(value - Math.Round(value)) <= 0.0001;

    #endregion

    private bool EnsureGraphics()
    {
        if (_graphics != 0)
        {
            return true;
        }

        if (_pixelSurface != null && _pixelSurface.DibBits != 0)
        {
            const int PixelFormat32bppPArgb = 0x000E200B;
            int strideBytes = _pixelSurface.PixelWidth * 4;
            if (GdiPlusInterop.GdipCreateBitmapFromScan0(
                    _pixelSurface.PixelWidth,
                    _pixelSurface.PixelHeight,
                    strideBytes,
                    PixelFormat32bppPArgb,
                    _pixelSurface.DibBits,
                    out _gpBitmap) != 0 || _gpBitmap == 0)
            {
                return false;
            }

            if (GdiPlusInterop.GdipGetImageGraphicsContext(_gpBitmap, out _graphics) != 0 || _graphics == 0)
            {
                GdiPlusInterop.GdipDisposeImage(_gpBitmap);
                _gpBitmap = 0;
                return false;
            }

            _pixelSurfaceDirtied = true;
        }
        else
        {
            if (GdiPlusInterop.GdipCreateFromHDC(Hdc, out _graphics) != 0 || _graphics == 0)
            {
                return false;
            }
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

        int hr;
        hr = GdiPlusInterop.GdipAddPathArc(path, x, y, ew, eh, 180, 90);
        hr = GdiPlusInterop.GdipAddPathArc(path, right - ew, y, ew, eh, 270, 90);
        hr = GdiPlusInterop.GdipAddPathArc(path, right - ew, bottom - eh, ew, eh, 0, 90);
        hr = GdiPlusInterop.GdipAddPathArc(path, x, bottom - eh, ew, eh, 90, 90);
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

    private Point TransformPoint(Point pt)
    {
        var v = Vector2.Transform(new Vector2((float)pt.X, (float)pt.Y), _stateManager.Transform);
        return new Point(v.X, v.Y);
    }

    private Rect TransformRect(Rect rect)
    {
        var m = _stateManager.Transform;
        var tl = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Y), m);
        var tr = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Y), m);
        var bl = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Bottom), m);
        var br = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Bottom), m);
        float minX = Math.Min(Math.Min(tl.X, tr.X), Math.Min(bl.X, br.X));
        float minY = Math.Min(Math.Min(tl.Y, tr.Y), Math.Min(bl.Y, br.Y));
        float maxX = Math.Max(Math.Max(tl.X, tr.X), Math.Max(bl.X, br.X));
        float maxY = Math.Max(Math.Max(tl.Y, tr.Y), Math.Max(bl.Y, br.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private POINT ToDevicePoint(Point pt)
    {
        // Transform is applied by GDI+ World Transform; only DPI scale here.
        return new POINT(
            (int)Math.Round(pt.X * _dpiScale),
            (int)Math.Round(pt.Y * _dpiScale));
    }

    private RECT ToDeviceRect(Rect rect)
    {
        // Transform is applied by GDI+ World Transform; only DPI scale here.
        var (left, top, right, bottom) = RenderingUtil.ToDeviceRect(rect, 0, 0, _dpiScale);
        return new RECT(left, top, right, bottom);
    }

    private Rect ToDeviceRectF(Rect rect)
    {
        // Transform is applied by GDI+ World Transform; only DPI scale here.
        var (left, top, right, bottom) = RenderingUtil.ToDeviceRect(rect, 0, 0, _dpiScale);
        return new Rect(left, top, right, bottom);
    }


    /// <summary>
    /// Returns the snapped stroke width in device pixels. The pen is created with
    /// <c>Unit.Pixel</c> so this value is interpreted as raw device pixels, not subject
    /// to GDI+ World Transform. We pre-multiply by the user transform's average scale
    /// so the stroke scales with transform (matching Skia/WPF/SVG defaults) while
    /// device-pixel snapping keeps the line crisp on fractional DPI/zoom.
    /// </summary>
    private float QuantizeStrokePx(double thicknessDip)
    {
        if (thicknessDip <= 0) return 0;
        var t = _stateManager.Transform;
        float sx = MathF.Sqrt(t.M11 * t.M11 + t.M12 * t.M12);
        float sy = MathF.Sqrt(t.M21 * t.M21 + t.M22 * t.M22);
        float avgScale = (sx + sy) * 0.5f;
        float totalScale = avgScale * (float)_dpiScale;
        return MathF.Max(1, MathF.Round((float)thicknessDip * totalScale));
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
        // Transform is applied by GDI+ World Transform; only DPI scale here.
        return (x * _dpiScale, y * _dpiScale);
    }

    private double ToDevicePx(double logicalValue) => logicalValue * _dpiScale;

    /// <summary>
    /// Creates a GDI+ gradient brush for use with pen creation.
    /// Caller must release the returned handle. Returns 0 on failure.
    /// </summary>
    /// <summary>
    /// Creates a GDI+ matrix from an SVG gradient transform (user-space), converted to device space.
    /// Caller must release with GdipDeleteMatrix. Returns 0 when <paramref name="transform"/> is null.
    /// </summary>
    private nint CreateGdipGradientMatrix(Matrix3x2? transform)
    {
        if (transform is not Matrix3x2 gradientTransform) return 0;
        float scale = (float)_dpiScale;
        if (GdiPlusInterop.GdipCreateMatrix2(
                gradientTransform.M11, gradientTransform.M12,
                gradientTransform.M21, gradientTransform.M22,
                gradientTransform.M31 * scale, gradientTransform.M32 * scale,
                out nint matrix) != 0)
        {
            return 0;
        }
        return matrix;
    }

    private nint CreateGdipGradientBrush(IGradientBrush brush, Rect objectBounds)
    {
        var stops = brush.Stops;
        if (stops == null || stops.Count == 0) return 0;

        int maxStops = stops.Count + 2;
        Span<uint> colors = maxStops <= 16 ? stackalloc uint[16] : new uint[maxStops];
        Span<float> positions = maxStops <= 16 ? stackalloc float[16] : new float[maxStops];
        int count = FillEndpointStops(stops, colors, positions);
        colors = colors[..count];
        positions = positions[..count];
        for (int i = 0; i < count; i++)
        {
            colors[i] = BlendGlobalAlpha(Color.FromArgb(colors[i])).ToArgb();
        }

        var wrapMode = brush.SpreadMethod switch
        {
            SpreadMethod.Reflect => GdiPlusInterop.WrapMode.TileFlipXY,
            SpreadMethod.Repeat => GdiPlusInterop.WrapMode.Tile,
            _ => GdiPlusInterop.WrapMode.TileFlipXY,
        };

        if (brush is ILinearGradientBrush linear)
        {
            var p1 = ResolveGradientPoint(linear.StartPoint, brush.GradientUnits, objectBounds);
            var p2 = ResolveGradientPoint(linear.EndPoint, brush.GradientUnits, objectBounds);
            var (ax, ay) = ToDeviceCoords(p1.X, p1.Y);
            var (bx, by) = ToDeviceCoords(p2.X, p2.Y);
            var gp1 = new GdiPlusInterop.PointF((float)ax, (float)ay);
            var gp2 = new GdiPlusInterop.PointF((float)bx, (float)by);

            if (GdiPlusInterop.GdipCreateLineBrush(ref gp1, ref gp2,
                    colors[0], colors[^1], wrapMode, out nint gradBrush) != 0 || gradBrush == 0) return 0;
            GdiPlusInterop.SetLinePresetBlend(gradBrush, colors, positions);
            nint linearMatrix = CreateGdipGradientMatrix(brush.GradientTransform);
            if (linearMatrix != 0)
            {
                try { GdiPlusInterop.GdipSetLineTransform(gradBrush, linearMatrix); }
                finally { GdiPlusInterop.GdipDeleteMatrix(linearMatrix); }
            }
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
            nint radialMatrix = CreateGdipGradientMatrix(brush.GradientTransform);
            if (radialMatrix != 0)
            {
                try { GdiPlusInterop.GdipMultiplyPathGradientTransform(gradBrush, radialMatrix, GdiPlusInterop.MatrixOrder.Prepend); }
                finally { GdiPlusInterop.GdipDeleteMatrix(radialMatrix); }
            }
            return gradBrush;
        }

        return 0;
    }

    private static void ReverseStops(Span<uint> colors, Span<float> positions)
    {
        colors.Reverse();
        positions.Reverse();
        for (int i = 0; i < positions.Length; i++)
            positions[i] = 1f - positions[i];
    }

    /// <summary>
    /// Creates a GDI+ TextureBrush from an <see cref="IImageBrush"/>. The brush transform is
    /// composed so that one tile appears at <c>DestinationRect</c> in logical coords, honoring
    /// any SVG <c>patternTransform</c> and the context's DPI scale. Returns 0 when the image
    /// is not a <see cref="GdiImage"/> (other backends' images are not renderable here).
    /// Caller owns the returned brush handle and must delete it via <see cref="GdiPlusInterop.GdipDeleteBrush"/>.
    /// </summary>
    private nint CreateGdipTextureBrush(IImageBrush imageBrush)
    {
        if (imageBrush.Image is not GdiImage gdiImage) return 0;
        gdiImage.EnsureUpToDate();

        nint gpBitmap = gdiImage.GetGdiPlusBitmap();
        if (gpBitmap == 0) return 0;

        var tileMode = imageBrush.TileMode;
        var wrapMode = tileMode switch
        {
            TileMode.None => GdiPlusInterop.WrapMode.Clamp,
            _ => GdiPlusInterop.WrapMode.Tile,
        };

        // Use the full image as the tile. sourceRect semantics on the IImageBrush are in the
        // image's DIP space (which for offscreen-rendered pattern tiles equals pixel space).
        if (GdiPlusInterop.GdipCreateTexture2(
                gpBitmap, wrapMode,
                0f, 0f, gdiImage.PixelWidth, gdiImage.PixelHeight,
                out nint texBrush) != 0 || texBrush == 0)
        {
            return 0;
        }

        // Compose the brush transform: bitmap-pixel → device-pixel (world coords for GDI+).
        // Order: scale bitmap-px → logical DIPs (dst.W/imageW) → translate to dst.XY (logical)
        //        → apply user patternTransform (logical) → scale to device (dpiScale).
        var src = imageBrush.SourceRect;
        var dst = imageBrush.DestinationRect;
        float scaleX = gdiImage.PixelWidth > 0 ? (float)(dst.Width / gdiImage.PixelWidth) : 1f;
        float scaleY = gdiImage.PixelHeight > 0 ? (float)(dst.Height / gdiImage.PixelHeight) : 1f;

        var m =
            System.Numerics.Matrix3x2.CreateTranslation(-(float)src.X, -(float)src.Y) *
            System.Numerics.Matrix3x2.CreateScale(scaleX, scaleY) *
            System.Numerics.Matrix3x2.CreateTranslation((float)dst.X, (float)dst.Y);

        if (imageBrush.Transform.HasValue)
        {
            m *= imageBrush.Transform.Value;
        }

        m *= System.Numerics.Matrix3x2.CreateScale((float)_dpiScale);

        // Seam avoidance — same reasoning as D2D backend: pixel-snap translate when axis-aligned,
        // otherwise sub-pixel tile origins create visible seams at every tile boundary from
        // interpolated AA-edge texels bleeding across the wrap.
        bool axisAligned = m.M12 == 0f && m.M21 == 0f;
        if (axisAligned)
        {
            m.M31 = MathF.Round(m.M31);
            m.M32 = MathF.Round(m.M32);
        }

        if (GdiPlusInterop.GdipCreateMatrix2(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32, out nint matrix) == 0 && matrix != 0)
        {
            try { GdiPlusInterop.GdipSetTextureTransform(texBrush, matrix); }
            finally { GdiPlusInterop.GdipDeleteMatrix(matrix); }
        }

        return texBrush;
    }

    private void FillWithGradient(IGradientBrush brush, Rect objectBounds, Action<nint> fillAction, Rect fillBounds = default)
    {
        var stops = brush.Stops;
        if (stops == null || stops.Count == 0) return;

        // Max stops: original + 2 endpoints + 1 expansion entry
        int maxStops = stops.Count + 3;
        Span<uint> colors = maxStops <= 16 ? stackalloc uint[16] : new uint[maxStops];
        Span<float> positions = maxStops <= 16 ? stackalloc float[16] : new float[maxStops];
        int count = FillEndpointStops(stops, colors, positions);
        colors = colors[..count];
        positions = positions[..count];
        for (int i = 0; i < count; i++)
        {
            colors[i] = BlendGlobalAlpha(Color.FromArgb(colors[i])).ToArgb();
        }

        // GDI+ LinearGradientBrush does NOT support WrapMode.Clamp;
        // use TileFlipXY as the closest approximation for SpreadMethod.Pad.
        var wrapMode = brush.SpreadMethod switch
        {
            SpreadMethod.Reflect => GdiPlusInterop.WrapMode.TileFlipXY,
            SpreadMethod.Repeat => GdiPlusInterop.WrapMode.Tile,
            _ => GdiPlusInterop.WrapMode.TileFlipXY,
        };

        if (brush is ILinearGradientBrush linear)
        {
            var p1 = ResolveGradientPoint(linear.StartPoint, brush.GradientUnits, objectBounds);
            var p2 = ResolveGradientPoint(linear.EndPoint, brush.GradientUnits, objectBounds);
            var (ax, ay) = ToDeviceCoords(p1.X, p1.Y);
            var (bx, by) = ToDeviceCoords(p2.X, p2.Y);
            var gp1 = new GdiPlusInterop.PointF((float)ax, (float)ay);
            var gp2 = new GdiPlusInterop.PointF((float)bx, (float)by);

            if (GdiPlusInterop.GdipCreateLineBrush(ref gp1, ref gp2,
                    colors[0], colors[^1], wrapMode, out nint gradBrush) != 0 || gradBrush == 0) return;
            try
            {
                GdiPlusInterop.SetLinePresetBlend(gradBrush, colors, positions);
                nint linearMatrix = CreateGdipGradientMatrix(brush.GradientTransform);
                if (linearMatrix != 0)
                {
                    try { GdiPlusInterop.GdipSetLineTransform(gradBrush, linearMatrix); }
                    finally { GdiPlusInterop.GdipDeleteMatrix(linearMatrix); }
                }
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

            // Expansion is computed in the gradient's CANONICAL space (cx/cy/rx/ry's
            // space), but fillBounds is in user space. When the SVG gradient carries
            // a non-identity gradientTransform, user space and canonical space differ,
            // so corners must be mapped through the inverse gradientTransform first
            // (otherwise the computed expansion is garbage and the path gradient can
            // miss the geometry entirely — visible at low zoom where GDI+ flattens
            // the ellipse path more coarsely, leaving large empty/tiled regions).
            Matrix3x2 inverseGradTransform = Matrix3x2.Identity;
            if (brush.GradientTransform is Matrix3x2 gt)
            {
                if (!Matrix3x2.Invert(gt, out inverseGradTransform))
                {
                    inverseGradTransform = Matrix3x2.Identity;
                }
            }

            double expansion = 1.0;
            if (fillBounds.Width > 0 && fillBounds.Height > 0)
            {
                var (fL, fT) = ToDeviceCoords(fillBounds.X, fillBounds.Y);
                var (fR, fB) = ToDeviceCoords(fillBounds.Right, fillBounds.Bottom);
                double maxT = 0;
                ReadOnlySpan<(double fx, double fy)> corners =
                    [(fL, fT), (fR, fT), (fR, fB), (fL, fB)];
                foreach (var (fx, fy) in corners)
                {
                    // Map (user*dpi) → (canonical*dpi). The inverse matrix's M11..M22
                    // are dimensionless; M31/M32 live in user-space units, so scale
                    // them by dpi to match the (*dpi) input.
                    double canonicalX = inverseGradTransform.M11 * fx + inverseGradTransform.M21 * fy
                        + inverseGradTransform.M31 * _dpiScale;
                    double canonicalY = inverseGradTransform.M12 * fx + inverseGradTransform.M22 * fy
                        + inverseGradTransform.M32 * _dpiScale;
                    double dx = (canonicalX - cx) / rx;
                    double dy = (canonicalY - cy) / ry;
                    double t = dx * dx + dy * dy;
                    if (t > maxT) maxT = t;
                }
                if (maxT > 1.0)
                    expansion = Math.Sqrt(maxT) + 0.05;
            }

            const float pad = 3f;
            double effRx = rx * expansion + pad;
            double effRy = ry * expansion + pad;

            if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out nint ellipsePath) != 0 || ellipsePath == 0) return;
            try
            {
                // Build the ellipse in the gradient's CANONICAL space × dpi.
                GdiPlusInterop.GdipAddPathEllipse(ellipsePath,
                    (float)(cx - effRx), (float)(cy - effRy), (float)(effRx * 2), (float)(effRy * 2));

                // Then bake gradientTransform into the path itself so the final path
                // lives in user-space × dpi. This avoids relying on the path gradient's
                // brush transform, which GDI+ rasterizes poorly when the canonical path
                // is far from the geometry being filled (visible as missing/distorted
                // fills at low zoom: GDI+ appears to flatten and sample the brush based
                // on device-space bounds of the path, and when canonical-space coords
                // are large and the fill uses a brush transform to compress them, the
                // internal gradient texture is resolved at inadequate resolution).
                nint pathMatrix = CreateGdipGradientMatrix(brush.GradientTransform);
                if (pathMatrix != 0)
                {
                    try { GdiPlusInterop.GdipTransformPath(ellipsePath, pathMatrix); }
                    finally { GdiPlusInterop.GdipDeleteMatrix(pathMatrix); }
                }

                if (GdiPlusInterop.GdipCreatePathGradientFromPath(ellipsePath, out nint gradBrush) != 0 || gradBrush == 0) return;
                try
                {
                    // Transform the focal/origin point through the same gradientTransform
                    // so it lives in user-space × dpi alongside the path.
                    var origin = ResolveGradientPoint(radial.GradientOrigin, brush.GradientUnits, objectBounds);
                    var (ox, oy) = ToDeviceCoords(origin.X, origin.Y);
                    double uox = ox, uoy = oy;
                    if (brush.GradientTransform is Matrix3x2 gtc)
                    {
                        uox = gtc.M11 * ox + gtc.M21 * oy + gtc.M31 * _dpiScale;
                        uoy = gtc.M12 * ox + gtc.M22 * oy + gtc.M32 * _dpiScale;
                    }
                    var centerPt = new GdiPlusInterop.PointF((float)uox, (float)uoy);
                    GdiPlusInterop.GdipSetPathGradientCenterPoint(gradBrush, ref centerPt);

                    // GDI+ PathGradient preset blend: position 0 = boundary, 1 = center.
                    // Reverse into separate span to avoid mutating original stops.
                    Span<uint> revColors = count + 1 <= 16 ? stackalloc uint[16] : new uint[count + 1];
                    Span<float> revPositions = count + 1 <= 16 ? stackalloc float[16] : new float[count + 1];
                    colors.CopyTo(revColors);
                    positions.CopyTo(revPositions);
                    var revC = revColors[..count];
                    var revP = revPositions[..count];
                    ReverseStops(revC, revP);

                    // When expanded, remap stops so the gradient looks the same within
                    // the original radius; fill the expanded zone with the boundary color.
                    if (expansion > 1.0)
                    {
                        double totalF = expansion + pad / Math.Max(rx, 1.0);
                        // Shift existing entries right by 1 to insert boundary entry at [0]
                        for (int i = count; i > 0; i--)
                        {
                            revColors[i] = revColors[i - 1];
                            revPositions[i] = revPositions[i - 1];
                        }
                        revColors[0] = revColors[1]; // boundary color fills expanded zone
                        revPositions[0] = 0f;
                        for (int i = 1; i <= count; i++)
                        {
                            revPositions[i] = (float)(1.0 - (1.0 - revPositions[i]) / totalF);
                        }
                        revC = revColors[..(count + 1)];
                        revP = revPositions[..(count + 1)];
                    }

                    GdiPlusInterop.SetPathGradientPresetBlend(gradBrush, revC, revP);
                    GdiPlusInterop.GdipSetPathGradientWrapMode(gradBrush, wrapMode);
                    // Path is already in user-space × dpi; no brush transform needed.
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
    /// Fills <paramref name="colors"/> and <paramref name="positions"/> with sorted gradient stops,
    /// inserting endpoint stops at 0.0 and 1.0 if missing. Returns the actual count used.
    /// Caller must provide spans of at least <c>stops.Count + 2</c> elements.
    /// </summary>
    private static int FillEndpointStops(IReadOnlyList<GradientStop> stops,
        Span<uint> colors, Span<float> positions)
    {
        // Copy and sort by offset using a small stackalloc buffer
        int n = stops.Count;
        Span<GradientStop> sorted = n <= 16
            ? stackalloc GradientStop[16]
            : new GradientStop[n];
        sorted = sorted[..n];
        for (int i = 0; i < n; i++) sorted[i] = stops[i];
        sorted.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        int idx = 0;
        if (sorted[0].Offset > 0.0)
        {
            var c = sorted[0].Color;
            colors[idx] = (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
            positions[idx] = 0f;
            idx++;
        }

        for (int i = 0; i < n; i++)
        {
            var s = sorted[i];
            colors[idx] = (uint)(s.Color.A << 24 | s.Color.R << 16 | s.Color.G << 8 | s.Color.B);
            positions[idx] = (float)Math.Clamp(s.Offset, 0f, 1f);
            idx++;
        }

        if (sorted[n - 1].Offset < 1.0)
        {
            var c = sorted[n - 1].Color;
            colors[idx] = (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
            positions[idx] = 1f;
            idx++;
        }

        return idx;
    }

    private readonly struct GraphicsStateSnapshot
    {
        public required uint GdiPlusState { get; init; }
    }

    #region BackBuffer (double-buffering)

    private sealed class BackBuffer : IDisposable
    {
        private static readonly Dictionary<nint, BackBuffer> Cache = new();

        public static BackBuffer GetOrCreate(nint hwnd, nint screenDc, int width, int height)
        {
            if (Cache.TryGetValue(hwnd, out var existing))
            {
                existing.EnsureSize(screenDc, width, height);
                return existing;
            }

            var buffer = new BackBuffer(hwnd, screenDc, width, height);
            Cache[hwnd] = buffer;
            return buffer;
        }

        public static void Release(nint hwnd)
        {
            if (Cache.Remove(hwnd, out var buffer))
            {
                buffer.Dispose();
            }
        }

        public static void ReleaseAll()
        {
            foreach (var (_, buffer) in Cache)
                buffer.Dispose();
            Cache.Clear();
        }

        private nint _bitmap;
        private nint _oldBitmap;
        private nint _bits;

        public nint MemDc { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        private BackBuffer(nint hwnd, nint screenDc, int width, int height)
        {
            Create(screenDc, width, height);
        }

        private void Create(nint screenDc, int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);

            MemDc = Gdi32.CreateCompatibleDC(screenDc);
            var bmi = BITMAPINFO.Create32bpp(Width, Height);
            _bitmap = Gdi32.CreateDIBSection(screenDc, ref bmi, usage: 0, out _bits, 0, 0);
            _oldBitmap = Gdi32.SelectObject(MemDc, _bitmap);
        }

        private void Destroy()
        {
            if (MemDc == 0) return;

            if (_oldBitmap != 0) Gdi32.SelectObject(MemDc, _oldBitmap);
            if (_bitmap != 0) Gdi32.DeleteObject(_bitmap);
            Gdi32.DeleteDC(MemDc);

            MemDc = 0;
            _bitmap = 0;
            _oldBitmap = 0;
            _bits = 0;
            Width = 0;
            Height = 0;
        }

        public void EnsureSize(nint screenDc, int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (width == Width && height == Height && MemDc != 0 && _bitmap != 0)
                return;

            Destroy();
            Create(screenDc, width, height);
        }

        public void Dispose() => Destroy();
    }

    #endregion
}
