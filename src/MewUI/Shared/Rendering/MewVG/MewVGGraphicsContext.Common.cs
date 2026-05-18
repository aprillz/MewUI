using System.Numerics;
using System.Runtime.CompilerServices;

using Aprillz.MewVG;
using Aprillz.MewVG.Tess;

namespace Aprillz.MewUI.Rendering.MewVG;

#if MEWUI_MEWVG_MACOS
internal sealed partial class MewVGMacOSGraphicsContext : GraphicsContextBase
#elif MEWUI_MEWVG_X11
internal sealed partial class MewVGX11GraphicsContext : GraphicsContextBase
#else
internal sealed partial class MewVGWin32GraphicsContext : GraphicsContextBase
#endif
{
#if MEWUI_MEWVG_MACOS
    private NanoVGMetal _vg;
#else
    private NanoVGGL _vg;
#endif

    private readonly Stack<(Rect? clipBoundsWorld, float globalAlpha, Matrix3x2 transform, bool textPixelSnap)> _saveStack =
        CollectionPool<Stack<(Rect? clipBoundsWorld, float globalAlpha, Matrix3x2 transform, bool textPixelSnap)>>.Rent();

    // Tracks external raster leases acquired during the current frame. Each source is
    // acquired at most once per frame and the lease is disposed at frame end after the
    // platform-specific flush/swap.
    private readonly List<(IExternalRasterSource source, IExternalRasterLease lease)> _acquiredExternalsThisFrame = new();
    private float _globalAlpha = 1f;
    private bool _textPixelSnap = true;
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private Rect? _clipBoundsWorld;
    private double _viewportWidthDip;
    private double _viewportHeightDip;
    private int _viewportWidthPx;
    private int _viewportHeightPx;
    // Frozen PathGeometry → object-space tessellation cache (static: shared across
    // windows, survives per-frame context recreation; ConditionalWeakTable ephemeron
    // semantics auto-collect entries when PathGeometry key is GC'd)
    private static readonly ConditionalWeakTable<PathGeometry, FrozenFillCacheEntry> _fillCache = new();

    private sealed class FrozenFillCacheEntry
    {
        public FrozenFillCache? NonZero;
        public FrozenFillCache? EvenOdd;
    }

    private double _dpiScale;
    public override double DpiScale => _dpiScale;

    public override ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    protected override void OnBeginFrame(IRenderTarget target)
    {
        _dpiScale = target.DpiScale <= 0 ? 1.0 : target.DpiScale;
        _viewportWidthPx = Math.Max(1, target.PixelWidth);
        _viewportHeightPx = Math.Max(1, target.PixelHeight);
        _viewportWidthDip = _viewportWidthPx / DpiScale;
        _viewportHeightDip = _viewportHeightPx / DpiScale;

        _globalAlpha = 1f;
        _textPixelSnap = true;
        _transform = Matrix3x2.Identity;
        _clipBoundsWorld = null;
        _saveStack.Clear();
        _acquiredExternalsThisFrame.Clear();

        BeginFramePlatform();
    }

    protected override void OnEndFrame()
    {
        EndFramePlatform();
        _saveStack.Clear();
        if (_acquiredExternalsThisFrame.Count > 0)
        {
            foreach (var (_, lease) in _acquiredExternalsThisFrame)
            {
                lease.Dispose();
            }
            _acquiredExternalsThisFrame.Clear();
        }
    }

    /// <summary>
    /// Acquires <paramref name="source"/> if not already acquired in this frame. The
    /// platform's DrawImage path calls this before issuing the NVG draw that samples the
    /// external texture. Idempotent within a frame: a single lease covers any number of
    /// DrawImage calls referencing the same texture.
    /// </summary>
    private IExternalRasterLease EnsureExternalAcquired(IExternalRasterSource source)
    {
        for (int i = 0; i < _acquiredExternalsThisFrame.Count; i++)
        {
            if (ReferenceEquals(_acquiredExternalsThisFrame[i].source, source))
            {
                return _acquiredExternalsThisFrame[i].lease;
            }
        }
        var lease = source.Acquire();
        _acquiredExternalsThisFrame.Add((source, lease));
        return lease;
    }

    protected override void OnDispose()
    {
        CollectionPool<Stack<(Rect? clipBoundsWorld, float globalAlpha, Matrix3x2 transform, bool textPixelSnap)>>.Return(_saveStack);
        DestroyPlatform();
    }

    /// <summary>
    /// Platform-specific per-frame initialization (MakeCurrent, GL.Viewport, vg.BeginFrame, etc.).
    /// </summary>
    partial void BeginFramePlatform();

    /// <summary>
    /// Platform-specific per-frame cleanup (vg.EndFrame, swap buffers, release context, etc.).
    /// </summary>
    partial void EndFramePlatform();

    /// <summary>
    /// Platform-specific permanent resource cleanup (delete GL context, etc.).
    /// </summary>
    partial void DestroyPlatform();

    #region State Management

    protected override void SaveCore()
    {
        _vg.Save();
        _saveStack.Push((_clipBoundsWorld, _globalAlpha, _transform, _textPixelSnap));
    }

    protected override void RestoreCore()
    {
        _vg.Restore();
        if (_saveStack.Count > 0)
        {
            var state = _saveStack.Pop();
            _clipBoundsWorld = state.clipBoundsWorld;
            _globalAlpha = state.globalAlpha;
            _textPixelSnap = state.textPixelSnap;
            _transform = state.transform;
            _vg.GlobalAlpha(_globalAlpha);
        }
    }

    protected override void SetClipCore(Rect rect)
    {
        var worldClip = TransformRectToWorldAABB(rect);
        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, worldClip);

        var clip = _clipBoundsWorld.Value;
        _vg.SetTransformMatrix(Matrix3x2.Identity);
        _vg.Scissor((float)clip.X, (float)clip.Y, (float)clip.Width, (float)clip.Height);
        _vg.SetTransformMatrix(_transform);
    }

    protected override void SetClipRoundedRectCore(Rect rect, double radiusX, double radiusY)
    {
        if (radiusX <= 0 && radiusY <= 0)
        {
            SetClip(rect);
            return;
        }

        var worldClip = TransformRectToWorldAABB(rect);
        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, worldClip);

        var clip = _clipBoundsWorld.Value;
        _vg.SetTransformMatrix(Matrix3x2.Identity);
        _vg.Scissor((float)clip.X, (float)clip.Y, (float)clip.Width, (float)clip.Height);
        _vg.SetTransformMatrix(_transform);

        float radius = (float)Math.Max(0, Math.Min(radiusX, radiusY));
        _vg.BeginPath();
        _vg.RoundedRect(
            (float)rect.X,
            (float)rect.Y,
            (float)rect.Width,
            (float)rect.Height,
            radius);
        _vg.Clip();
    }

    protected override void SetClipPathCore(PathGeometry path)
    {
        var bounds = path.GetBounds();
        var worldClip = TransformRectToWorldAABB(bounds);
        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, worldClip);

        var clip = _clipBoundsWorld.Value;
        _vg.SetTransformMatrix(Matrix3x2.Identity);
        _vg.Scissor((float)clip.X, (float)clip.Y, (float)clip.Width, (float)clip.Height);
        _vg.SetTransformMatrix(_transform);

        ReplayNvgPathCommands(path, path.FillRule);
        _vg.Clip();
    }


    protected override void TranslateCore(double dx, double dy)
    {
        _transform = Matrix3x2.CreateTranslation((float)dx, (float)dy) * _transform;
        _vg.SetTransformMatrix(_transform);
    }

    protected override void RotateCore(double angleRadians)
    {
        _transform = Matrix3x2.CreateRotation((float)angleRadians) * _transform;
        _vg.SetTransformMatrix(_transform);
    }

    protected override void ScaleCore(double sx, double sy)
    {
        _transform = Matrix3x2.CreateScale((float)sx, (float)sy) * _transform;
        _vg.SetTransformMatrix(_transform);
    }

    protected override void SetTransformCore(Matrix3x2 matrix)
    {
        _transform = matrix;
        _vg.SetTransformMatrix(_transform);
    }

    protected override Matrix3x2 GetTransformCore() => _transform;

    protected override void ResetTransformCore()
    {
        _transform = Matrix3x2.Identity;
        _vg.SetTransformMatrix(_transform);
    }

    public override float GlobalAlpha
    {
        get => _globalAlpha;
        set { _globalAlpha = value; _vg.GlobalAlpha(value); }
    }

    public override bool TextPixelSnap
    {
        get => _textPixelSnap;
        set => _textPixelSnap = value;
    }

    protected override void ResetClipCore()
    {
        _clipBoundsWorld = null;
        _vg.ResetScissor();
    }

    #endregion

    #region Drawing Primitives

    public override void Clear(Color color)
    {
        _vg.Save();
        _vg.ResetTransform();
        _vg.ResetScissor();
        _vg.ShapeAntiAlias(false);
        _vg.BeginPath();

        //_vg.Rect(-1, -1, (float)_viewportWidthDip + 2, (float)_viewportHeightDip + 2);
        _vg.Rect(0, 0, (float)_viewportWidthDip, (float)_viewportHeightDip);

        _vg.FillColor(ToNvgColor(color));
        _vg.Fill();
        _vg.Restore();
    }

    protected override void DrawLineCore(Point start, Point end, Color color, double thickness = 1)
    {
        _vg.BeginPath();
        _vg.MoveTo((float)start.X, (float)start.Y);
        _vg.LineTo((float)end.X, (float)end.Y);
        _vg.StrokeColor(ToNvgColor(color));
        NvgStrokeWidth((float)thickness);
        _vg.Stroke();
    }

    protected override void DrawRectangleCore(Rect rect, Color color, double thickness, bool strokeInset)
    {
        if (strokeInset)
            rect = rect.Deflate(new Thickness(QuantizeHalfStroke(thickness, DpiScale)));
        _vg.ShapeAntiAlias(false);
        _vg.BeginPath();
        _vg.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        _vg.StrokeColor(ToNvgColor(color));
        NvgStrokeWidth((float)thickness);
        _vg.Stroke();
        _vg.ShapeAntiAlias(true);
    }

    protected override void FillRectangleCore(Rect rect, Color color)
    {
        _vg.ShapeAntiAlias(false);
        _vg.BeginPath();
        _vg.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        _vg.FillColor(ToNvgColor(color));
        _vg.Fill();
        _vg.ShapeAntiAlias(true);
    }

    protected override void DrawRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        float radius = (float)Math.Max(0, Math.Min(radiusX, radiusY));
        _vg.BeginPath();
        _vg.RoundedRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, radius);
        _vg.StrokeColor(ToNvgColor(color));
        NvgStrokeWidth((float)thickness);
        _vg.Stroke();
    }

    protected override void FillRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color)
    {
        float radius = (float)Math.Max(0, Math.Min(radiusX, radiusY));
        _vg.BeginPath();
        _vg.RoundedRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, radius);
        _vg.FillColor(ToNvgColor(color));
        _vg.Fill();
    }

    protected override void DrawEllipseCore(Rect bounds, Color color, double thickness = 1)
    {
        float cx = (float)(bounds.X + bounds.Width * 0.5);
        float cy = (float)(bounds.Y + bounds.Height * 0.5);
        float rx = (float)(bounds.Width * 0.5);
        float ry = (float)(bounds.Height * 0.5);

        _vg.BeginPath();
        _vg.Ellipse(cx, cy, rx, ry);
        _vg.StrokeColor(ToNvgColor(color));
        NvgStrokeWidth((float)thickness);
        _vg.Stroke();
    }

    protected override void FillEllipseCore(Rect bounds, Color color)
    {
        float cx = (float)(bounds.X + bounds.Width * 0.5);
        float cy = (float)(bounds.Y + bounds.Height * 0.5);
        float rx = (float)(bounds.Width * 0.5);
        float ry = (float)(bounds.Height * 0.5);

        _vg.BeginPath();
        _vg.Ellipse(cx, cy, rx, ry);
        _vg.FillColor(ToNvgColor(color));
        _vg.Fill();
    }

    public override void DrawPath(PathGeometry path, Color color, double thickness = 1)
    {
        RecordDrawPath();
        if (path == null || color.A == 0 || thickness <= 0)
        {
            return;
        }

        ReplayNvgPathCommands(path);
        _vg.StrokeColor(ToNvgColor(color));
        NvgStrokeWidth((float)thickness);
        _vg.Stroke();
    }

    public override void FillPath(PathGeometry path, Color color)
        => FillPath(path, color, path?.FillRule ?? FillRule.NonZero);

    public override void FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        RecordFillPath();
        if (path == null || color.A == 0)
        {
            return;
        }

        if (path.IsFrozen)
        {
            var windingRule = fillRule == FillRule.EvenOdd
                ? TessWindingRule.Odd : TessWindingRule.NonZero;

            var entry = _fillCache.GetOrCreateValue(path);
            var cached = fillRule == FillRule.EvenOdd ? entry.EvenOdd : entry.NonZero;

            if (cached == null || cached.IsStale(_vg.TessTol))
            {
                // First use or DPI changed: build object-space cache (identity transform)
                ReplayNvgPathCommands(path, fillRule, identityTransform: true);
                cached = _vg.BuildFillCache(windingRule);

                // Store back into entry
                if (fillRule == FillRule.EvenOdd)
                    entry.EvenOdd = cached;
                else
                    entry.NonZero = cached;

                _fillCache.AddOrUpdate(path, entry);
            }

            // Every frame: render from cache with current transform
            _vg.FillColor(ToNvgColor(color));
            _vg.FillFromCache(cached, windingRule);
            return;
        }

        ReplayNvgPathCommands(path, fillRule);
        _vg.FillColor(ToNvgColor(color));
        _vg.Fill();
    }

    public override void DrawLine(Point start, Point end, IPen pen)
    {
        if (pen.Thickness <= 0) return;
        var bounds = new Rect(
            Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));

        if (pen.StrokeStyle.IsDashed)
        {
            NvgStrokeHelper.DrawDashedLine(_vg, (float)start.X, (float)start.Y, (float)end.X, (float)end.Y, pen, bounds);
            return;
        }

        _vg.BeginPath();
        _vg.MoveTo((float)start.X, (float)start.Y);
        _vg.LineTo((float)end.X, (float)end.Y);
        NvgStrokeHelper.ApplyPenStyle(_vg, pen);
        NvgStrokeWidth((float)pen.Thickness);
        NvgStrokeHelper.ApplyStrokeBrush(_vg, pen, bounds);
        _vg.Stroke();
    }

    public override void DrawRectangle(Rect rect, IPen pen)
    {
        if (pen.Thickness <= 0) return;

        if (pen.StrokeStyle.IsDashed)
        {
            NvgStrokeHelper.DrawDashedRect(_vg, (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, pen, rect);
            return;
        }

        _vg.ShapeAntiAlias(false);
        _vg.BeginPath();
        _vg.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        NvgStrokeHelper.ApplyPenStyle(_vg, pen);
        NvgStrokeWidth((float)pen.Thickness);
        NvgStrokeHelper.ApplyStrokeBrush(_vg, pen, rect);
        _vg.Stroke();
        _vg.ShapeAntiAlias(true);
    }

    public override void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen)
    {
        if (pen.Thickness <= 0) return;
        float radius = (float)Math.Max(0, Math.Min(radiusX, radiusY));

        if (pen.StrokeStyle.IsDashed)
        {
            NvgStrokeHelper.DrawDashedRoundedRect(_vg, (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, radius, pen, rect);
            return;
        }

        _vg.BeginPath();
        _vg.RoundedRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, radius);
        NvgStrokeHelper.ApplyPenStyle(_vg, pen);
        NvgStrokeWidth((float)pen.Thickness);
        NvgStrokeHelper.ApplyStrokeBrush(_vg, pen, rect);
        _vg.Stroke();
    }

    public override void DrawEllipse(Rect bounds, IPen pen)
    {
        if (pen.Thickness <= 0) return;
        float cx = (float)(bounds.X + bounds.Width * 0.5);
        float cy = (float)(bounds.Y + bounds.Height * 0.5);
        float rx = (float)(bounds.Width * 0.5);
        float ry = (float)(bounds.Height * 0.5);

        if (pen.StrokeStyle.IsDashed)
        {
            NvgStrokeHelper.DrawDashedEllipse(_vg, cx, cy, rx, ry, pen, bounds);
            return;
        }

        _vg.BeginPath();
        _vg.Ellipse(cx, cy, rx, ry);
        NvgStrokeHelper.ApplyPenStyle(_vg, pen);
        NvgStrokeWidth((float)pen.Thickness);
        NvgStrokeHelper.ApplyStrokeBrush(_vg, pen, bounds);
        _vg.Stroke();
    }

    public override void DrawPath(PathGeometry path, IPen pen)
    {
        RecordDrawPath();
        if (path == null || pen.Thickness <= 0) return;

        if (pen.StrokeStyle.IsDashed)
        {
            NvgStrokeHelper.DrawDashedStroke(_vg, path, pen, NvgStrokeHelper.ComputePathBounds(path));
            return;
        }

        ReplayNvgPathCommands(path);
        NvgStrokeHelper.ApplyPenStyle(_vg, pen);
        NvgStrokeWidth((float)pen.Thickness);
        NvgStrokeHelper.ApplyStrokeBrush(_vg, pen, NvgStrokeHelper.ComputePathBounds(path));
        _vg.Stroke();
    }

    public override void FillRectangle(Rect rect, IBrush brush)
    {
        if (brush is ISolidColorBrush solid) { FillRectangle(rect, solid.Color); return; }
        if (brush is IImageBrush imageBrush)
        {
            _vg.ShapeAntiAlias(false);
            _vg.BeginPath();
            _vg.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
            if (ApplyImageBrushPaint(imageBrush)) _vg.Fill();
            _vg.ShapeAntiAlias(true);
            return;
        }
        if (brush is not IGradientBrush gradient) return;

        _vg.ShapeAntiAlias(false);
        _vg.BeginPath();
        _vg.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        NvgStrokeHelper.ApplyGradientPaint(_vg, gradient, rect);
        _vg.Fill();
        _vg.ShapeAntiAlias(true);
    }

    public override void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush)
    {
        if (brush is ISolidColorBrush solid) { FillRoundedRectangle(rect, radiusX, radiusY, solid.Color); return; }
        if (brush is IImageBrush imageBrush)
        {
            float radius = (float)Math.Max(0, Math.Min(radiusX, radiusY));
            _vg.BeginPath();
            _vg.RoundedRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, radius);
            if (ApplyImageBrushPaint(imageBrush)) _vg.Fill();
            return;
        }
        if (brush is not IGradientBrush gradient) return;

        float r = (float)Math.Max(0, Math.Min(radiusX, radiusY));
        _vg.BeginPath();
        _vg.RoundedRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, r);
        NvgStrokeHelper.ApplyGradientPaint(_vg, gradient, rect);
        _vg.Fill();
    }

    public override void FillEllipse(Rect bounds, IBrush brush)
    {
        if (brush is ISolidColorBrush solid) { FillEllipse(bounds, solid.Color); return; }
        if (brush is IImageBrush imageBrush)
        {
            float cx = (float)(bounds.X + bounds.Width * 0.5);
            float cy = (float)(bounds.Y + bounds.Height * 0.5);
            _vg.BeginPath();
            _vg.Ellipse(cx, cy, (float)(bounds.Width * 0.5), (float)(bounds.Height * 0.5));
            if (ApplyImageBrushPaint(imageBrush)) _vg.Fill();
            return;
        }
        if (brush is not IGradientBrush gradient) return;

        float ecx = (float)(bounds.X + bounds.Width * 0.5);
        float ecy = (float)(bounds.Y + bounds.Height * 0.5);
        _vg.BeginPath();
        _vg.Ellipse(ecx, ecy, (float)(bounds.Width * 0.5), (float)(bounds.Height * 0.5));
        NvgStrokeHelper.ApplyGradientPaint(_vg, gradient, bounds);
        _vg.Fill();
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
        if (brush is ISolidColorBrush solid) { FillPath(path, solid.Color, fillRule); return; }
        if (brush is IImageBrush imageBrush)
        {
            ReplayNvgPathCommands(path, fillRule);
            if (ApplyImageBrushPaint(imageBrush)) _vg.Fill();
            return;
        }
        if (brush is not IGradientBrush gradient) return;

        ReplayNvgPathCommands(path, fillRule);
        NvgStrokeHelper.ApplyGradientPaint(_vg, gradient, NvgStrokeHelper.ComputePathBounds(path));
        _vg.Fill();
    }

    /// <summary>
    /// Applies an <see cref="IImageBrush"/> as the current NanoVG fill paint. Returns false
    /// when the brush cannot be rendered by this backend (e.g. image is not a <see cref="MewVGImage"/>).
    /// The tile is realized via <see cref="NanoVG.ImagePattern"/>, which takes a pre-flag'd
    /// NVG texture (RepeatX/RepeatY set at texture creation) and a paint transform that
    /// positions one tile and sets its size; NanoVG then wraps UVs via GL_REPEAT.
    /// </summary>
    private bool ApplyImageBrushPaint(IImageBrush imageBrush)
    {
        if (imageBrush.Image is not MewVGImage mewImage)
        {
            return false;
        }

        var flags = GetImageFlags() | NVGimageFlags.Premultiplied;
        if (imageBrush.TileMode is TileMode.Tile or TileMode.TileX)
        {
            flags |= NVGimageFlags.RepeatX;
        }
        if (imageBrush.TileMode is TileMode.Tile or TileMode.TileY)
        {
            flags |= NVGimageFlags.RepeatY;
        }

        int imageId = mewImage.GetOrCreateImageId(_vg, flags);
        if (imageId == 0)
        {
            return false;
        }

        var dst = imageBrush.DestinationRect;
        float patternX = (float)dst.X;
        float patternY = (float)dst.Y;
        float patternW = (float)dst.Width;
        float patternH = (float)dst.Height;
        float angle = 0f;

        if (imageBrush.Transform is { } t)
        {
            // Decompose into translate + rotation + per-axis scale (ignores shear).
            // NanoVG's ImagePattern supports all three natively: translate via (cx,cy),
            // rotate via angle, scale by folding into (w,h).
            float scaleX = MathF.Sqrt(t.M11 * t.M11 + t.M12 * t.M12);
            float scaleY = MathF.Sqrt(t.M21 * t.M21 + t.M22 * t.M22);
            angle = MathF.Atan2(t.M12, t.M11);
            patternW *= scaleX;
            patternH *= scaleY;
            patternX += t.M31;
            patternY += t.M32;
        }

        float opacity = (float)imageBrush.Opacity;
        var paint = _vg.ImagePattern(patternX, patternY, patternW, patternH, angle, imageId, opacity);
        _vg.FillPaint(paint);
        return true;
    }

    public override void DrawBoxShadow(Rect bounds, double cornerRadius, double blurRadius,
        Color shadowColor, double offsetX = 0, double offsetY = 0)
    {
        if (blurRadius <= 0 || shadowColor.A == 0) return;

        float x = (float)(bounds.X + offsetX);
        float y = (float)(bounds.Y + offsetY);
        float w = (float)bounds.Width;
        float h = (float)bounds.Height;
        float cr = (float)Math.Min(Math.Max(cornerRadius, 0), Math.Min(w, h) * 0.5);
        float br = (float)blurRadius;

        var inner = ToNvgColor(shadowColor);
        var outer = ToNvgColor(Color.FromArgb(0, shadowColor.R, shadowColor.G, shadowColor.B));

        var paint = _vg.BoxGradient(x, y, w, h, cr, br, inner, outer);

        _vg.ShapeAntiAlias(false);
        _vg.BeginPath();
        _vg.Rect(x - br, y - br, w + br * 2, h + br * 2);
        _vg.FillPaint(paint);
        _vg.Fill();
        _vg.ShapeAntiAlias(true);
    }

    private void ReplayNvgPathCommands(PathGeometry path, FillRule fillRule = FillRule.NonZero,
        bool identityTransform = false)
    {
        if (identityTransform)
        {
            _vg.Save();
            _vg.ResetTransform();
        }

        _vg.BeginPath();
        _vg.FillRule(fillRule == FillRule.EvenOdd ? NVGfillRule.EvenOdd : NVGfillRule.NonZero);

        foreach (var cmd in path.Commands)
        {
            switch (cmd.Type)
            {
                case PathCommandType.MoveTo:
                    _vg.MoveTo((float)cmd.X0, (float)cmd.Y0);
                    break;
                case PathCommandType.LineTo:
                    _vg.LineTo((float)cmd.X0, (float)cmd.Y0);
                    break;
                case PathCommandType.BezierTo:
                    _vg.BezierTo((float)cmd.X0, (float)cmd.Y0,
                                 (float)cmd.X1, (float)cmd.Y1,
                                 (float)cmd.X2, (float)cmd.Y2);
                    break;
                case PathCommandType.Close:
                    _vg.ClosePath();
                    break;
            }
        }

        if (identityTransform)
        {
            _vg.Restore();
        }
    }
    #endregion

    #region Image Helpers

    private void DrawImagePattern(int imageId, Rect destRect, float alpha, Rect? sourceRect, int imageWidthPx, int imageHeightPx)
    {
        if (destRect.Width <= 0 || destRect.Height <= 0)
        {
            return;
        }

        NVGpaint paint;

        if (sourceRect is null)
        {
            paint = _vg.ImagePattern((float)destRect.X, (float)destRect.Y, (float)destRect.Width, (float)destRect.Height, 0f, imageId, alpha);
        }
        else
        {
            var src = sourceRect.Value;
            if (src.Width <= 0 || src.Height <= 0)
            {
                return;
            }

            double srcWidthDip = src.Width / DpiScale;
            double srcHeightDip = src.Height / DpiScale;
            if (srcWidthDip <= 0 || srcHeightDip <= 0)
            {
                return;
            }

            float scaleX = (float)(destRect.Width / srcWidthDip);
            float scaleY = (float)(destRect.Height / srcHeightDip);
            float imageWidthDip = (float)(imageWidthPx / DpiScale);
            float imageHeightDip = (float)(imageHeightPx / DpiScale);
            float patternW = imageWidthDip * scaleX;
            float patternH = imageHeightDip * scaleY;
            float patternX = (float)destRect.X - (float)(src.X / DpiScale * scaleX);
            float patternY = (float)destRect.Y - (float)(src.Y / DpiScale * scaleY);
            paint = _vg.ImagePattern(patternX, patternY, patternW, patternH, 0f, imageId, alpha);
        }

        // Outline AA only matters when the rect is rasterised non-axis-aligned
        // (rotation/skew). Axis-aligned image fills land on integer pixel boundaries —
        // the texture's own sampling handles edges and shape AA would add a needless
        // half-pixel feather. D2D applies the same coverage AA implicitly on its
        // bitmap draw; this matches that behaviour.
        bool isAxisAligned = _transform.M12 == 0f && _transform.M21 == 0f;
        if (isAxisAligned) _vg.ShapeAntiAlias(false);
        _vg.BeginPath();
        _vg.Rect((float)destRect.X, (float)destRect.Y, (float)destRect.Width, (float)destRect.Height);
        _vg.FillPaint(paint);
        _vg.Fill();
        if (isAxisAligned) _vg.ShapeAntiAlias(true);
    }

    private NVGimageFlags GetImageFlags()
    {
        return ImageScaleQuality switch
        {
            ImageScaleQuality.Fast => NVGimageFlags.Nearest,
            ImageScaleQuality.HighQuality => NVGimageFlags.GenerateMipmaps,
            _ => NVGimageFlags.None,
        };
    }

    #endregion

    #region Utilities

    private Rect TransformRectToWorldAABB(Rect rect)
    {
        // Fast path: translation-only transform.
        if (_transform.M11 == 1f && _transform.M12 == 0f &&
            _transform.M21 == 0f && _transform.M22 == 1f)
        {
            return new Rect(rect.X + _transform.M31, rect.Y + _transform.M32,
                rect.Width, rect.Height);
        }

        // General case: transform all 4 corners and compute AABB.
        var tl = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Y), _transform);
        var tr = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Y), _transform);
        var bl = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Bottom), _transform);
        var br = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Bottom), _transform);

        float minX = MathF.Min(MathF.Min(tl.X, tr.X), MathF.Min(bl.X, br.X));
        float minY = MathF.Min(MathF.Min(tl.Y, tr.Y), MathF.Min(bl.Y, br.Y));
        float maxX = MathF.Max(MathF.Max(tl.X, tr.X), MathF.Max(bl.X, br.X));
        float maxY = MathF.Max(MathF.Max(tl.Y, tr.Y), MathF.Max(bl.Y, br.Y));

        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    private static Rect IntersectClipBounds(Rect? current, Rect next)
    {
        if (!current.HasValue)
        {
            return next;
        }

        double left = Math.Max(current.Value.X, next.X);
        double top = Math.Max(current.Value.Y, next.Y);
        double right = Math.Min(current.Value.Right, next.Right);
        double bottom = Math.Min(current.Value.Bottom, next.Bottom);
        return right > left && bottom > top
            ? new Rect(left, top, right - left, bottom - top)
            : new Rect(left, top, 0, 0);
    }

    private static NVGcolor ToNvgColor(Color color) => NVGcolor.RGBA(color.R, color.G, color.B, color.A);


    /// <summary>
    /// Sets NanoVG stroke width with device-pixel snapping. The thickness is in DIP
    /// (input units); NanoVG applies the current transform (user × dpi) on its side,
    /// so we round to whole device pixels at total scale and convert back to DIP for
    /// NVG to multiply through. Result: stroke scales with transform like Skia/WPF/SVG
    /// while remaining crisp on fractional DPI.
    /// </summary>
    private void NvgStrokeWidth(float thickness)
    {
        if (thickness <= 0) { _vg.StrokeWidth(0); return; }
        float sx = MathF.Sqrt(_transform.M11 * _transform.M11 + _transform.M12 * _transform.M12);
        float sy = MathF.Sqrt(_transform.M21 * _transform.M21 + _transform.M22 * _transform.M22);
        float avgScale = (sx + sy) * 0.5f;
        float totalScale = avgScale * (float)DpiScale;
        if (totalScale < 0.001f) { _vg.StrokeWidth(thickness); return; }
        float snappedPx = MathF.Max(1, MathF.Round(thickness * totalScale));
        _vg.StrokeWidth(snappedPx / totalScale);
    }

    #endregion
}
