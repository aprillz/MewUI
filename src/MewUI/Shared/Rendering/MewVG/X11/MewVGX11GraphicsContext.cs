using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.FreeType;
using Aprillz.MewUI.Rendering.OpenGL;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed partial class MewVGX11GraphicsContext
{
    private readonly nint _display;
    private readonly nint _window;
    private readonly MewVGX11WindowResources _resources;

    public MewVGX11GraphicsContext(
        nint display,
        nint window,
        int pixelWidth,
        int pixelHeight,
        double dpiScale,
        MewVGX11WindowResources resources)
    {
        _display = display;
        _window = window;
        _resources = resources;
        _vg = resources.Vg;

        DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;

        _viewportWidthPx = Math.Max(1, pixelWidth);
        _viewportHeightPx = Math.Max(1, pixelHeight);
        _viewportWidthDip = _viewportWidthPx / DpiScale;
        _viewportHeightDip = _viewportHeightPx / DpiScale;

        _resources.MakeCurrent(_display);
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
        _resources.SetSwapInterval(GetSwapInterval());
        _resources.SwapBuffers(_display, _window);
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

        if (font is not FreeTypeFont ftFont)
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

            widthPx = Math.Max(1, RenderingUtil.CeilToPixelInt(measured.Width, DpiScale));
            heightPx = Math.Max(1, RenderingUtil.CeilToPixelInt(measured.Height, DpiScale));
            boundsPx = new PixelRect(boundsPx.Left, boundsPx.Top, widthPx, heightPx);
        }

        widthPx = ClampTextRasterExtent(widthPx, boundsPx, axis: 0);
        heightPx = ClampTextRasterExtent(heightPx, boundsPx, axis: 1);
        boundsPx = new PixelRect(boundsPx.Left, boundsPx.Top, widthPx, heightPx);

        // Wrap + vertical alignment: shift the bitmap top so shorter text is positioned correctly.
        if (wrapping != TextWrapping.NoWrap && verticalAlignment != TextAlignment.Top && bounds.Height > 0)
        {
            var measured = MeasureText(text, font, bounds.Width > 0 ? bounds.Width : MeasureText(text, font).Width);
            int textHeightPx = Math.Max(1, RenderingUtil.CeilToPixelInt(measured.Height, DpiScale));
            int remaining = heightPx - textHeightPx;
            if (remaining > 0)
            {
                int yOffsetPx = verticalAlignment == TextAlignment.Bottom ? remaining : remaining / 2;
                boundsPx = new PixelRect(boundsPx.Left, boundsPx.Top + yOffsetPx, widthPx, textHeightPx);
                heightPx = textHeightPx;
            }
        }

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

        // FreeType bakes both horizontal and vertical alignment into the rasterized bitmap.
        // Draw at the (possibly adjusted) boundsPx origin; no extra drawX/drawY shift needed.
        double drawX = RenderingUtil.RoundToPixelInt(boundsPx.Left / DpiScale, DpiScale) / DpiScale;
        double drawY = RenderingUtil.RoundToPixelInt(boundsPx.Top / DpiScale, DpiScale) / DpiScale;
        double widthDip = widthPx / DpiScale;
        double heightDip = heightPx / DpiScale;

        var key = new MewVGTextCacheKey(new TextCacheKey(
            string.GetHashCode(text),
            0,
            ftFont.FontPath,
            ftFont.PixelHeight,
            color.ToArgb(),
            widthPx,
            heightPx,
            (int)horizontalAlignment,
            (int)verticalAlignment,
            (int)wrapping));

        if (!_resources.TextCache.TryGet(key, out var entry))
        {
            var bmp = FreeTypeText.Rasterize(
                text,
                ftFont,
                widthPx,
                heightPx,
                color,
                horizontalAlignment,
                verticalAlignment,
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
        if (font is FreeTypeFont ftFont)
        {
            var px = FreeTypeText.Measure(text, ftFont);
            return new Size(px.Width / DpiScale, px.Height / DpiScale);
        }

        using var fallback = new OpenGLMeasurementContext((uint)Math.Round(DpiScale * 96.0));
        return fallback.MeasureText(text, font);
    }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (font is FreeTypeFont ftFont)
        {
            int maxWidthPx = maxWidth <= 0
                ? 0
                : Math.Max(1, RenderingUtil.CeilToPixelInt(maxWidth, DpiScale));
            var px = FreeTypeText.Measure(text, ftFont, maxWidthPx, TextWrapping.Wrap);
            return new Size(px.Width / DpiScale, px.Height / DpiScale);
        }

        using var fallback = new OpenGLMeasurementContext((uint)Math.Round(DpiScale * 96.0));
        return fallback.MeasureText(text, font, maxWidth);
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
