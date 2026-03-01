using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.OpenGL;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed partial class MewVGGraphicsContext
{
    private readonly nint _hwnd;
    private readonly nint _hdc;
    private readonly MewVGWindowResources _resources;
    private readonly OpenGLBitmapRenderTarget? _bitmapTarget;
    private readonly bool _swapOnDispose;

    public MewVGGraphicsContext(
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

        DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;

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
        }

        GL.Viewport(0, 0, _viewportWidthPx, _viewportHeightPx);

        _vg.BeginFrame((float)_viewportWidthDip, (float)_viewportHeightDip, (float)DpiScale);
        _vg.ResetTransform();
        _vg.ResetScissor();
    }

    public void Dispose()
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

    public void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color)
    {
        if (text.IsEmpty)
        {
            return;
        }

        DrawText(text, new Rect(location.X, location.Y, 0, 0), font, color, TextAlignment.Left, TextAlignment.Top,
            text.IndexOfAny('\r', '\n') >= 0 ? TextWrapping.Wrap : TextWrapping.NoWrap);
    }

    public void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap)
    {
        if (text.IsEmpty)
        {
            return;
        }

        if (font is not GdiFont gdiFont)
        {
            return;
        }

        var boundsPx = ToPixelRect(bounds);

        int widthPx = boundsPx.Width;
        int heightPx = boundsPx.Height;

        // Point-based draw uses measured size.
        if (widthPx <= 0 || heightPx <= 0)
        {
            Size measured;
            if (wrapping == TextWrapping.NoWrap)
            {
                measured = MeasureText(text, font);
            }
            else
            {
                double maxWidth = bounds.Width > 0 ? bounds.Width : MeasureText(text, font).Width;
                measured = MeasureText(text, font, maxWidth);
            }

            // When we materialize a text bitmap for point-based drawing, always round up to avoid clipping.
            widthPx = Math.Max(1, RenderingUtil.CeilToPixelInt(measured.Width, DpiScale));
            heightPx = Math.Max(1, RenderingUtil.CeilToPixelInt(measured.Height, DpiScale));
            boundsPx = new PixelRect(boundsPx.Left, boundsPx.Top, widthPx, heightPx);
        }

        // Guard against pathological sizes (matches OpenGL behavior).
        widthPx = ClampTextRasterExtent(widthPx, boundsPx, axis: 0);
        heightPx = ClampTextRasterExtent(heightPx, boundsPx, axis: 1);
        boundsPx = new PixelRect(boundsPx.Left, boundsPx.Top, widthPx, heightPx);

        // Early clip cull: skip text entirely outside the current scissor region.
        if (_clipBoundsWorld.HasValue)
        {
            var c = _clipBoundsWorld.Value;
            double worldLeft = bounds.X + _translateX;
            double worldTop = bounds.Y + _translateY;
            double worldRight = worldLeft + widthPx / DpiScale;
            double worldBottom = worldTop + heightPx / DpiScale;
            if (worldRight <= c.X || worldLeft >= c.Right || worldBottom <= c.Y || worldTop >= c.Bottom)
            {
                return;
            }
        }

        double drawX = bounds.X;
        double drawY = bounds.Y;
        double widthDip = widthPx / DpiScale;
        double heightDip = heightPx / DpiScale;

        bool adjustedForWrapAlignment = false;
        bool useRasterVerticalAlignment = wrapping == TextWrapping.NoWrap
            && verticalAlignment != TextAlignment.Top
            && bounds.Height > 0;
        if (wrapping != TextWrapping.NoWrap && verticalAlignment != TextAlignment.Top && bounds.Height > 0)
        {
            double maxWidthDip = bounds.Width > 0 ? bounds.Width : MeasureText(text, font).Width;
            var measured = MeasureText(text, font, maxWidthDip);
            int textHeightPx = Math.Max(1, RenderingUtil.CeilToPixelInt(measured.Height, DpiScale));
            int remaining = heightPx - textHeightPx;
            if (remaining > 0)
            {
                int yOffsetPx = verticalAlignment == TextAlignment.Bottom
                    ? remaining
                    : remaining / 2;

                boundsPx = new PixelRect(boundsPx.Left, boundsPx.Top + yOffsetPx, widthPx, textHeightPx);
                heightPx = textHeightPx;
                heightDip = heightPx / DpiScale;
                drawY = boundsPx.Top / DpiScale;
                adjustedForWrapAlignment = true;
            }
        }

        if (bounds.Width > 0)
        {
            drawX = horizontalAlignment switch
            {
                TextAlignment.Center => bounds.X + (bounds.Width - widthDip) * 0.5,
                TextAlignment.Right => bounds.Right - widthDip,
                _ => bounds.X
            };
        }

        if (bounds.Height > 0 && !useRasterVerticalAlignment && !adjustedForWrapAlignment)
        {
            drawY = verticalAlignment switch
            {
                TextAlignment.Center => bounds.Y + (bounds.Height - heightDip) * 0.5,
                TextAlignment.Bottom => bounds.Bottom - heightDip,
                _ => bounds.Y
            };
        }

        // Pixel-snap to avoid sampling between texels (blurry text) when drawing rasterized text via image patterns.
        // NanoVG coordinates are in DIP, so we snap to the underlying device-pixel grid.
        drawX = RenderingUtil.RoundToPixelInt(drawX, DpiScale) / DpiScale;
        drawY = RenderingUtil.RoundToPixelInt(drawY, DpiScale) / DpiScale;

        var textHash = string.GetHashCode(text);
        var key = new MewVGTextCacheKey(new TextCacheKey(
            textHash,
            gdiFont.Handle,
            string.Empty,
            0,
            color.ToArgb(),
            widthPx,
            heightPx,
            (int)horizontalAlignment,
            (int)verticalAlignment,
            (int)wrapping));

        if (!_resources.TextCache.TryGet(key, out var entry))
        {
            var bmp = OpenGLTextRasterizer.Rasterize(
                _hdc,
                gdiFont,
                text,
                widthPx,
                heightPx,
                color,
                horizontalAlignment,
                useRasterVerticalAlignment ? verticalAlignment : TextAlignment.Top,
                wrapping);
            entry = _resources.TextCache.CreateImage(key, ref bmp);
        }

        if (entry.ImageId == 0)
        {
            return;
        }


        var drawRect = new Rect(drawX, drawY, widthDip, heightDip);
        var srcRect = new Rect(entry.X, entry.Y, entry.WidthPx, entry.HeightPx);
        DrawImagePattern(entry.ImageId, drawRect, alpha: 1f, sourceRect: srcRect, entry.AtlasWidthPx, entry.AtlasHeightPx);
    }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        using var measure = new GdiMeasurementContext(User32.GetDC(0), (uint)Math.Round(DpiScale * 96));
        return measure.MeasureText(text, font);
    }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        using var measure = new GdiMeasurementContext(User32.GetDC(0), (uint)Math.Round(DpiScale * 96));
        return measure.MeasureText(text, font, maxWidth);
    }

    #endregion

    #region Image Rendering

    public void DrawImage(IImage image, Point location)
    {
        ArgumentNullException.ThrowIfNull(image);

        var dest = new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight);
        DrawImage(image, dest);
    }

    public void DrawImage(IImage image, Rect destRect)
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

    public void DrawImage(IImage image, Rect destRect, Rect sourceRect)
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
