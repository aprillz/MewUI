using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.OpenGL;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed partial class MewVGWin32GraphicsContext
{
    private readonly nint _hwnd;
    private readonly nint _hdc;
    private readonly MewVGWindowResources _resources;
    private readonly OpenGLBitmapRenderTarget? _bitmapTarget;
    private readonly bool _swapOnDispose;

    public MewVGWin32GraphicsContext(
        nint hwnd,
        nint hdc,
        int pixelWidth,
        int pixelHeight,
        double dpiScale,
        MewVGWindowResources resources,
        OpenGLBitmapRenderTarget? bitmapTarget = null)
    {
        _hwnd = hwnd;
        _hdc = hdc;
        _resources = resources;
        _vg = resources.Vg;
        _bitmapTarget = bitmapTarget;
        _swapOnDispose = bitmapTarget == null;

        _dpiScale = dpiScale <= 0 ? 1.0 : dpiScale;

        _viewportWidthPx = Math.Max(1, pixelWidth);
        _viewportHeightPx = Math.Max(1, pixelHeight);
        _viewportWidthDip = _viewportWidthPx / DpiScale;
        _viewportHeightDip = _viewportHeightPx / DpiScale;

        _resources.MakeCurrent(_hdc);

        if (_bitmapTarget != null)
        {
            _bitmapTarget.InitializeFbo();
            if (!_bitmapTarget.IsFboInitialized || _bitmapTarget.Fbo == 0)
            {
                _resources.ReleaseCurrent();
                throw new PlatformNotSupportedException("OpenGL FBOs are required for Win32 layered window presentation.");
            }

            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, _bitmapTarget.Fbo);

            // Clear FBO to transparent black before rendering.
            // NanoVG's blending (SRC_ALPHA, ONE_MINUS_SRC_ALPHA) cannot overwrite with alpha=0,
            // so a hardware clear is required for proper layered window transparency.
            GL.ClearColor(0f, 0f, 0f, 0f);
            GL.Clear(GL.GL_COLOR_BUFFER_BIT);
        }

        GL.Viewport(0, 0, _viewportWidthPx, _viewportHeightPx);

        _vg.BeginFrame((float)_viewportWidthDip, (float)_viewportHeightDip, (float)DpiScale);
        _vg.ResetTransform();
        _vg.ResetScissor();
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _vg.EndFrame();

        if (_bitmapTarget != null)
        {
            _bitmapTarget.ReadbackFromFbo();
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
            _resources.ReleaseCurrent();
            return;
        }

        if (_swapOnDispose)
        {
            _resources.SetSwapInterval(GetSwapInterval());
            _resources.SwapBuffers(_hdc, _hwnd);
        }

        _resources.ReleaseCurrent();
    }

    private static int GetSwapInterval()
    {
        if (!Application.IsRunning)
        {
            return 1;
        }

        return Application.Current.RenderLoopSettings.VSyncEnabled ? 1 : 0;
    }

    #region Text Rendering

    protected override void DrawTextCore(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None)
    {
        if (text.IsEmpty) return;
        if (font is not GdiFont gdiFont) return;

        var boundsPx = ToPixelRect(bounds);
        int widthPx = boundsPx.Width;
        int heightPx = boundsPx.Height;

        if (widthPx <= 0 || heightPx <= 0) return;

        widthPx = ClampTextRasterExtent(widthPx, boundsPx, axis: 0);
        heightPx = ClampTextRasterExtent(heightPx, boundsPx, axis: 1);
        boundsPx = new PixelRect(boundsPx.Left, boundsPx.Top, widthPx, heightPx);

        if (_clipBoundsWorld.HasValue)
        {
            var c = _clipBoundsWorld.Value;
            double worldLeft = bounds.X + _translateX;
            double worldTop = bounds.Y + _translateY;
            double worldRight = worldLeft + widthPx / DpiScale;
            double worldBottom = worldTop + heightPx / DpiScale;
            if (worldRight <= c.X || worldLeft >= c.Right || worldBottom <= c.Y || worldTop >= c.Bottom)
                return;
        }

        double drawX = bounds.X;
        double drawY = bounds.Y;
        double widthDip = widthPx / DpiScale;
        double heightDip = heightPx / DpiScale;

        if (bounds.Width > 0)
        {
            drawX = horizontalAlignment switch
            {
                TextAlignment.Center => bounds.X + (bounds.Width - widthDip) * 0.5,
                TextAlignment.Right => bounds.Right - widthDip,
                _ => bounds.X
            };
        }

        if (bounds.Height > 0)
        {
            drawY = verticalAlignment switch
            {
                TextAlignment.Center => bounds.Y + (bounds.Height - heightDip) * 0.5,
                TextAlignment.Bottom => bounds.Bottom - heightDip,
                _ => bounds.Y
            };
        }

        if (_textPixelSnap)
        {
            drawX = RenderingUtil.RoundToPixelInt(drawX, DpiScale) / DpiScale;
            drawY = RenderingUtil.RoundToPixelInt(drawY, DpiScale) / DpiScale;
        }

        var textHash = string.GetHashCode(text);
        var key = new MewVGTextCacheKey(new TextCacheKey(
            textHash, gdiFont.Handle, string.Empty, 0, color.ToArgb(),
            widthPx, heightPx,
            (int)horizontalAlignment, (int)verticalAlignment,
            (int)wrapping, (int)trimming));

        if (!_resources.TextCache.TryGet(key, out var entry))
        {
            var bmp = OpenGLTextRasterizer.Rasterize(
                _hdc, gdiFont, text, widthPx, heightPx, color,
                horizontalAlignment, verticalAlignment, wrapping, trimming);
            entry = _resources.TextCache.CreateImage(key, ref bmp);
        }

        if (entry.ImageId == 0) return;

        var drawRect = new Rect(drawX, drawY, widthDip, heightDip);
        var srcRect = new Rect(entry.X, entry.Y, entry.WidthPx, entry.HeightPx);
        DrawImagePattern(entry.ImageId, drawRect, alpha: 1f, sourceRect: srcRect, entry.AtlasWidthPx, entry.AtlasHeightPx);
    }

    private Size MeasureTextCore(ReadOnlySpan<char> text, IFont font)
    {
        using var measure = new GdiMeasurementContext(User32.GetDC(0), (uint)Math.Round(DpiScale * 96));
        return measure.MeasureText(text, font);
    }

    private Size MeasureTextCore(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        using var measure = new GdiMeasurementContext(User32.GetDC(0), (uint)Math.Round(DpiScale * 96));
        return measure.MeasureText(text, font, maxWidth);
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        => MeasureTextCore(text, font);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => MeasureTextCore(text, font, maxWidth);

    public override TextLayout CreateTextLayout(ReadOnlySpan<char> text,
        TextFormat format, in TextLayoutConstraints constraints)
    {
        var bounds = constraints.Bounds;
        var safeBounds = new Rect(bounds.X, bounds.Y,
            double.IsPositiveInfinity(bounds.Width) ? 0 : bounds.Width,
            double.IsPositiveInfinity(bounds.Height) ? 0 : bounds.Height);

        Size measured;
        if (format.Wrapping == TextWrapping.NoWrap)
        {
            measured = MeasureTextCore(text, format.Font);
        }
        else
        {
            double maxWidth = safeBounds.Width > 0 ? safeBounds.Width : MeasureTextCore(text, format.Font).Width;
            measured = MeasureTextCore(text, format.Font, maxWidth);
        }

        var boundsPx = ToPixelRect(safeBounds);
        int widthPx = boundsPx.Width;
        int heightPx = boundsPx.Height;

        if (widthPx <= 0 || heightPx <= 0)
        {
            widthPx = Math.Max(1, RenderingUtil.CeilToPixelInt(measured.Width, DpiScale));
            heightPx = Math.Max(1, RenderingUtil.CeilToPixelInt(measured.Height, DpiScale));
        }

        double effectiveMaxWidth = safeBounds.Width > 0 ? safeBounds.Width : measured.Width;
        var effectiveBounds = new Rect(safeBounds.X, safeBounds.Y,
            widthPx / DpiScale, heightPx / DpiScale);

        return new TextLayout
        {
            MeasuredSize = measured,
            EffectiveBounds = effectiveBounds,
            EffectiveMaxWidth = effectiveMaxWidth,
            ContentHeight = measured.Height
        };
    }

    public override void DrawTextLayout(ReadOnlySpan<char> text,
        TextFormat format, TextLayout layout, Color color)
    {
        if (text.IsEmpty) return;
        if (format.Font is not GdiFont gdiFont) return;

        var bounds = layout.EffectiveBounds;
        var boundsPx = ToPixelRect(bounds);
        int widthPx = boundsPx.Width;
        int heightPx = boundsPx.Height;

        if (widthPx <= 0 || heightPx <= 0) return;

        widthPx = ClampTextRasterExtent(widthPx, boundsPx, axis: 0);
        heightPx = ClampTextRasterExtent(heightPx, boundsPx, axis: 1);
        boundsPx = new PixelRect(boundsPx.Left, boundsPx.Top, widthPx, heightPx);

        if (_clipBoundsWorld.HasValue)
        {
            var c = _clipBoundsWorld.Value;
            double worldLeft = bounds.X + _translateX;
            double worldTop = bounds.Y + _translateY;
            double worldRight = worldLeft + widthPx / DpiScale;
            double worldBottom = worldTop + heightPx / DpiScale;
            if (worldRight <= c.X || worldLeft >= c.Right || worldBottom <= c.Y || worldTop >= c.Bottom)
                return;
        }

        var originalBounds = layout.EffectiveBounds;
        double drawX = originalBounds.X;
        double drawY = originalBounds.Y;
        double widthDip = widthPx / DpiScale;
        double heightDip = heightPx / DpiScale;

        if (originalBounds.Width > 0)
        {
            drawX = format.HorizontalAlignment switch
            {
                TextAlignment.Center => originalBounds.X + (originalBounds.Width - widthDip) * 0.5,
                TextAlignment.Right => originalBounds.X + originalBounds.Width - widthDip,
                _ => originalBounds.X
            };
        }

        if (originalBounds.Height > 0)
        {
            drawY = format.VerticalAlignment switch
            {
                TextAlignment.Center => originalBounds.Y + (originalBounds.Height - heightDip) * 0.5,
                TextAlignment.Bottom => originalBounds.Y + originalBounds.Height - heightDip,
                _ => originalBounds.Y
            };
        }

        if (_textPixelSnap)
        {
            drawX = RenderingUtil.RoundToPixelInt(drawX, DpiScale) / DpiScale;
            drawY = RenderingUtil.RoundToPixelInt(drawY, DpiScale) / DpiScale;
        }

        var textHash = string.GetHashCode(text);
        var key = new MewVGTextCacheKey(new TextCacheKey(
            textHash,
            gdiFont.Handle,
            string.Empty,
            0,
            color.ToArgb(),
            widthPx,
            heightPx,
            (int)format.HorizontalAlignment,
            (int)format.VerticalAlignment,
            (int)format.Wrapping,
            (int)format.Trimming));

        if (!_resources.TextCache.TryGet(key, out var entry))
        {
            var bmp = OpenGLTextRasterizer.Rasterize(
                _hdc,
                gdiFont,
                text,
                widthPx,
                heightPx,
                color,
                format.HorizontalAlignment,
                format.VerticalAlignment,
                format.Wrapping,
                format.Trimming);
            entry = _resources.TextCache.CreateImage(key, ref bmp);
        }

        if (entry.ImageId == 0) return;

        var drawRect = new Rect(drawX, drawY, widthDip, heightDip);
        var srcRect = new Rect(entry.X, entry.Y, entry.WidthPx, entry.HeightPx);
        DrawImagePattern(entry.ImageId, drawRect, alpha: 1f, sourceRect: srcRect, entry.AtlasWidthPx, entry.AtlasHeightPx);
    }

    #endregion

    #region Image Rendering

    public override void DrawImage(IImage image, Point location)
    {
        ArgumentNullException.ThrowIfNull(image);

        var dest = new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight);
        DrawImageCore(image, dest);
    }

    protected override void DrawImageCore(IImage image, Rect destRect)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image is not MewVGImage vgImage)
        {
            throw new ArgumentException("Image must be a MewVGImage.", nameof(image));
        }

        int imageId = vgImage.GetOrCreateImageId(_vg, GetImageFlags());
        if (imageId == 0)
        {
            return;
        }

        DrawImagePattern(imageId, destRect, alpha: 1f, sourceRect: null, vgImage.PixelWidth, vgImage.PixelHeight);
    }

    protected override void DrawImageCore(IImage image, Rect destRect, Rect sourceRect)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (image is not MewVGImage vgImage)
        {
            throw new ArgumentException("Image must be a MewVGImage.", nameof(image));
        }

        int imageId = vgImage.GetOrCreateImageId(_vg, GetImageFlags());
        if (imageId == 0)
        {
            return;
        }

        DrawImagePattern(imageId, destRect, alpha: 1f, sourceRect: sourceRect, vgImage.PixelWidth, vgImage.PixelHeight);
    }

    #endregion

    private int ClampTextRasterExtent(int extentPx, PixelRect boundsPx, int axis)
    {
        int viewport = axis == 0 ? _viewportWidthPx : _viewportHeightPx;
        if (extentPx <= 0)
        {
            return 1;
        }

        int hardMax = Math.Max(256, viewport * 4);
        if (extentPx <= hardMax)
        {
            return extentPx;
        }

        int remaining = axis == 0 ? Math.Max(1, viewport - boundsPx.Left) : Math.Max(1, viewport - boundsPx.Top);
        return Math.Clamp(remaining, 1, hardMax);
    }

    private PixelRect ToPixelRect(Rect rect)
    {
        int left = RenderingUtil.RoundToPixelInt(rect.X, DpiScale);
        int top = RenderingUtil.RoundToPixelInt(rect.Y, DpiScale);
        int width = RenderingUtil.RoundToPixelInt(rect.Width, DpiScale);
        int height = RenderingUtil.RoundToPixelInt(rect.Height, DpiScale);
        return new PixelRect(left, top, Math.Max(0, width), Math.Max(0, height));
    }

    private readonly record struct PixelRect(int Left, int Top, int Width, int Height);
}
