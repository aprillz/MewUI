using System.Numerics;
using System.Runtime.CompilerServices;

using Aprillz.MewVG;
using Aprillz.MewVG.Tess;

namespace Aprillz.MewUI.Rendering.MewVG;

#if MEWUI_MEWVG_MACOS
internal sealed partial class MewVGMetalGraphicsContext : GraphicsContextBase
#elif MEWUI_MEWVG_X11
internal sealed partial class MewVGX11GraphicsContext : GraphicsContextBase
#else
internal sealed partial class MewVGGraphicsContext : GraphicsContextBase
#endif
{
#if MEWUI_MEWVG_MACOS
    private NanoVGMetal _vg;
#else
    private NanoVGGL _vg;
#endif

    private readonly ClipStack _clip = new();
    private readonly Stack<(double tx, double ty, Rect? clipBoundsWorld, float globalAlpha, Matrix3x2 transform, bool textPixelSnap)> _saveStack = new();
    private double _translateX;
    private double _translateY;
    private float _globalAlpha = 1f;
    private bool _textPixelSnap = true;
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private Rect? _clipBoundsWorld;
    private double _viewportWidthDip;
    private double _viewportHeightDip;
    private int _viewportWidthPx;
    private int _viewportHeightPx;
    private bool _disposed;

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

    #region State Management

    protected override void SaveCore()
    {
        _vg.Save();
        _clip.Save();
        _saveStack.Push((_translateX, _translateY, _clipBoundsWorld, _globalAlpha, _transform, _textPixelSnap));
    }

    protected override void RestoreCore()
    {
        _vg.Restore();
        _clip.Restore();
        if (_saveStack.Count > 0)
        {
            var state = _saveStack.Pop();
            _translateX = state.tx;
            _translateY = state.ty;
            _clipBoundsWorld = state.clipBoundsWorld;
            _globalAlpha = state.globalAlpha;
            _textPixelSnap = state.textPixelSnap;
            _transform = state.transform;
            _vg.GlobalAlpha(_globalAlpha);
        }
    }

    protected override void SetClipCore(Rect rect)
    {
        var worldClip = new Rect(rect.X + _translateX, rect.Y + _translateY, rect.Width, rect.Height);
        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, worldClip);
        _clip.Apply(
            rect,
            r => _vg.Scissor((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height),
            r => _vg.IntersectScissor((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height),
            () => _vg.ResetScissor());
    }

    protected override void SetClipRoundedRectCore(Rect rect, double radiusX, double radiusY)
    {
        if (radiusX <= 0 && radiusY <= 0)
        {
            SetClip(rect);
            return;
        }

        var worldClip = new Rect(rect.X + _translateX, rect.Y + _translateY, rect.Width, rect.Height);
        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, worldClip);

        _clip.Apply(
            rect,
            r => _vg.Scissor((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height),
            r => _vg.IntersectScissor((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height),
            () => _vg.ResetScissor());

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


    protected override void TranslateCore(double dx, double dy)
    {
        _translateX += dx;
        _translateY += dy;
        _transform = Matrix3x2.CreateTranslation((float)dx, (float)dy) * _transform;
        _vg.Translate((float)dx, (float)dy);
    }

    protected override void RotateCore(double angleRadians)
    {
        _transform = Matrix3x2.CreateRotation((float)angleRadians) * _transform;
        _vg.Rotate((float)angleRadians);
    }

    protected override void ScaleCore(double sx, double sy)
    {
        _transform = Matrix3x2.CreateScale((float)sx, (float)sy) * _transform;
        _vg.Scale((float)sx, (float)sy);
    }

    protected override void SetTransformCore(Matrix3x2 matrix)
    {
        // NanoVG doesn't expose a direct set-matrix API.
        // Reset and then decompose the matrix into Translate + Rotate + Scale.
        _vg.ResetTransform();
        _transform = matrix;
        _translateX = matrix.M31;
        _translateY = matrix.M32;

        // Decompose the 2x2 linear part into rotation + scale.
        float sx = MathF.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
        float sy = MathF.Sqrt(matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22);
        // Detect reflection (negative determinant).
        if (matrix.M11 * matrix.M22 - matrix.M12 * matrix.M21 < 0)
            sy = -sy;
        float angle = MathF.Atan2(matrix.M12, matrix.M11);

        // Apply in reverse order (NanoVG premultiplies: last call is innermost).
        // We want: T * R * S, so call Scale first, then Rotate, then Translate.
        if (sx != 1f || sy != 1f)
            _vg.Scale(sx, sy);
        if (angle != 0f)
            _vg.Rotate(angle);
        if (matrix.M31 != 0f || matrix.M32 != 0f)
            _vg.Translate(matrix.M31, matrix.M32);
    }

    protected override Matrix3x2 GetTransformCore() => _transform;

    protected override void ResetTransformCore()
    {
        _translateX = 0;
        _translateY = 0;
        _transform = Matrix3x2.Identity;
        _vg.ResetTransform();
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
        _clip.Reset();
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

    protected override void DrawRectangleCore(Rect rect, Color color, double thickness = 1)
    {
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
        if (brush is not IGradientBrush gradient) return;

        float radius = (float)Math.Max(0, Math.Min(radiusX, radiusY));
        _vg.BeginPath();
        _vg.RoundedRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, radius);
        NvgStrokeHelper.ApplyGradientPaint(_vg, gradient, rect);
        _vg.Fill();
    }

    public override void FillEllipse(Rect bounds, IBrush brush)
    {
        if (brush is ISolidColorBrush solid) { FillEllipse(bounds, solid.Color); return; }
        if (brush is not IGradientBrush gradient) return;

        float cx = (float)(bounds.X + bounds.Width * 0.5);
        float cy = (float)(bounds.Y + bounds.Height * 0.5);
        _vg.BeginPath();
        _vg.Ellipse(cx, cy, (float)(bounds.Width * 0.5), (float)(bounds.Height * 0.5));
        NvgStrokeHelper.ApplyGradientPaint(_vg, gradient, bounds);
        _vg.Fill();
    }

    public override void FillPath(PathGeometry path, IBrush brush)
        => FillPath(path, brush, path?.FillRule ?? FillRule.NonZero);

    public override void FillPath(PathGeometry path, IBrush brush, FillRule fillRule)
    {
        if (path == null) return;
        if (brush is ISolidColorBrush solid) { FillPath(path, solid.Color, fillRule); return; }
        if (brush is not IGradientBrush gradient) return;

        ReplayNvgPathCommands(path, fillRule);
        NvgStrokeHelper.ApplyGradientPaint(_vg, gradient, NvgStrokeHelper.ComputePathBounds(path));
        _vg.Fill();
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

        _vg.ShapeAntiAlias(false);
        _vg.BeginPath();
        _vg.Rect((float)destRect.X, (float)destRect.Y, (float)destRect.Width, (float)destRect.Height);
        _vg.FillPaint(paint);
        _vg.Fill();
        _vg.ShapeAntiAlias(true);
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
    /// Sets NanoVG stroke width compensated for the current transform scale.
    /// NanoVG's Stroke() internally multiplies by GetAverageScale, but MewUI's
    /// convention (matching GDI/D2D/WPF) is that stroke width is transform-independent.
    /// </summary>
    private void NvgStrokeWidth(float thickness)
    {
        thickness = QuantizeStrokeDip(thickness);
        float sx = MathF.Sqrt(_transform.M11 * _transform.M11 + _transform.M12 * _transform.M12);
        float sy = MathF.Sqrt(_transform.M21 * _transform.M21 + _transform.M22 * _transform.M22);
        float avgScale = (sx + sy) * 0.5f;
        _vg.StrokeWidth(avgScale > 0.001f ? thickness / avgScale : thickness);
    }

    private float QuantizeStrokeDip(float thickness)
    {
        if (thickness <= 0) return 0;
        float snappedPx = MathF.Max(1, MathF.Round(thickness * (float)DpiScale));
        return snappedPx / (float)DpiScale;
    }

    #endregion
}
