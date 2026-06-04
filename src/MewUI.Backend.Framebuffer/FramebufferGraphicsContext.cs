using System.Numerics;
using SkiaSharp;

namespace Aprillz.MewUI.Rendering.Framebuffer;

internal sealed class FramebufferGraphicsContext : GraphicsContextBase
{
    private readonly FramebufferRenderSurface _surface;
    private readonly Stack<SKMatrix> _transformStack = new();
    private SKSurface? _skSurface;
    private SKCanvas? _canvas;
    private float _globalAlpha = 1f;
    private bool _textPixelSnap = true;

    public FramebufferGraphicsContext(FramebufferRenderSurface surface)
    {
        _surface = surface;
    }

    public override double DpiScale => _surface.DpiScale;

    public override ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    public override float GlobalAlpha
    {
        get => _globalAlpha;
        set => _globalAlpha = Math.Clamp(value, 0f, 1f);
    }

    public override bool TextPixelSnap
    {
        get => _textPixelSnap;
        set => _textPixelSnap = value;
    }

    protected override unsafe void OnBeginFrame(IRenderTarget target)
    {
        if (!ReferenceEquals(target, _surface))
        {
            throw new ArgumentException("FramebufferGraphicsContext can only render to its owning surface.", nameof(target));
        }

        var info = new SKImageInfo(_surface.PixelWidth, _surface.PixelHeight, SKColorType.Bgra8888,
            _surface.Capabilities.HasFlag(SurfaceCapabilities.Premultiplied) ? SKAlphaType.Premul : SKAlphaType.Unpremul);
        var span = _surface.GetWritablePixelSpan();
        fixed (byte* pixels = span)
        {
            _skSurface = SKSurface.Create(info, (nint)pixels, _surface.StrideBytes)
                ?? throw new InvalidOperationException("Failed to create Skia surface over framebuffer render surface.");
        }

        _canvas = _skSurface.Canvas;
        _canvas.ResetMatrix();
        _canvas.Scale((float)_surface.DpiScale);
        _transformStack.Clear();
    }

    protected override void OnEndFrame()
    {
        Canvas.Flush();
        _surface.IncrementVersion();
        _skSurface?.Dispose();
        _skSurface = null;
        _canvas = null;
    }

    protected override void OnDispose()
    {
        _skSurface?.Dispose();
        _transformStack.Clear();
    }

    public override void Clear(Color color)
        => Canvas.Clear(FramebufferText.ToSkColor(ApplyGlobalAlpha(color)));

    protected override void SaveCore()
    {
        _transformStack.Push(Canvas.TotalMatrix);
        Canvas.Save();
    }

    protected override void RestoreCore()
    {
        Canvas.Restore();
        if (_transformStack.Count > 0)
        {
            _ = _transformStack.Pop();
        }
    }

    protected override void SetClipCore(Rect rect)
        => Canvas.ClipRect(ToSkRect(rect), SKClipOperation.Intersect, antialias: true);

    protected override void SetClipRoundedRectCore(Rect rect, double radiusX, double radiusY)
    {
        using var rr = new SKRoundRect(ToSkRect(rect), (float)radiusX, (float)radiusY);
        Canvas.ClipRoundRect(rr, SKClipOperation.Intersect, antialias: true);
    }

    protected override void SetClipPathCore(PathGeometry path)
    {
        using var skPath = ToSkPath(path);
        Canvas.ClipPath(skPath, SKClipOperation.Intersect, antialias: true);
    }

    protected override void TranslateCore(double dx, double dy)
        => Canvas.Translate((float)dx, (float)dy);

    protected override void RotateCore(double angleRadians)
        => Canvas.RotateRadians((float)angleRadians);

    protected override void ScaleCore(double sx, double sy)
        => Canvas.Scale((float)sx, (float)sy);

    protected override void SetTransformCore(Matrix3x2 matrix)
    {
        var skMatrix = ToSkMatrix(matrix);
        skMatrix = SKMatrix.Concat(SKMatrix.CreateScale((float)_surface.DpiScale, (float)_surface.DpiScale), skMatrix);
        Canvas.SetMatrix(skMatrix);
    }

    protected override Matrix3x2 GetTransformCore()
    {
        var m = Canvas.TotalMatrix;
        float scale = (float)_surface.DpiScale;
        if (scale > 0)
        {
            m = SKMatrix.Concat(SKMatrix.CreateScale(1 / scale, 1 / scale), m);
        }

        return new Matrix3x2(m.ScaleX, m.SkewY, m.SkewX, m.ScaleY, m.TransX, m.TransY);
    }

    protected override void ResetTransformCore()
    {
        Canvas.ResetMatrix();
        Canvas.Scale((float)_surface.DpiScale);
    }

    protected override void ResetClipCore()
    {
        while (_transformStack.Count > 0)
        {
            Canvas.Restore();
            _ = _transformStack.Pop();
        }
    }

    protected override void DrawLineCore(Point start, Point end, Color color, double thickness = 1)
    {
        using var paint = StrokePaint(color, thickness);
        Canvas.DrawLine((float)start.X, (float)start.Y, (float)end.X, (float)end.Y, paint);
    }

    protected override void DrawRectangleCore(Rect rect, Color color, double thickness, bool strokeInset)
    {
        if (strokeInset)
        {
            double half = QuantizeHalfStroke(thickness, DpiScale);
            rect = rect.Deflate(new Thickness(half));
        }

        using var paint = StrokePaint(color, thickness);
        Canvas.DrawRect(ToSkRect(rect), paint);
    }

    protected override void FillRectangleCore(Rect rect, Color color)
    {
        using var paint = FillPaint(color);
        Canvas.DrawRect(ToSkRect(rect), paint);
    }

    protected override void DrawRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        using var paint = StrokePaint(color, thickness);
        using var rr = new SKRoundRect(ToSkRect(rect), (float)radiusX, (float)radiusY);
        Canvas.DrawRoundRect(rr, paint);
    }

    protected override void FillRoundedRectangleCore(Rect rect, double radiusX, double radiusY, Color color)
    {
        using var paint = FillPaint(color);
        using var rr = new SKRoundRect(ToSkRect(rect), (float)radiusX, (float)radiusY);
        Canvas.DrawRoundRect(rr, paint);
    }

    protected override void DrawEllipseCore(Rect bounds, Color color, double thickness = 1)
    {
        using var paint = StrokePaint(color, thickness);
        Canvas.DrawOval(ToSkRect(bounds), paint);
    }

    protected override void FillEllipseCore(Rect bounds, Color color)
    {
        using var paint = FillPaint(color);
        Canvas.DrawOval(ToSkRect(bounds), paint);
    }

    public override void DrawPath(PathGeometry path, Color color, double thickness = 1)
    {
        RecordDrawPath();
        using var skPath = ToSkPath(path);
        using var paint = StrokePaint(color, thickness);
        Canvas.DrawPath(skPath, paint);
    }

    public override void FillPath(PathGeometry path, Color color)
        => FillPath(path, color, path.FillRule);

    public override void FillPath(PathGeometry path, Color color, FillRule fillRule)
    {
        RecordFillPath();
        using var skPath = ToSkPath(path);
        skPath.FillType = fillRule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        using var paint = FillPaint(color);
        Canvas.DrawPath(skPath, paint);
    }

    public override void FillRectangle(Rect rect, IBrush brush)
    {
        if (brush is ILinearGradientBrush linear)
        {
            using var paint = FillPaint(linear, rect);
            Canvas.DrawRect(ToSkRect(rect), paint);
            return;
        }

        if (brush is IRadialGradientBrush radial)
        {
            using var paint = FillPaint(radial, rect);
            Canvas.DrawRect(ToSkRect(rect), paint);
            return;
        }

        base.FillRectangle(rect, brush);
    }

    public override void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, IBrush brush)
    {
        if (brush is ILinearGradientBrush linear)
        {
            using var paint = FillPaint(linear, rect);
            using var rr = new SKRoundRect(ToSkRect(rect), (float)radiusX, (float)radiusY);
            Canvas.DrawRoundRect(rr, paint);
            return;
        }

        if (brush is IRadialGradientBrush radial)
        {
            using var paint = FillPaint(radial, rect);
            using var rr = new SKRoundRect(ToSkRect(rect), (float)radiusX, (float)radiusY);
            Canvas.DrawRoundRect(rr, paint);
            return;
        }

        base.FillRoundedRectangle(rect, radiusX, radiusY, brush);
    }

    public override TextLayout? CreateTextLayout(ReadOnlySpan<char> text, TextFormat format, in TextLayoutConstraints constraints)
        => FramebufferText.CreateLayout(text, format, constraints);

    public override void DrawTextLayout(ReadOnlySpan<char> text, TextFormat format, TextLayout layout, Color color)
        => FramebufferText.DrawText(Canvas, text, layout.EffectiveBounds, format, layout, ApplyGlobalAlpha(color));

    protected override void DrawTextCore(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None)
    {
        var format = new TextFormat
        {
            Font = font,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = verticalAlignment,
            Wrapping = wrapping,
            Trimming = trimming,
        };
        var layout = FramebufferText.CreateLayout(text, format, new TextLayoutConstraints(bounds));
        FramebufferText.DrawText(Canvas, text, bounds, format, layout, ApplyGlobalAlpha(color));
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        => FramebufferText.MeasureText(text, font);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => FramebufferText.MeasureText(text, font, maxWidth);

    public override void DrawImage(IImage image, Point location)
    {
        if (image is not FramebufferImage framebufferImage)
        {
            return;
        }

        using var paint = ImagePaint();
        Canvas.DrawImage(framebufferImage.Image, (float)location.X, (float)location.Y, ImageSampling(), paint);
    }

    protected override void DrawImageCore(IImage image, Rect destRect)
    {
        if (image is not FramebufferImage framebufferImage)
        {
            return;
        }

        using var paint = ImagePaint();
        Canvas.DrawImage(framebufferImage.Image, ToSkRect(destRect), ImageSampling(), paint);
    }

    protected override void DrawImageCore(IImage image, Rect destRect, Rect sourceRect)
    {
        if (image is not FramebufferImage framebufferImage)
        {
            return;
        }

        using var paint = ImagePaint();
        Canvas.DrawImage(framebufferImage.Image, ToSkRect(sourceRect), ToSkRect(destRect), ImageSampling(), paint);
    }

    private SKCanvas Canvas => _canvas ?? throw new InvalidOperationException("BeginFrame must be called before drawing.");

    private SKPaint FillPaint(Color color)
        => new()
        {
            Color = FramebufferText.ToSkColor(ApplyGlobalAlpha(color)),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

    private SKPaint StrokePaint(Color color, double thickness)
        => new()
        {
            Color = FramebufferText.ToSkColor(ApplyGlobalAlpha(color)),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Max(0, thickness),
            IsAntialias = true,
        };

    private SKPaint ImagePaint()
        => new()
        {
            IsAntialias = ImageScaleQuality != ImageScaleQuality.Fast,
            Color = GlobalAlpha >= 0.999f ? SKColors.White : new SKColor(255, 255, 255, (byte)Math.Round(GlobalAlpha * 255)),
        };

    private SKSamplingOptions ImageSampling()
        => ImageScaleQuality switch
        {
            ImageScaleQuality.Fast => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
            ImageScaleQuality.HighQuality => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            _ => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
        };

    private SKPaint FillPaint(ILinearGradientBrush brush, Rect bounds)
    {
        var colors = brush.Stops.Select(s => ApplyGlobalAlpha(s.Color)).Select(FramebufferText.ToSkColor).ToArray();
        var positions = brush.Stops.Select(s => (float)s.Offset).ToArray();
        var tileMode = ToTileMode(brush.SpreadMethod);
        var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint((float)brush.StartPoint.X, (float)brush.StartPoint.Y),
            new SKPoint((float)brush.EndPoint.X, (float)brush.EndPoint.Y),
            colors,
            positions,
            tileMode);
        return paint;
    }

    private SKPaint FillPaint(IRadialGradientBrush brush, Rect bounds)
    {
        var colors = brush.Stops.Select(s => ApplyGlobalAlpha(s.Color)).Select(FramebufferText.ToSkColor).ToArray();
        var positions = brush.Stops.Select(s => (float)s.Offset).ToArray();
        var radius = (float)Math.Max(brush.RadiusX, brush.RadiusY);
        var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        paint.Shader = SKShader.CreateRadialGradient(
            new SKPoint((float)brush.Center.X, (float)brush.Center.Y),
            radius,
            colors,
            positions,
            ToTileMode(brush.SpreadMethod));
        return paint;
    }

    private Color ApplyGlobalAlpha(Color color)
    {
        if (GlobalAlpha >= 0.999f)
        {
            return color;
        }

        return color.WithAlpha((byte)Math.Clamp(Math.Round(color.A * GlobalAlpha), 0, 255));
    }

    private static SKShaderTileMode ToTileMode(SpreadMethod spreadMethod)
        => spreadMethod switch
        {
            SpreadMethod.Repeat => SKShaderTileMode.Repeat,
            SpreadMethod.Reflect => SKShaderTileMode.Mirror,
            _ => SKShaderTileMode.Clamp,
        };

    private static SKRect ToSkRect(Rect rect)
        => new((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);

    private static SKMatrix ToSkMatrix(Matrix3x2 matrix)
        => new()
        {
            ScaleX = matrix.M11,
            SkewX = matrix.M21,
            TransX = matrix.M31,
            SkewY = matrix.M12,
            ScaleY = matrix.M22,
            TransY = matrix.M32,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1,
        };

    private static SKPath ToSkPath(PathGeometry path)
    {
        var skPath = new SKPath
        {
            FillType = path.FillRule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding,
        };

        foreach (var command in path.Commands)
        {
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                    skPath.MoveTo((float)command.X0, (float)command.Y0);
                    break;
                case PathCommandType.LineTo:
                    skPath.LineTo((float)command.X0, (float)command.Y0);
                    break;
                case PathCommandType.BezierTo:
                    skPath.CubicTo(
                        (float)command.X0, (float)command.Y0,
                        (float)command.X1, (float)command.Y1,
                        (float)command.X2, (float)command.Y2);
                    break;
                case PathCommandType.Close:
                    skPath.Close();
                    break;
            }
        }

        return skPath;
    }
}
