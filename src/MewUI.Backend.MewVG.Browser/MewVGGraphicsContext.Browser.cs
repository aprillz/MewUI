using System.Runtime.CompilerServices;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.OpenGL;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed partial class MewVGWin32GraphicsContext
{
    // A single run wider or taller than this would blow past the texture budget for no visible gain.
    private const int MAX_TEXT_EXTENT_PX = 4096;

    private readonly BrowserWindowResources? _resources;
    private readonly MewVGGLOffscreenSurface? _offscreen;
    private readonly IMewVGOffscreenSurfaceProvider? _offscreenProvider;
    private readonly OpenGLPixelRenderSurface? _pixelSurface;

    // Offscreen surfaces are pooled and handed out again, but a context is built for each pass. A
    // cache owned by the context is therefore thrown away while the surface it drew into lives on,
    // so every pass re-rasterizes text the previous one already had. Keying the cache off the
    // surface is what the desktop backends do, where the pooled surface carries the cache itself.
    private static readonly ConditionalWeakTable<MewVGGLOffscreenSurface, BrowserTextCache> _offscreenTextCaches = new();

    internal MewVGWin32GraphicsContext(BrowserWindowResources resources)
    {
        _resources = resources;
        _vg = resources.Vg;
    }

    private MewVGWin32GraphicsContext(
        MewVGGLOffscreenSurface offscreen,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        OpenGLPixelRenderSurface pixelSurface)
    {
        _offscreen = offscreen;
        _offscreenProvider = offscreenProvider;
        _pixelSurface = pixelSurface;
        _vg = offscreen.Vg;
    }

    internal static MewVGWin32GraphicsContext CreateForOffscreen(
        MewVGGLOffscreenSurface offscreen,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        OpenGLPixelRenderSurface pixelSurface)
        => new(offscreen, offscreenProvider, pixelSurface);

    private uint FrameTargetFbo => _pixelSurface?.Fbo ?? 0;

    partial void BeginFramePlatform()
    {
        try
        {
            if (_pixelSurface != null)
            {
                _offscreenProvider!.EnterSession();
                PreparePixelSurface(_pixelSurface);
                _pixelSurface.SetContentSize(_viewportWidthPx, _viewportHeightPx);
            }
            else
            {
                BrowserNative.MakeContextCurrent();
                OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
                GL.ColorMask(true, true, true, true);
                GL.ClearColor(0f, 0f, 0f, 0f);
                GL.Clear(GL.GL_COLOR_BUFFER_BIT);
            }

            GL.Viewport(0, 0, _viewportWidthPx, _viewportHeightPx);
            _vg.BeginFrame((float)_viewportWidthDip, (float)_viewportHeightDip, (float)DpiScale);
            _vg.ResetTransform();
            _vg.ResetScissor();
        }
        catch
        {
            AbortFrame();
            throw;
        }
    }

    partial void EndFramePlatform()
    {
        try
        {
            // A nested offscreen pass leaves its own framebuffer and viewport bound, and NanoVG
            // only flushes here, so the target has to be restored before EndFrame runs.
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, FrameTargetFbo);
            GL.Viewport(0, 0, _viewportWidthPx, _viewportHeightPx);

            _vg.EndFrame();

            if (_pixelSurface != null)
            {
                _pixelSurface.RequestDeferredReadback();
                OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
                _offscreenProvider!.ReleasePendingImagesForVg(_vg);
                NvgStrokeHelper.ReleasePendingGradientLutDeletes(_vg);
                if (_offscreenProvider.ExitSession())
                {
                    _offscreenProvider.ReleasePendingTargetsUnderCurrentContext();
                }
            }
        }
        catch
        {
            AbortFrame();
            throw;
        }
    }

    private void AbortFrame()
    {
        _offscreenProvider?.ExitSession();
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
    }

    private static void PreparePixelSurface(OpenGLPixelRenderSurface pixelSurface)
    {
        pixelSurface.InitializeFbo();
        if (!pixelSurface.IsFboInitialized || pixelSurface.Fbo == 0)
        {
            throw new PlatformNotSupportedException("WebGL2 framebuffer objects are required for offscreen rendering.");
        }

        pixelSurface.RecordCreationContext(MewVGWin32GraphicsFactory.GetCurrentGLContextStatic());
        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, pixelSurface.Fbo);

        // A previous flush can leave color writes masked, which would make the clear below skip
        // alpha and leave opaque black where the surface should stay transparent.
        GL.ColorMask(true, true, true, true);
        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(GL.GL_COLOR_BUFFER_BIT);
    }

    partial void DestroyPlatform()
    {
        if (_pixelSurface != null)
        {
            _offscreenProvider!.ReturnSurface(_offscreen!);
        }
        else
        {
            _resources!.InvalidateContext(this);
        }
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        => MeasureTextCore(text, font, double.PositiveInfinity);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => MeasureTextCore(text, font, maxWidth);

    private static Size MeasureTextCore(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => BrowserTextMeasure.Measure(text, font, maxWidth);

    public override BackendTextLayout CreateBackendTextLayout(
        ReadOnlySpan<char> text,
        BackendTextFormat format,
        in BackendTextLayoutConstraints constraints)
    {
        var bounds = constraints.Bounds;
        double maxWidth = double.IsPositiveInfinity(bounds.Width) ? 0 : bounds.Width;
        var measured = format.Wrapping == TextWrapping.NoWrap
            ? MeasureText(text, format.Font)
            : MeasureText(text, format.Font, maxWidth);
        double width = maxWidth > 0 ? maxWidth : measured.Width;
        double height = double.IsPositiveInfinity(bounds.Height) || bounds.Height <= 0
            ? measured.Height
            : bounds.Height;

        return new BackendTextLayout
        {
            MeasuredSize = measured,
            EffectiveBounds = new Rect(bounds.X, bounds.Y, width, height),
            EffectiveMaxWidth = width,
            ContentHeight = measured.Height,
        };
    }

    public override void DrawBackendTextLayout(
        ReadOnlySpan<char> text,
        BackendTextFormat format,
        BackendTextLayout layout,
        Color color)
    {
        if (text.IsEmpty || color.A == 0)
        {
            return;
        }

        var bounds = layout.EffectiveBounds;
        double widthDip = bounds.Width > 0 ? bounds.Width : layout.MeasuredSize.Width;
        double heightDip = bounds.Height > 0 ? bounds.Height : layout.MeasuredSize.Height;
        if (widthDip <= 0 || heightDip <= 0)
        {
            return;
        }

        // Rasterize at the run's own size rather than the layout box: an animating box would miss
        // the cache every frame and re-run Canvas2D for every visible label.
        var measured = layout.MeasuredSize;
        double inkWidthDip = measured.Width > 0 ? Math.Min(measured.Width, widthDip) : widthDip;
        double inkHeightDip = measured.Height > 0 ? Math.Min(measured.Height, heightDip) : heightDip;

        int widthPx = (int)Math.Ceiling(inkWidthDip * DpiScale);
        int heightPx = (int)Math.Ceiling(inkHeightDip * DpiScale);
        if (widthPx <= 0 || heightPx <= 0 || widthPx > MAX_TEXT_EXTENT_PX || heightPx > MAX_TEXT_EXTENT_PX)
        {
            return;
        }

        // The window path shares one cache across context recreations; an offscreen pass shares the
        // one its pooled surface carries.
        var cache = _resources?.TextCache
            ?? _offscreenTextCaches.GetValue(_offscreen!, surface => new BrowserTextCache(surface.Vg));
        int imageId = cache.GetOrCreateImage(
            text, layout, BrowserFont.CssFontFor(format.Font), widthPx, heightPx, DpiScale, color);
        if (imageId == 0)
        {
            return;
        }

        // The run is drawn inside the layout box, aligned the way the box asked for.
        double drawWidth = widthPx / DpiScale;
        double drawHeight = heightPx / DpiScale;
        double offsetX = format.HorizontalAlignment switch
        {
            TextAlignment.Center => (widthDip - drawWidth) / 2,
            TextAlignment.Right => widthDip - drawWidth,
            _ => 0,
        };
        double offsetY = format.VerticalAlignment switch
        {
            TextAlignment.Center => (heightDip - drawHeight) / 2,
            TextAlignment.Bottom => heightDip - drawHeight,
            _ => 0,
        };

        double destX = bounds.X + offsetX;
        double destY = bounds.Y + offsetY;

        // Snapped on the DEVICE grid, translation included, the way the desktop backends snap
        // text: the run's texture then maps texel-for-pixel instead of being bilinearly smeared
        // at a fractional offset, and a cache capture (which differs from the window pass by an
        // integer-pixel translate) lands its rows on the same grid as live rendering.
        if (_textPixelSnap)
        {
            (destX, destY) = RenderingUtil.SnapTextOriginToDevice(destX, destY, _transform, DpiScale);
        }

        DrawImagePattern(
            imageId,
            new Rect(destX, destY, drawWidth, drawHeight),
            1f,
            sourceRect: null,
            widthPx,
            heightPx);
    }

    public override void DrawImage(IImage image, Point location)
    {
        ArgumentNullException.ThrowIfNull(image);
        DrawImageCore(image, new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight));
    }

    protected override void DrawImageCore(IImage image, Rect destRect)
        => DrawImageCore(image, destRect, new Rect(0, 0, image.PixelWidth, image.PixelHeight));

    protected override void DrawImageCore(IImage image, Rect destRect, Rect sourceRect)
    {
        if (image is not MewVGImage vgImage)
        {
            return;
        }

        int imageId = vgImage.GetOrCreateImageId(_vg, GetImageFlags());
        if (imageId != 0)
        {
            DrawImagePattern(imageId, destRect, 1f, AdjustSourceRectForContent(vgImage, sourceRect),
                image.PixelWidth, image.PixelHeight);
        }
    }

    /// <summary>
    /// Maps a content-space source rect to the sampled image space of a pooled FBO surface.
    /// </summary>
    private static Rect? AdjustSourceRectForContent(MewVGImage vgImage, Rect? sourceRect)
    {
        if (vgImage.Source is not OpenGLPixelRenderSurface surface || !surface.IsFboInitialized)
        {
            return sourceRect;
        }

        int contentWidth = surface.ContentWidthPx;
        int contentHeight = surface.ContentHeightPx;
        int offsetY = surface.PixelHeight - contentHeight;
        if (offsetY == 0 && contentWidth == surface.PixelWidth)
        {
            return sourceRect;
        }

        // FlipY sampling anchors the FBO's rendered content at the bottom of the image space, so on
        // a pooled allocation taller than the content the crop must shift down by the unrendered
        // band or it samples cleared texels above the content.
        var src = sourceRect ?? new Rect(0, 0, contentWidth, contentHeight);
        return new Rect(src.X, src.Y + offsetY, src.Width, src.Height);
    }
}
