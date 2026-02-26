using System.Numerics;

using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

#if MEWUI_MEWVG_MACOS
internal sealed partial class MewVGMetalGraphicsContext : IGraphicsContext
#elif MEWUI_MEWVG_X11
internal sealed partial class MewVGX11GraphicsContext : IGraphicsContext
#else
internal sealed partial class MewVGGraphicsContext : IGraphicsContext
#endif
{
#if MEWUI_MEWVG_MACOS
    private NanoVGMetal _vg;
#else
    private NanoVGGL _vg;
#endif

    private readonly ClipStack _clip = new();
    private readonly Stack<(double tx, double ty, Rect? clipBoundsWorld, float globalAlpha, Matrix3x2 transform)> _saveStack = new();
    private double _translateX;
    private double _translateY;
    private float _globalAlpha = 1f;
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private Rect? _clipBoundsWorld;
    private double _viewportWidthDip;
    private double _viewportHeightDip;
    private int _viewportWidthPx;
    private int _viewportHeightPx;
    private bool _disposed;

    public double DpiScale { get; private set; }

    public ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    #region State Management

    public void Save()
    {
        _vg.Save();
        _clip.Save();
        _saveStack.Push((_translateX, _translateY, _clipBoundsWorld, _globalAlpha, _transform));
    }

    public void Restore()
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
            _transform = state.transform;
            _vg.GlobalAlpha(_globalAlpha);
        }
    }

    public void SetClip(Rect rect)
    {
        var worldClip = new Rect(rect.X + _translateX, rect.Y + _translateY, rect.Width, rect.Height);
        _clipBoundsWorld = IntersectClipBounds(_clipBoundsWorld, worldClip);
        _clip.Apply(
            rect,
            r => _vg.Scissor((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height),
            r => _vg.IntersectScissor((float)r.X, (float)r.Y, (float)r.Width, (float)r.Height),
            () => _vg.ResetScissor());

        //FillRectangle(rect, Color.Red.WithAlpha(32));
    }

    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY)
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

        // NanoVG's Clip() writes only the solid fill interior to the stencil buffer,
        // which is inset by 0.5 * fringeWidth due to ExpandFill's AA offset.
        // Expand the path by 1 device pixel on all edges to compensate.
        // The scissor (set above) still clips the straight edges to the exact rect.
        float radius = (float)Math.Max(0, Math.Min(radiusX, radiusY));
        float expand = 1f / (float)DpiScale;
        _vg.BeginPath();
        rect = new Rect((float)rect.X - expand,
            (float)rect.Y - expand,
            (float)rect.Width + expand * 2,
            (float)rect.Height + expand * 2);

        // Keep the visual corner radius consistent after expanding the clip rect.
        radius = MathF.Max(0f, radius + expand * 2);
        float maxR = MathF.Min((float)rect.Width, (float)rect.Height) * 0.5f;
        if (radius > maxR)
        {
            radius = maxR;
        }

        _vg.RoundedRect(
            (float)rect.X,
            (float)rect.Y,
            (float)rect.Width,
            (float)rect.Height,
            radius);
        _vg.Clip();

        //FillRectangle(rect, Color.Red.WithAlpha(64));
    }

    public void Translate(double dx, double dy)
    {
        _translateX += dx;
        _translateY += dy;
        _transform = Matrix3x2.CreateTranslation((float)dx, (float)dy) * _transform;
        _vg.Translate((float)dx, (float)dy);
    }

    public void Rotate(double angleRadians)
    {
        _transform = Matrix3x2.CreateRotation((float)angleRadians) * _transform;
        _vg.Rotate((float)angleRadians);
    }

    public void Scale(double sx, double sy)
    {
        _transform = Matrix3x2.CreateScale((float)sx, (float)sy) * _transform;
        _vg.Scale((float)sx, (float)sy);
    }

    public void SetTransform(Matrix3x2 matrix)
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

    public Matrix3x2 GetTransform() => _transform;

    public void ResetTransform()
    {
        _translateX = 0;
        _translateY = 0;
        _transform = Matrix3x2.Identity;
        _vg.ResetTransform();
    }

    public float GlobalAlpha
    {
        get => _globalAlpha;
        set { _globalAlpha = value; _vg.GlobalAlpha(value); }
    }

    public void ResetClip()
    {
        _clipBoundsWorld = null;
        _clip.Reset();
        _vg.ResetScissor();
    }

    #endregion

    #region Drawing Primitives

    public void Clear(Color color)
    {
        _vg.Save();
        _vg.ResetTransform();
        _vg.ResetScissor();
        _vg.BeginPath();
        _vg.Rect(0, 0, (float)_viewportWidthDip, (float)_viewportHeightDip);
        _vg.FillColor(ToNvgColor(color));
        _vg.Fill();
        _vg.Restore();
    }

    public void DrawLine(Point start, Point end, Color color, double thickness = 1)
    {
        _vg.BeginPath();
        _vg.MoveTo((float)start.X, (float)start.Y);
        _vg.LineTo((float)end.X, (float)end.Y);
        _vg.StrokeColor(ToNvgColor(color));
        _vg.StrokeWidth((float)thickness);
        _vg.Stroke();
    }

    public void DrawRectangle(Rect rect, Color color, double thickness = 1)
    {
        _vg.BeginPath();
        _vg.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        _vg.StrokeColor(ToNvgColor(color));
        _vg.StrokeWidth((float)thickness);
        _vg.Stroke();
    }

    public void FillRectangle(Rect rect, Color color)
    {
        _vg.BeginPath();
        _vg.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        _vg.FillColor(ToNvgColor(color));
        _vg.Fill();
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        float radius = (float)Math.Max(0, Math.Min(radiusX, radiusY));
        _vg.BeginPath();
        _vg.RoundedRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, radius);
        _vg.StrokeColor(ToNvgColor(color));
        _vg.StrokeWidth((float)thickness);
        _vg.Stroke();
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    {
        float radius = (float)Math.Max(0, Math.Min(radiusX, radiusY));
        _vg.BeginPath();
        _vg.RoundedRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, radius);
        _vg.FillColor(ToNvgColor(color));
        _vg.Fill();
    }

    public void DrawEllipse(Rect bounds, Color color, double thickness = 1)
    {
        float cx = (float)(bounds.X + bounds.Width * 0.5);
        float cy = (float)(bounds.Y + bounds.Height * 0.5);
        float rx = (float)(bounds.Width * 0.5);
        float ry = (float)(bounds.Height * 0.5);

        _vg.BeginPath();
        _vg.Ellipse(cx, cy, rx, ry);
        _vg.StrokeColor(ToNvgColor(color));
        _vg.StrokeWidth((float)thickness);
        _vg.Stroke();
    }

    public void FillEllipse(Rect bounds, Color color)
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

    public void DrawPath(PathGeometry path, Color color, double thickness = 1)
    {
        if (path == null || color.A == 0 || thickness <= 0)
        {
            return;
        }

        ReplayNvgPathCommands(path);
        _vg.StrokeColor(ToNvgColor(color));
        _vg.StrokeWidth((float)thickness);
        _vg.Stroke();
    }

    public void FillPath(PathGeometry path, Color color)
        => FillPath(path, color, path?.FillRule ?? FillRule.NonZero);

    public void FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        if (path == null || color.A == 0)
        {
            return;
        }

        ReplayNvgPathCommands(path, fillRule);
        _vg.FillColor(ToNvgColor(color));
        _vg.Fill();
    }

    public void DrawLine(Point start, Point end, IPen pen)
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
        NvgStrokeHelper.ApplyStrokeBrush(_vg, pen, bounds);
        _vg.Stroke();
    }

    public void DrawRectangle(Rect rect, IPen pen)
    {
        if (pen.Thickness <= 0) return;

        if (pen.StrokeStyle.IsDashed)
        {
            NvgStrokeHelper.DrawDashedRect(_vg, (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, pen, rect);
            return;
        }

        _vg.BeginPath();
        _vg.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        NvgStrokeHelper.ApplyPenStyle(_vg, pen);
        NvgStrokeHelper.ApplyStrokeBrush(_vg, pen, rect);
        _vg.Stroke();
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, IPen pen)
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
        NvgStrokeHelper.ApplyStrokeBrush(_vg, pen, rect);
        _vg.Stroke();
    }

    public void DrawEllipse(Rect bounds, IPen pen)
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
        NvgStrokeHelper.ApplyStrokeBrush(_vg, pen, bounds);
        _vg.Stroke();
    }

    public void DrawPath(PathGeometry path, IPen pen)
    {
        if (path == null || pen.Thickness <= 0) return;

        if (pen.StrokeStyle.IsDashed)
        {
            NvgStrokeHelper.DrawDashedStroke(_vg, path, pen, NvgStrokeHelper.ComputePathBounds(path));
            return;
        }

        ReplayNvgPathCommands(path);
        NvgStrokeHelper.ApplyPenStyle(_vg, pen);
        NvgStrokeHelper.ApplyStrokeBrush(_vg, pen, NvgStrokeHelper.ComputePathBounds(path));
        _vg.Stroke();
    }

    public void FillRectangle(Rect rect, IBrush brush)
    {
        if (brush is ISolidColorBrush solid) { FillRectangle(rect, solid.Color); return; }
        if (brush is not IGradientBrush gradient) return;

        _vg.BeginPath();
        _vg.Rect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        NvgStrokeHelper.ApplyGradientPaint(_vg, gradient, rect);
        _vg.Fill();
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush)
    {
        if (brush is ISolidColorBrush solid) { FillRoundedRectangle(rect, radiusX, radiusY, solid.Color); return; }
        if (brush is not IGradientBrush gradient) return;

        float radius = (float)Math.Max(0, Math.Min(radiusX, radiusY));
        _vg.BeginPath();
        _vg.RoundedRect((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, radius);
        NvgStrokeHelper.ApplyGradientPaint(_vg, gradient, rect);
        _vg.Fill();
    }

    public void FillEllipse(Rect bounds, IBrush brush)
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

    public void FillPath(PathGeometry path, IBrush brush)
        => FillPath(path, brush, path?.FillRule ?? FillRule.NonZero);

    public void FillPath(PathGeometry path, IBrush brush, FillRule fillRule)
    {
        if (path == null) return;
        if (brush is ISolidColorBrush solid) { FillPath(path, solid.Color, fillRule); return; }
        if (brush is not IGradientBrush gradient) return;

        ReplayNvgPathCommands(path, fillRule);
        NvgStrokeHelper.ApplyGradientPaint(_vg, gradient, NvgStrokeHelper.ComputePathBounds(path));
        _vg.Fill();
    }

    private void ReplayNvgPathCommands(PathGeometry path, FillRule fillRule = FillRule.NonZero)
    {
        _vg.BeginPath();
        bool isEvenOdd = fillRule == FillRule.EvenOdd;
        int subPathIndex = 0;

        foreach (var cmd in path.Commands)
        {
            switch (cmd.Type)
            {
                case PathCommandType.MoveTo:
                    _vg.MoveTo((float)cmd.X0, (float)cmd.Y0);
                    // For EvenOdd: mark alternating subpaths as holes (CW winding).
                    // NanoVG doesn't have a true EvenOdd fill rule, but marking every other
                    // subpath as CW achieves the expected result for concentric shapes.
                    if (isEvenOdd && subPathIndex > 0 && subPathIndex % 2 == 1)
                        _vg.PathWinding(NVGwinding.CW);
                    subPathIndex++;
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

        _vg.BeginPath();
        _vg.Rect((float)destRect.X, (float)destRect.Y, (float)destRect.Width, (float)destRect.Height);
        _vg.FillPaint(paint);
        _vg.Fill();
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

    #endregion
}
