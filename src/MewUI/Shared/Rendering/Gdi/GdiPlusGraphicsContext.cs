using System.Numerics;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering.Gdi.Core;
using Aprillz.MewUI.Rendering.Win32;
using Aprillz.MewUI.Text;

using static Aprillz.MewUI.Rendering.GradientBrushHelper;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI+ graphics context (vector/clip quality), while keeping GDI text measurement/rendering.
/// </summary>
internal sealed class GdiPlusGraphicsContext : GraphicsContextBase, ITransparentDamageContext, IOpaqueDamageContext, IPartialPresentContext
{
    private readonly nint _hwnd;
    private readonly bool _ownsDc;
    private readonly ImageScaleQuality _imageScaleQuality;
    private readonly GdiPixelRenderSurface? _pixelSurface;

    private readonly int _pixelWidth;
    private readonly int _pixelHeight;

    private readonly GdiStateManager _stateManager;
    private readonly GdiPrimitiveRenderer _primitiveRenderer;

    // Per-HWND contexts share the long-lived pool/caches owned by WindowRenderResources (survives across
    // frames, since a new GdiPlusGraphicsContext is created every frame). Pixel-surface contexts (hwnd == 0,
    // e.g. offscreen render targets) have no stable per-window key, so they fall back to a private pool
    // scoped to this context's single frame, same as before this cache was introduced.
    private readonly AaSurfacePool _surfacePool;
    private readonly GdiTextCache? _textCache;
    private readonly GdiPlusResourceCache? _penBrushCache;

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

    internal static void ReleaseForWindow(nint hwnd)
    {
        BackBuffer.Release(hwnd);
        WindowRenderResources.Release(hwnd);
    }

    internal static void ReleaseAllBackBuffers()
    {
        BackBuffer.ReleaseAll();
        WindowRenderResources.ReleaseAll();
    }

    internal static void TrimWindowResourceCaches()
    {
        WindowRenderResources.TrimAll();
        FaceCoverageCache.Clear();
    }

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

        if (hwnd != 0)
        {
            var resources = WindowRenderResources.GetOrCreate(hwnd);
            _surfacePool = resources.SurfacePool;
            _textCache = resources.TextCache;
            _penBrushCache = resources.PenBrushCache;
        }
        else
        {
            _surfacePool = new AaSurfacePool();
        }

        Gdi32.SetBkMode(Hdc, GdiConstants.TRANSPARENT);
        GdiPlusInterop.EnsureInitialized();
    }

    // Areas the caller said this frame changed. Unless the caller said so, the whole buffer is copied.
    private readonly List<Rect> _presentAreas = [];
    private bool _presentLimited;

    void IPartialPresentContext.LimitPresentTo(IReadOnlyList<Rect> areas)
    {
        _presentAreas.Clear();
        _presentLimited = true;
        for (int index = 0; index < areas.Count; index++)
        {
            _presentAreas.Add(areas[index]);
        }
    }

    protected override void OnBeginFrame(IRenderTarget target)
    {
        _opaqueBackdropDepth = 0;
        if (_pixelSurface != null &&
            _pixelSurface.DibBits != 0 &&
            !_pixelSurface.PreserveContentsOnBeginFrame)
        {
            _pixelSurface.GetPixelSpan().Clear();
            _pixelSurfaceDirtied = true;
        }
    }

    protected override void OnEndFrame()
    {
        // Windowed contexts share a pool owned by WindowRenderResources (lives across frames); only the
        // pixel-surface fallback pool is scoped to this single frame and needs disposing here.
        if (_hwnd == 0)
        {
            _surfacePool.Dispose();
        }

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
            else if (_presentLimited)
            {
                // The window still shows the previous frame, so only what this one changed is copied.
                for (int index = 0; index < _presentAreas.Count; index++)
                {
                    var area = ToDeviceRect(_presentAreas[index]);
                    int left = Math.Clamp(area.left, 0, _pixelWidth);
                    int top = Math.Clamp(area.top, 0, _pixelHeight);
                    int right = Math.Clamp(area.right, left, _pixelWidth);
                    int bottom = Math.Clamp(area.bottom, top, _pixelHeight);
                    Gdi32.BitBlt(_screenDc, left, top, right - left, bottom - top,
                        _backBuffer.MemDc, left, top, 0x00CC0020); // SRCCOPY
                }
            }
            else
            {
                Gdi32.BitBlt(_screenDc, 0, 0, _pixelWidth, _pixelHeight,
                    _backBuffer.MemDc, 0, 0, 0x00CC0020); // SRCCOPY
            }
        }

        _presentAreas.Clear();
        _presentLimited = false;
        _states.Clear();
    }

    protected override void OnDispose()
    {
        CollectionPool.Return(_states);

        // An aborted frame (exception before OnEndFrame) would otherwise leak the pixel-surface fallback
        // pool's DIB/DC. Idempotent: AaSurfacePool.Dispose is a no-op if already disposed. The windowed
        // pool is owned by WindowRenderResources and must outlive this per-frame context, so it is not
        // touched here.
        if (_hwnd == 0)
        {
            _surfacePool.Dispose();
        }

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

    void ITransparentDamageContext.ClearRectangleToTransparent(Rect rect)
        => ClearRectangleCore(rect, Color.FromArgb(0, 0, 0, 0));

    void IOpaqueDamageContext.ClearRectangle(Rect rect, Color color)
        => ClearRectangleCore(rect, color);

    private const double ERASE_EDGE_TOLERANCE_PX = 0.001;

    private void ClearRectangleCore(Rect rect, Color color)
    {
        // The caller hands over whole pixels expressed in layout units, and the way back to pixels
        // lands a hair off them. Covering outward from there would erase a pixel the clip that follows
        // does not repaint, so an edge that close to a pixel is that pixel.
        int left = (int)Math.Floor(ToDevicePx(rect.Left) + ERASE_EDGE_TOLERANCE_PX);
        int top = (int)Math.Floor(ToDevicePx(rect.Top) + ERASE_EDGE_TOLERANCE_PX);
        int right = (int)Math.Ceiling(ToDevicePx(rect.Right) - ERASE_EDGE_TOLERANCE_PX);
        int bottom = (int)Math.Ceiling(ToDevicePx(rect.Bottom) - ERASE_EDGE_TOLERANCE_PX);

        if (_pixelSurface == null)
        {
            // A window is drawn through a buffer kept per window, which has no alpha to erase: the
            // area is filled with the colour the window is cleared to.
            var area = new RECT(left, top, right, bottom);
            nint brush = Gdi32.CreateSolidBrush(color.ToCOLORREF());
            try
            {
                Gdi32.FillRect(Hdc, ref area, brush);
            }
            finally
            {
                Gdi32.DeleteObject(brush);
            }
        }
        else
        {
            // GDI batches calls against the DC that shares this DIB; a pending one would land after
            // the memory write below.
            Gdi32.GdiFlush();
            _pixelSurface.ClearRectangle(left, top, right, bottom, color);
            _pixelSurfaceDirtied = true;
        }
    }

    protected override void DrawLineCore(Point start, Point end, Color color, double thickness = 1)
    {
        color = BlendGlobalAlpha(color);
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

        if (!TryRentPen(ToArgb(color), widthPx, out var pen, out var ownsPen))
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipDrawLine(_graphics, pen, (float)ax, (float)ay, (float)bx, (float)by);
        }
        finally
        {
            ReleasePen(pen, ownsPen);
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

            if (!TryRentBrush(ToArgb(color), out var brush, out var ownsBrush))
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
                ReleaseBrush(brush, ownsBrush);
            }
        }
        else
        {
            float widthPx = QuantizeStrokePx(thickness);
            if (widthPx <= 0)
                return;

            if (!TryRentPen(ToArgb(color), widthPx, out var pen, out var ownsPen))
                return;

            try
            {
                GdiPlusInterop.GdipDrawRectangleI(_graphics, pen, outer.left, outer.top, outer.Width, outer.Height);
            }
            finally
            {
                ReleasePen(pen, ownsPen);
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

        if (!TryRentBrush(ToArgb(color), out var brush, out var ownsBrush))
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipFillRectangleI(_graphics, brush, r.left, r.top, r.Width, r.Height);
        }
        finally
        {
            ReleaseBrush(brush, ownsBrush);
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

        if (!TryRentPen(ToArgb(color), widthPx, out var pen, out var ownsPen))
        {
            return;
        }

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0)
        {
            ReleasePen(pen, ownsPen);
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
            ReleasePen(pen, ownsPen);
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

        if (!TryRentBrush(ToArgb(color), out var brush, out var ownsBrush))
        {
            return;
        }

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0)
        {
            ReleaseBrush(brush, ownsBrush);
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
            ReleaseBrush(brush, ownsBrush);
        }
    }

    protected override void DrawEllipseCore(Rect bounds, Color color, double thickness = 1)
    {
        color = BlendGlobalAlpha(color);
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
        if (!TryRentPen(ToArgb(color), widthPx, out var pen, out var ownsPen))
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipDrawEllipse(_graphics, pen, (float)bounds.Left, (float)bounds.Top, (float)bounds.Width, (float)bounds.Height);
        }
        finally
        {
            ReleasePen(pen, ownsPen);
        }
    }

    protected override void FillEllipseCore(Rect bounds, Color color)
    {
        color = BlendGlobalAlpha(color);
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

        if (!TryRentBrush(ToArgb(color), out var brush, out var ownsBrush))
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
            ReleaseBrush(brush, ownsBrush);
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
        if (!TryRentPen(ToArgb(color), widthPx, out var pen, out var ownsPen))
        {
            return;
        }

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var gdipPath) != 0 || gdipPath == 0)
        {
            ReleasePen(pen, ownsPen);
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
            ReleasePen(pen, ownsPen);
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

        if (!TryRentBrush(ToArgb(color), out var brush, out var ownsBrush))
        {
            return;
        }

        var fillMode = fillRule == FillRule.EvenOdd ? GdiPlusInterop.FillMode.Alternate : GdiPlusInterop.FillMode.Winding;
        if (GdiPlusInterop.GdipCreatePath(fillMode, out var gdipPath) != 0 || gdipPath == 0)
        {
            ReleaseBrush(brush, ownsBrush);
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
            ReleaseBrush(brush, ownsBrush);
        }
    }

    public override void FillRectangle(Rect rect, Brush brush)
    {
        if (brush is SolidColorBrush solid)
        {
            FillRectangle(rect, solid.Color); return;
        }
        if (brush is ImageBrush imageBrush && EnsureGraphics())
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
        if (brush is GradientBrush gradient && EnsureGraphics())
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

    public override void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Brush brush)
    {
        if (brush is SolidColorBrush solid)
        {
            FillRoundedRectangle(rect, radiusX, radiusY, solid.Color); return;
        }
        if (brush is ImageBrush imageBrush && EnsureGraphics())
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
        if (brush is GradientBrush gradient && EnsureGraphics())
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

    public override void FillEllipse(Rect bounds, Brush brush)
    {
        if (brush is SolidColorBrush solid)
        {
            FillEllipse(bounds, solid.Color); return;
        }
        if (brush is ImageBrush imageBrush && EnsureGraphics())
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
        if (brush is GradientBrush gradient && EnsureGraphics())
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

    public override void FillPath(PathGeometry path, Brush brush)
        => FillPath(path, brush, path?.FillRule ?? FillRule.NonZero);

    public override void FillPath(PathGeometry path, Brush brush, FillRule fillRule)
    {
        if (brush is not SolidColorBrush)
        {
            RecordFillPath();
        }
        if (path == null) return;
        if (brush is SolidColorBrush solid)
        {
            FillPath(path, solid.Color, fillRule); return;
        }
        if (brush is ImageBrush imageBrush && EnsureGraphics())
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
        if (brush is GradientBrush gradient && EnsureGraphics())
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

    public override void DrawLine(Point start, Point end, Pen pen)
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

    public override void DrawRectangle(Rect rect, Pen pen)
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

    public override void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Pen pen)
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

    public override void DrawEllipse(Rect bounds, Pen pen)
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

    public override void DrawPath(PathGeometry path, Pen pen)
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

    // Rents a solid-ARGB pen from the per-window cache when one is available (windowed contexts), otherwise
    // creates a throwaway pen the caller must delete via ReleasePen. Only used for the plain Color/Brush
    // draw calls above; TryCreateStyledPen below has its own per-call gradient/dash-array state and is not
    // cached (see its remarks).
    private bool TryRentPen(uint argb, float widthPx, out nint pen, out bool ownsPen)
    {
        if (_penBrushCache != null)
        {
            pen = _penBrushCache.GetOrCreatePen(argb, widthPx);
            ownsPen = false;
            return pen != 0;
        }

        ownsPen = true;
        return GdiPlusInterop.GdipCreatePen1(argb, widthPx, GdiPlusInterop.Unit.Pixel, out pen) == 0 && pen != 0;
    }

    private void ReleasePen(nint pen, bool ownsPen)
    {
        if (ownsPen && pen != 0)
        {
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    private bool TryRentBrush(uint argb, out nint brush, out bool ownsBrush)
    {
        if (_penBrushCache != null)
        {
            brush = _penBrushCache.GetOrCreateBrush(argb);
            ownsBrush = false;
            return brush != 0;
        }

        ownsBrush = true;
        return GdiPlusInterop.GdipCreateSolidFill(argb, out brush) == 0 && brush != 0;
    }

    private void ReleaseBrush(nint brush, bool ownsBrush)
    {
        if (ownsBrush && brush != 0)
        {
            GdiPlusInterop.GdipDeleteBrush(brush);
        }
    }

    // Not cached: pens here may wrap a per-call gradient brush (arbitrary stops) or an arbitrary-length
    // dash array, neither of which is a cheap cache key. Caching would need to hash the dash array and
    // gradient stops on every call, which costs about as much as just creating the pen.
    private bool TryCreateStyledPen(Pen pen, float widthPx, out nint gdipPen)
    {
        gdipPen = 0;

        if (pen.Brush is GradientBrush gradient)
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
            uint argb = pen.Brush is SolidColorBrush solid ? ToArgb(BlendGlobalAlpha(solid.Color)) : 0xFF000000;
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

    public override BackendTextLayout CreateBackendTextLayout(ReadOnlySpan<char> text,
        BackendTextFormat format, in BackendTextLayoutConstraints constraints)
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

        return new BackendTextLayout
        {
            MeasuredSize = measured,
            EffectiveBounds = safeBounds,
            EffectiveMaxWidth = effectiveMaxWidth,
            ContentHeight = measured.Height
        };
    }

    // Set while a transient run is drawn: text that changes every frame bypasses the text cache.
    private bool _transientText;

    public override void DrawBackendTextLayout(ReadOnlySpan<char> text,
        BackendTextFormat format, BackendTextLayout layout, Color color, object? owner)
    {
        _transientText = ReferenceEquals(owner, Aprillz.MewUI.Text.TransientText.Owner);
        try
        {
            DrawBackendTextLayout(text, format, layout, color);
        }
        finally
        {
            _transientText = false;
        }
    }

    public override unsafe void DrawBackendTextLayout(ReadOnlySpan<char> text,
        BackendTextFormat format, BackendTextLayout layout, Color color)
    {
        if (format.Font is not GdiFont gdiFont)
        {
            // A face that rasterizes the run itself (the DirectWrite engine) hands back a bitmap, so
            // none of the GDI drawing modes below apply to it.
            if (format.Font is IWin32TextFace rasterizingFace)
            {
                DrawFaceRasterizedText(text, format, layout, color, rasterizingFace);
                return;
            }

            throw new ArgumentException("Font must be a Win32 text face", nameof(format));
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

        // GDI text bypasses GDI+ WorldTransform - apply transform manually.
        // A text-engine run rasterizes into a surface grown by its ink overhang and draws unclipped
        // inside it, so glyphs that reach past the run box survive.
        var inkInset = TextInkInsetPx.FromOverhang(layout.InkOverhang, _dpiScale);
        bool hasTextTransform = TryGetTextWorldTransform(out var textTransform);
        var bounds = hasTextTransform ? layout.EffectiveBounds : TransformRect(layout.EffectiveBounds);
        var cullBounds = hasTextTransform
            ? TransformRect(InflateByInset(layout.EffectiveBounds, inkInset))
            : InflateByInset(bounds, inkInset);

        // Early cull: skip if the bounds rect is entirely outside the current clip region.
        var cullR = ToDeviceRect(cullBounds);
        if (Gdi32.RectVisible(Hdc, ref cullR) == 0)
        {
            return;
        }

        // A surface without alpha is drawn as a window is: its alpha channel means nothing, so the
        // text path that writes none can be used and text looks the same on both.
        bool surfaceCarriesAlpha = _pixelSurface != null && _pixelSurface.HasAlpha;

        // Inside an opaque backdrop the pixels under the text are known, so the text is drawn as it is
        // on a window, with subpixel antialiasing, and the alpha that drawing clobbers is put back.
        bool overOpaqueBackdrop = surfaceCarriesAlpha && _opaqueBackdropDepth > 0 && !hasTextTransform &&
            color.A == 255 && !EnableAlphaTextHint;
        if (!hasTextTransform && !overOpaqueBackdrop && (surfaceCarriesAlpha || color.A < 255 || EnableAlphaTextHint))
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

            if (inkInset.HasInset)
            {
                r = inkInset.Inflate(r);
                gdiFormat |= GdiConstants.DT_NOCLIP;
            }

            if (_textCache != null && !_transientText)
            {
                _textCache.DrawCached(
                    Hdc, text, r, gdiFont, color, gdiFormat, yOffsetPx, textHeightPx,
                    wrapping, trimming, horizontalAlignment, verticalAlignment, inkInset);
            }
            else
            {
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
                    verticalAlignment,
                    inkInset);
            }
            return;
        }

        if (hasTextTransform && surfaceCarriesAlpha && _pixelSurface is GdiPixelRenderSurface alphaSurface)
        {
            // Transformed text on a per-pixel-alpha cache: the direct GDI path below writes no
            // alpha, so glyphs end up transparent (reading as the background colour). Render the
            // rotated text with correct premultiplied alpha instead.
            var rt = GetTextLayoutRect(bounds, wrapping);
            uint fmt = BuildTextFormat(horizontalAlignment, verticalAlignment, wrapping, trimming);
            if (inkInset.HasInset)
            {
                // The temp surface already spans the ink-grown bounding box.
                fmt |= GdiConstants.DT_NOCLIP;
            }
            int yOff = 0;
            int txtH = 0;
            if (wrapping != TextWrapping.NoWrap)
            {
                ComputeWrappedTextOffsetsPx(
                    text, gdiFont.GetHandle(GdiFontRenderMode.Coverage), rt.Width, rt.Height,
                    verticalAlignment, out yOff, out txtH);
            }

            if (PerPixelAlphaTextRenderer.DrawTextTransformed(
                    Hdc, alphaSurface, _surfacePool, text, rt, cullR, textTransform, gdiFont, color, fmt,
                    yOff, txtH, wrapping, trimming, horizontalAlignment, verticalAlignment))
            {
                return;
            }
            // Oversized bounding box: fall through to the direct GDI path below.
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

            var inkRect = inkInset.HasInset ? inkInset.Inflate(r) : r;
            int clipState = ApplyTextClip(inkRect);
            byte[]? keptAlpha = overOpaqueBackdrop ? KeepAlpha(inkRect) : null;
            if (inkInset.HasInset)
            {
                gdiFormat |= GdiConstants.DT_NOCLIP;
            }

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

            if (keptAlpha != null)
            {
                PutAlphaBack(inkRect, keptAlpha);
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

    private int _opaqueBackdropDepth;

    public override void BeginOpaqueBackdrop() => _opaqueBackdropDepth++;

    public override void EndOpaqueBackdrop()
    {
        if (_opaqueBackdropDepth > 0)
        {
            _opaqueBackdropDepth--;
        }
    }

    private bool TryClampToSurface(RECT area, out int left, out int top, out int right, out int bottom)
    {
        left = Math.Max(0, area.left);
        top = Math.Max(0, area.top);
        right = Math.Min(_pixelSurface!.PixelWidth, area.right);
        bottom = Math.Min(_pixelSurface.PixelHeight, area.bottom);
        return right > left && bottom > top;
    }

    /// <summary>Copies the alpha of the area before text is drawn over it; the text call writes zero there.</summary>
    private byte[]? KeepAlpha(RECT area)
    {
        if (!TryClampToSurface(area, out int left, out int top, out int right, out int bottom))
        {
            return null;
        }

        // Calls still batched against the DC would land after this read.
        Gdi32.GdiFlush();
        int width = right - left;
        var kept = System.Buffers.ArrayPool<byte>.Shared.Rent(width * (bottom - top));
        var pixels = _pixelSurface!.GetPixelSpan();
        int stride = _pixelSurface.StrideBytes;
        int index = 0;
        for (int row = top; row < bottom; row++)
        {
            int offset = (row * stride) + (left * 4) + 3;
            for (int column = 0; column < width; column++)
            {
                kept[index++] = pixels[offset];
                offset += 4;
            }
        }

        return kept;
    }

    private void PutAlphaBack(RECT area, byte[] kept)
    {
        if (TryClampToSurface(area, out int left, out int top, out int right, out int bottom))
        {
            Gdi32.GdiFlush();
            int width = right - left;
            var pixels = _pixelSurface!.GetPixelSpan();
            int stride = _pixelSurface.StrideBytes;
            int index = 0;
            for (int row = top; row < bottom; row++)
            {
                int offset = (row * stride) + (left * 4) + 3;
                for (int column = 0; column < width; column++)
                {
                    pixels[offset] = kept[index++];
                    offset += 4;
                }
            }

            _pixelSurfaceDirtied = true;
        }

        System.Buffers.ArrayPool<byte>.Shared.Return(kept);
    }

    protected override TextInkOverhang MeasureRunInkOverhang(ReadOnlySpan<char> text, IFont font, BackendTextLayout layout)
        => font is IWin32TextFace face ? face.GetRunInkOverhang(text) : TextInkOverhang.None;

    // What a face rasterizes into, grown as needed and reused for every run.
    private byte[]? _faceRasterBuffer;

    /// <summary>
    /// Draws a run whose face rasterizes itself. The bitmap comes back with straight alpha, which
    /// <c>AlphaBlend</c> cannot take, so it is premultiplied on the way into the blend surface.
    /// </summary>
    private void DrawFaceRasterizedText(ReadOnlySpan<char> text, BackendTextFormat format,
        BackendTextLayout layout, Color color, IWin32TextFace face)
    {
        color = BlendGlobalAlpha(color);
        if (text.IsEmpty || color.A == 0)
        {
            return;
        }

        var inkInset = TextInkInsetPx.FromOverhang(layout.InkOverhang, _dpiScale);
        var target = ToDeviceRect(InflateByInset(TransformRect(layout.EffectiveBounds), inkInset));
        int widthPx = target.right - target.left;
        int heightPx = target.bottom - target.top;
        if (widthPx <= 0 || heightPx <= 0 || Gdi32.RectVisible(Hdc, ref target) == 0)
        {
            return;
        }

        // Either result is blended into the target before this method returns, so one buffer serves every run.
        int rasterBytes = widthPx * heightPx * 4;
        if (_faceRasterBuffer is null || _faceRasterBuffer.Length < rasterBytes)
        {
            _faceRasterBuffer = new byte[Math.Max(rasterBytes, 16384)];
        }

        var request = new Win32TextRasterizeRequest(
            widthPx, heightPx, color,
            format.HorizontalAlignment, format.VerticalAlignment,
            format.Wrapping, format.Trimming, _dpiScale, inkInset, _faceRasterBuffer);

        // Subpixel antialiasing needs a coverage value per channel, which one alpha per pixel cannot
        // carry. Blending it takes reading the pixels under the run, so it is only available where
        // those are known: no transform, an opaque colour, and either a surface without alpha or an
        // opaque backdrop behind it. That is the same condition the GDI path uses for ClearType.
        bool destinationIsKnown = !TryGetTextWorldTransform(out _) && color.A == 255 && !EnableAlphaTextHint
            && (!(_pixelSurface?.HasAlpha ?? false) || _opaqueBackdropDepth > 0);
        if (destinationIsKnown)
        {
            if (!FaceCoverageCache.TryGet(text, face, in request, out var coverage, out bool hasSubpixelForm))
            {
                hasSubpixelForm = face.TryRasterizeSubpixel(text, request, out coverage);
                FaceCoverageCache.Add(text, face, in request, in coverage, hasSubpixelForm);
            }

            if (hasSubpixelForm)
            {
                BlendSubpixelCoverage(coverage, target, color);
                return;
            }
        }

        if (!face.TryRasterize(text, request, out var bitmap) || bitmap.Data.Length == 0)
        {
            return;
        }

        using var surface = new AaSurface(Hdc, bitmap.WidthPx, bitmap.HeightPx);
        if (!surface.IsValid)
        {
            return;
        }

        var destination = surface.GetPixelSpan();
        int bytes = Math.Min(destination.Length, bitmap.WidthPx * bitmap.HeightPx * 4);
        for (int i = 0; i < bytes; i += 4)
        {
            byte alpha = bitmap.Data[i + 3];
            destination[i] = (byte)(bitmap.Data[i] * alpha / 255);
            destination[i + 1] = (byte)(bitmap.Data[i + 1] * alpha / 255);
            destination[i + 2] = (byte)(bitmap.Data[i + 2] * alpha / 255);
            destination[i + 3] = alpha;
        }

        surface.AlphaBlendTo(Hdc, target.left, target.top);
    }

    /// <summary>
    /// Lerps the text colour into the pixels under the run, one coverage value per channel. The
    /// destination is copied in and written back whole, because subpixel blending has to read what
    /// is already there and <c>AlphaBlend</c> carries only one alpha per pixel.
    /// </summary>
    private void BlendSubpixelCoverage(Win32TextCoverage coverage, RECT target, Color color)
    {
        int width = coverage.WidthPx;
        int height = coverage.HeightPx;
        if (width <= 0 || height <= 0 || coverage.Data.Length < width * height * 3)
        {
            return;
        }

        using var surface = new AaSurface(Hdc, width, height);
        if (!surface.IsValid ||
            !Gdi32.BitBlt(surface.MemDc, 0, 0, width, height, Hdc, target.left, target.top, GdiConstants.SRCCOPY))
        {
            return;
        }

        var destination = surface.GetPixelSpan();
        var channels = coverage.Data;
        int pixels = Math.Min(width * height, destination.Length / 4);
        for (int i = 0; i < pixels; i++)
        {
            int source = i * 3;
            int target32 = i * 4;
            destination[target32] = Lerp(destination[target32], color.B, channels[source + 2]);
            destination[target32 + 1] = Lerp(destination[target32 + 1], color.G, channels[source + 1]);
            destination[target32 + 2] = Lerp(destination[target32 + 2], color.R, channels[source]);
            destination[target32 + 3] = 255;
        }

        Gdi32.BitBlt(Hdc, target.left, target.top, width, height, surface.MemDc, 0, 0, GdiConstants.SRCCOPY);
    }

    private static byte Lerp(byte destination, byte source, byte coverage)
        => coverage == 0 ? destination : (byte)(((destination * (255 - coverage)) + (source * coverage)) / 255);

    private Rect InflateByInset(Rect rect, TextInkInsetPx inset)
    {
        if (!inset.HasInset)
        {
            return rect;
        }

        return new Rect(
            rect.X - inset.Left / _dpiScale,
            rect.Y - inset.Top / _dpiScale,
            rect.Width + (inset.Left + inset.Right) / _dpiScale,
            rect.Height + (inset.Top + inset.Bottom) / _dpiScale);
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

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        => font is IWin32TextFace face
            ? face.Measure(text, double.PositiveInfinity, TextWrapping.NoWrap, DpiScale)
            : throw new ArgumentException("Font must be a Win32 text face.", nameof(font));

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => font is IWin32TextFace face
            ? face.Measure(text, maxWidth, TextWrapping.Wrap, DpiScale)
            : throw new ArgumentException("Font must be a Win32 text face.", nameof(font));

    #endregion

    #region Image Rendering (GDI)

    public override void DrawImage(IImage image, Point location)
    {
        // Size from the logical image: a pooled surface behind it can be larger than the
        // content, and the backend image reports that whole allocation.
        var dest = new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight);
        if (ImageResource.ResolveBackendImage(image) is not GdiImage gdiImage)
        {
            throw new ArgumentException("Image must be a GdiImage", nameof(image));
        }

        DrawImageCore(gdiImage, dest, new Rect(0, 0, dest.Width, dest.Height));
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

        // Honor the context's global alpha so image fades / opacity work. AlphaBlend's
        // SourceConstantAlpha multiplies the (premultiplied) per-pixel alpha.
        byte constAlpha = (byte)Math.Clamp((int)Math.Round(_stateManager.GlobalAlpha * 255.0), 0, 255);
        if (constAlpha == 0)
        {
            return;
        }

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

        // Within a pixel of native size: snap to the source and take the 1:1 path below, or AlphaBlend
        // stretches the bitmap and softens its pixel-snapped text.
        int nativeSrcW = (int)sourceRect.Width;
        int nativeSrcH = (int)sourceRect.Height;
        if (m.M11 == 1f && m.M22 == 1f &&
            Math.Abs(destPx.Width - nativeSrcW) <= 1 && Math.Abs(destPx.Height - nativeSrcH) <= 1)
        {
            destPx = RECT.FromLTRB(destPx.left, destPx.top, destPx.left + nativeSrcW, destPx.top + nativeSrcH);
        }

        // Resolve backend default:
        // - Default => factory default (which is Linear by default to match other backends)
        // - NearestNeighbor => GDI stretch with COLORONCOLOR (fast, pixelated)
        // - Linear => cached bilinear resample
        // - HighQuality => cached prefiltered downscale + bilinear
        var effective = ImageScaleQuality == ImageScaleQuality.Default
            ? (_imageScaleQuality == ImageScaleQuality.Default ? ImageScaleQuality.Normal : _imageScaleQuality)
            : ImageScaleQuality;

        nint borrowedDc = gdiImage.BorrowedDc;
        nint memDc;
        nint oldBitmap = 0;
        if (borrowedDc != 0)
        {
            memDc = borrowedDc;
        }
        else
        {
            memDc = Gdi32.CreateCompatibleDC(Hdc);
            oldBitmap = Gdi32.SelectObject(memDc, gdiImage.Handle);
        }

        try
        {
            int srcX = (int)sourceRect.X;
            int srcY = (int)sourceRect.Y;
            int srcW = (int)sourceRect.Width;
            int srcH = (int)sourceRect.Height;

            // Large upscales (dest >> src) are expensive to resample - fall back to
            // GDI stretch which is hardware-accelerated and handles its own clipping.
            const int MaxResamplePixels = 2048 * 2048;
            if (effective == ImageScaleQuality.Fast ||
                ((long)destPx.Width * destPx.Height > MaxResamplePixels && destPx.Width > srcW && destPx.Height > srcH))
            {
                // Nearest / large-upscale fallback: rely on GDI stretch + alpha blend (COLORONCOLOR).
                int oldStretchMode = Gdi32.SetStretchBltMode(Hdc, GdiConstants.COLORONCOLOR);
                try
                {
                    var blend = BLENDFUNCTION.SourceOver(constAlpha);
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
                if (gdiImage.IsOpaque && constAlpha == 255)
                {
                    // All pixels alpha == 255 and no global alpha → SRCCOPY (~2-3x faster than AlphaBlend).
                    Gdi32.BitBlt(
                        Hdc, destPx.left, destPx.top, destPx.Width, destPx.Height,
                        memDc, srcX, srcY, GdiConstants.SRCCOPY);
                }
                else
                {
                    var blend11 = BLENDFUNCTION.SourceOver(constAlpha);
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
                    var blendScaled = BLENDFUNCTION.SourceOver(constAlpha);
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
                var blend = BLENDFUNCTION.SourceOver(constAlpha);
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
            if (borrowedDc == 0)
            {
                Gdi32.SelectObject(memDc, oldBitmap);
                Gdi32.DeleteDC(memDc);
            }
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

        // Honor global alpha (applied via AlphaBlend on the cached path below). The uncached
        // GDI+ DrawImageRectRect fallback does not yet apply it (rare rotated/skewed case).
        byte constAlpha = (byte)Math.Clamp((int)Math.Round(_stateManager.GlobalAlpha * 255.0), 0, 255);
        if (constAlpha == 0)
        {
            return;
        }

        var effective = ImageScaleQuality == ImageScaleQuality.Default
            ? (_imageScaleQuality == ImageScaleQuality.Default ? ImageScaleQuality.Normal : _imageScaleQuality)
            : ImageScaleQuality;

        int destWPx = (int)Math.Round(destRect.Width * dpi);
        int destHPx = (int)Math.Round(destRect.Height * dpi);

        if (destWPx <= 0 || destHPx <= 0) return;

        // Try cached transformed bitmap - avoids per-frame GDI+ rendering in immediate mode.
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
                var blend = BLENDFUNCTION.SourceOver(constAlpha);
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

    private nint CreateGdipGradientBrush(GradientBrush brush, Rect objectBounds)
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

        if (brush is LinearGradientBrush linear)
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
                try { GdiPlusInterop.GdipMultiplyLineTransform(gradBrush, linearMatrix, GdiPlusInterop.MatrixOrder.Append); }
                finally { GdiPlusInterop.GdipDeleteMatrix(linearMatrix); }
            }
            return gradBrush;
        }

        if (brush is RadialGradientBrush radial)
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
    /// Creates a GDI+ TextureBrush from an <see cref="ImageBrush"/>. The brush transform is
    /// composed so that one tile appears at <c>DestinationRect</c> in logical coords, honoring
    /// any SVG <c>patternTransform</c> and the context's DPI scale. Returns 0 when the image
    /// is not a <see cref="GdiImage"/> (other backends' images are not renderable here).
    /// Caller owns the returned brush handle and must delete it via <see cref="GdiPlusInterop.GdipDeleteBrush"/>.
    /// </summary>
    private nint CreateGdipTextureBrush(ImageBrush imageBrush)
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

        // Use the full image as the tile. sourceRect semantics on the ImageBrush are in the
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

        // Seam avoidance - same reasoning as D2D backend: pixel-snap translate when axis-aligned,
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

    private void FillWithGradient(GradientBrush brush, Rect objectBounds, Action<nint> fillAction, Rect fillBounds = default)
    {
        var stops = brush.Stops;
        if (stops == null || stops.Count == 0) return;

        // Max stops: original + 2 endpoints + 2 clamp entries added when a Pad line is stretched
        int maxStops = stops.Count + 4;
        Span<uint> colors = maxStops <= 16 ? stackalloc uint[16] : new uint[maxStops];
        Span<float> positions = maxStops <= 16 ? stackalloc float[16] : new float[maxStops];
        int count = FillEndpointStops(stops, colors, positions);
        colors = colors[..count];
        positions = positions[..count];
        for (int i = 0; i < count; i++)
        {
            colors[i] = BlendGlobalAlpha(Color.FromArgb(colors[i])).ToArgb();
        }

        // GDI+ has no WrapMode.Clamp;
        // use TileFlipXY as the closest approximation for SpreadMethod.Pad.
        var wrapMode = brush.SpreadMethod switch
        {
            SpreadMethod.Reflect => GdiPlusInterop.WrapMode.TileFlipXY,
            SpreadMethod.Repeat => GdiPlusInterop.WrapMode.Tile,
            _ => GdiPlusInterop.WrapMode.TileFlipXY,
        };

        if (brush is LinearGradientBrush linear)
        {
            var p1 = ResolveGradientPoint(linear.StartPoint, brush.GradientUnits, objectBounds);
            var p2 = ResolveGradientPoint(linear.EndPoint, brush.GradientUnits, objectBounds);
            var (ax, ay) = ToDeviceCoords(p1.X, p1.Y);
            var (bx, by) = ToDeviceCoords(p2.X, p2.Y);

            Span<uint> stretchedColors = count + 2 <= 16 ? stackalloc uint[16] : new uint[count + 2];
            Span<float> stretchedPositions = count + 2 <= 16 ? stackalloc float[16] : new float[count + 2];
            var lineColors = colors;
            var linePositions = positions;

            // Pad is approximated with TileFlipXY because GDI+ has no clamp wrap mode, so wherever the
            // filled area runs past the gradient line the mirrored tile shows instead of the end color.
            // Stretching the line to cover the fill and remapping the stops onto it restores clamping.
            if (brush.SpreadMethod == SpreadMethod.Pad)
            {
                int stretched = StretchLinearGradientOverFill(
                    brush, fillBounds, ref ax, ref ay, ref bx, ref by,
                    colors, positions, count, stretchedColors, stretchedPositions);
                if (stretched > 0)
                {
                    lineColors = stretchedColors[..stretched];
                    linePositions = stretchedPositions[..stretched];
                }
            }

            var gp1 = new GdiPlusInterop.PointF((float)ax, (float)ay);
            var gp2 = new GdiPlusInterop.PointF((float)bx, (float)by);

            if (GdiPlusInterop.GdipCreateLineBrush(ref gp1, ref gp2,
                    lineColors[0], lineColors[^1], wrapMode, out nint gradBrush) != 0 || gradBrush == 0) return;
            try
            {
                GdiPlusInterop.SetLinePresetBlend(gradBrush, lineColors, linePositions);
                nint linearMatrix = CreateGdipGradientMatrix(brush.GradientTransform);
                if (linearMatrix != 0)
                {
                    try { GdiPlusInterop.GdipMultiplyLineTransform(gradBrush, linearMatrix, GdiPlusInterop.MatrixOrder.Append); }
                    finally { GdiPlusInterop.GdipDeleteMatrix(linearMatrix); }
                }
                fillAction(gradBrush);
            }
            finally { GdiPlusInterop.GdipDeleteBrush(gradBrush); }
        }
        else if (brush is RadialGradientBrush radial)
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
            // miss the geometry entirely - visible at low zoom where GDI+ flattens
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
    /// <summary>
    /// Stretches a Pad gradient's line until the filled area projects inside it, and rewrites the stops
    /// onto the longer line. Returns the new stop count, or 0 when the line already covers the fill.
    /// </summary>
    private int StretchLinearGradientOverFill(
        GradientBrush brush, Rect fillBounds,
        ref double ax, ref double ay, ref double bx, ref double by,
        ReadOnlySpan<uint> colors, ReadOnlySpan<float> positions, int count,
        Span<uint> stretchedColors, Span<float> stretchedPositions)
    {
        if (fillBounds.Width <= 0 || fillBounds.Height <= 0 || count == 0)
        {
            return 0;
        }

        double dirX = bx - ax;
        double dirY = by - ay;
        double lengthSquared = dirX * dirX + dirY * dirY;
        if (lengthSquared < 1e-9)
        {
            return 0;
        }

        // The line lives in the gradient's own space, so the fill corners are mapped back through the
        // gradientTransform before they are projected onto it.
        var inverse = Matrix3x2.Identity;
        if (brush.GradientTransform is Matrix3x2 gradientTransform &&
            !Matrix3x2.Invert(gradientTransform, out inverse))
        {
            inverse = Matrix3x2.Identity;
        }

        var (left, top) = ToDeviceCoords(fillBounds.X, fillBounds.Y);
        var (right, bottom) = ToDeviceCoords(fillBounds.Right, fillBounds.Bottom);
        ReadOnlySpan<(double X, double Y)> corners =
            [(left, top), (right, top), (right, bottom), (left, bottom)];

        double minT = double.MaxValue;
        double maxT = double.MinValue;
        foreach (var (cornerX, cornerY) in corners)
        {
            // M31/M32 are in user-space units, so they scale by dpi to match the (*dpi) input.
            double canonicalX = inverse.M11 * cornerX + inverse.M21 * cornerY + inverse.M31 * _dpiScale;
            double canonicalY = inverse.M12 * cornerX + inverse.M22 * cornerY + inverse.M32 * _dpiScale;
            double t = ((canonicalX - ax) * dirX + (canonicalY - ay) * dirY) / lengthSquared;
            if (t < minT) minT = t;
            if (t > maxT) maxT = t;
        }

        // A pixel of slack keeps the mirrored tile off the edge where GDI+ samples the boundary.
        double margin = 1.0 / Math.Sqrt(lengthSquared);
        minT = Math.Min(0.0, minT - margin);
        maxT = Math.Max(1.0, maxT + margin);

        double span = maxT - minT;
        if (span <= 1.0 + 1e-6 || span > 1e6)
        {
            return 0;
        }

        ax += dirX * minT;
        ay += dirY * minT;
        bx = ax + dirX * span;
        by = ay + dirY * span;

        int written = 0;
        if (-minT / span > 0.0)
        {
            stretchedColors[written] = colors[0];
            stretchedPositions[written] = 0f;
            written++;
        }

        for (int i = 0; i < count; i++)
        {
            stretchedColors[written] = colors[i];
            stretchedPositions[written] = (float)Math.Clamp((positions[i] - minT) / span, 0.0, 1.0);
            written++;
        }

        if (stretchedPositions[written - 1] < 1f)
        {
            stretchedColors[written] = colors[count - 1];
            stretchedPositions[written] = 1f;
            written++;
        }

        return written;
    }

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
            if (MemDc == 0)
            {
                throw new InvalidOperationException("Failed to create the GDI back-buffer DC.");
            }

            var bmi = BITMAPINFO.Create32bpp(Width, Height);
            _bitmap = Gdi32.CreateDIBSection(screenDc, ref bmi, usage: 0, out _bits, 0, 0);
            if (_bitmap == 0 || _bits == 0)
            {
                Gdi32.DeleteDC(MemDc);
                MemDc = 0;
                _bitmap = 0;
                _bits = 0;
                throw new InvalidOperationException("Failed to create the GDI back-buffer bitmap.");
            }

            _oldBitmap = Gdi32.SelectObject(MemDc, _bitmap);
            if (_oldBitmap == 0 || _oldBitmap == new nint(-1))
            {
                Gdi32.DeleteObject(_bitmap);
                Gdi32.DeleteDC(MemDc);
                MemDc = 0;
                _bitmap = 0;
                _oldBitmap = 0;
                _bits = 0;
                throw new InvalidOperationException("Failed to select the GDI back-buffer bitmap.");
            }
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

    #region Window Render Resources (long-lived per-HWND: AA surface pool, text cache, GDI+ pen/brush cache)

    // A new GdiPlusGraphicsContext is created every frame (see CreateContextCore), so anything that should
    // survive across frames for a given window is parked here instead of on the context itself.
    private sealed class WindowRenderResources : IDisposable
    {
        private static readonly Dictionary<nint, WindowRenderResources> Cache = new();

        public static WindowRenderResources GetOrCreate(nint hwnd)
        {
            if (Cache.TryGetValue(hwnd, out var existing))
            {
                return existing;
            }

            var created = new WindowRenderResources();
            Cache[hwnd] = created;
            return created;
        }

        public static void Release(nint hwnd)
        {
            if (Cache.Remove(hwnd, out var resources))
            {
                resources.Dispose();
            }
        }

        public static void ReleaseAll()
        {
            foreach (var (_, resources) in Cache)
                resources.Dispose();
            Cache.Clear();
        }

        public static void TrimAll()
        {
            foreach (var resources in Cache.Values)
            {
                resources.Trim();
            }
        }

        public AaSurfacePool SurfacePool { get; } = new();

        public GdiPlusResourceCache PenBrushCache { get; } = new();

        public GdiTextCache TextCache { get; }

        private WindowRenderResources()
        {
            TextCache = new GdiTextCache(SurfacePool);
        }

        public void Trim()
        {
            TextCache.Clear();
            PenBrushCache.Clear();
            SurfacePool.Clear();
        }

        public void Dispose()
        {
            // TextCache first: its Clear() hands surfaces back to SurfacePool, which then disposes
            // everything (including those just-returned surfaces) in one pass.
            TextCache.Dispose();
            PenBrushCache.Dispose();
            SurfacePool.Dispose();
        }
    }

    /// <summary>
    /// Caches GDI+ pen/brush handles for solid ARGB fills/strokes with no per-call stroke style, so the
    /// common draw calls (rectangles, ellipses, plain paths) reuse a native handle instead of creating and
    /// deleting one per call. Styled pens (custom dash arrays, gradient brushes) are not cached here; see
    /// TryCreateStyledPen for why.
    /// </summary>
    private sealed class GdiPlusResourceCache : IDisposable
    {
        private readonly Dictionary<PenKey, CachedHandle> _pens = new();
        private readonly Dictionary<uint, CachedHandle> _brushes = new();
        private readonly LinkedList<PenKey> _penLru = new();
        private readonly LinkedList<uint> _brushLru = new();
        private bool _disposed;

        private readonly struct PenKey : IEquatable<PenKey>
        {
            public readonly uint Argb;
            public readonly float WidthPx;

            public PenKey(uint argb, float widthPx)
            {
                Argb = argb;
                WidthPx = widthPx;
            }

            public bool Equals(PenKey other) => Argb == other.Argb && WidthPx == other.WidthPx;
            public override bool Equals(object? obj) => obj is PenKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Argb, WidthPx);
        }

        private sealed class CachedHandle
        {
            public nint Handle;
            public LinkedListNode<PenKey>? PenNode;
            public LinkedListNode<uint>? BrushNode;
        }

        public nint GetOrCreatePen(uint argb, float widthPx)
        {
            if (_disposed)
            {
                return 0;
            }

            var key = new PenKey(argb, widthPx);

            if (_pens.TryGetValue(key, out var cached))
            {
                _penLru.Remove(cached.PenNode!);
                _penLru.AddFirst(cached.PenNode!);
                return cached.Handle;
            }

            while (_pens.Count >= GdiRenderingConstants.MaxCachedPens && _penLru.Last != null)
            {
                var oldKey = _penLru.Last.Value;
                if (_pens.Remove(oldKey, out var old) && old.Handle != 0)
                {
                    GdiPlusInterop.GdipDeletePen(old.Handle);
                }
                _penLru.RemoveLast();
            }

            if (GdiPlusInterop.GdipCreatePen1(argb, widthPx, GdiPlusInterop.Unit.Pixel, out var handle) != 0 || handle == 0)
            {
                return 0;
            }

            var node = _penLru.AddFirst(key);
            _pens[key] = new CachedHandle { Handle = handle, PenNode = node };
            return handle;
        }

        public nint GetOrCreateBrush(uint argb)
        {
            if (_disposed)
            {
                return 0;
            }

            if (_brushes.TryGetValue(argb, out var cached))
            {
                _brushLru.Remove(cached.BrushNode!);
                _brushLru.AddFirst(cached.BrushNode!);
                return cached.Handle;
            }

            while (_brushes.Count >= GdiRenderingConstants.MaxCachedBrushes && _brushLru.Last != null)
            {
                var oldKey = _brushLru.Last.Value;
                if (_brushes.Remove(oldKey, out var old) && old.Handle != 0)
                {
                    GdiPlusInterop.GdipDeleteBrush(old.Handle);
                }
                _brushLru.RemoveLast();
            }

            if (GdiPlusInterop.GdipCreateSolidFill(argb, out var handle) != 0 || handle == 0)
            {
                return 0;
            }

            var node = _brushLru.AddFirst(argb);
            _brushes[argb] = new CachedHandle { Handle = handle, BrushNode = node };
            return handle;
        }

        public void Clear()
        {
            foreach (var (_, cached) in _pens)
            {
                if (cached.Handle != 0)
                {
                    GdiPlusInterop.GdipDeletePen(cached.Handle);
                }
            }
            _pens.Clear();
            _penLru.Clear();

            foreach (var (_, cached) in _brushes)
            {
                if (cached.Handle != 0)
                {
                    GdiPlusInterop.GdipDeleteBrush(cached.Handle);
                }
            }
            _brushes.Clear();
            _brushLru.Clear();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Clear();
                _disposed = true;
            }
        }
    }

    #endregion
}
