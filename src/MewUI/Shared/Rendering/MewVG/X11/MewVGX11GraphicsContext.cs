using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.FreeType;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed partial class MewVGX11GraphicsContext
{
    private readonly IX11FrameSession _frameSession;
    private readonly MewVGTextCache _textCache;
    private readonly Action<GpuInteropInvalidatedEventArgs>? _gpuInteropInvalidated;
    private readonly HashSet<GpuDeviceIdentity> _reportedExternalMismatches = new();

    private MewVGX11GraphicsContext(
        IX11FrameSession frameSession,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
    {
        _frameSession = frameSession;
        _vg = frameSession.Vg;
        _textCache = frameSession.TextCache;
        _gpuInteropInvalidated = gpuInteropInvalidated;
    }

    internal static MewVGX11GraphicsContext CreateForWindow(
        MewVGX11WindowResources resources,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
        => new(new X11WindowFrameSession(resources, offscreenProvider), gpuInteropInvalidated);

    internal static MewVGX11GraphicsContext CreateForOffscreen(
        MewVGGLOffscreenSurface offscreen,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        OpenGLPixelRenderSurface pixelSurface)
        => new(new X11OffscreenFrameSession(offscreen, offscreenProvider, pixelSurface), gpuInteropInvalidated: null);

    internal void SetTarget(nint display, nint window)
    {
        if (_frameSession is X11WindowFrameSession windowSession)
        {
            windowSession.SetTarget(display, window);
        }
    }

    partial void BeginFramePlatform()
    {
        try
        {
            _frameSession.BeginFrame();

            GL.Viewport(0, 0, _viewportWidthPx, _viewportHeightPx);

            _vg.BeginFrame((float)_viewportWidthDip, (float)_viewportHeightDip, (float)DpiScale);
            _vg.ResetTransform();
            _vg.ResetScissor();
        }
        catch
        {
            _frameSession.AbortFrame();
            throw;
        }
    }

            // Flushed lazily by Lock/CopyPixels/GetPixelSpan. See Win32 EndFrame
            // comment for the rationale (folds N per-element sync barriers into 0–1).
    partial void EndFramePlatform()
    {
        try
        {
            _frameSession.BindFrameTarget();
            GL.Viewport(0, 0, _viewportWidthPx, _viewportHeightPx);

            _vg.EndFrame();
            _frameSession.EndFrame();
        }
        catch
        {
            _frameSession.AbortFrame();
            throw;
        }
    }

    partial void DestroyPlatform()
    {
        _frameSession.DisposeContext(this);
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
            return;
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
            double worldLeft = bounds.X + _transform.M31;
            double worldTop = bounds.Y + _transform.M32;
            double worldRight = worldLeft + widthPx / DpiScale;
            double worldBottom = worldTop + heightPx / DpiScale;
            if (worldRight <= c.X || worldLeft >= c.Right || worldBottom <= c.Y || worldTop >= c.Bottom)
            {
                return;
            }
        }

        // FreeType bakes both horizontal and vertical alignment into the rasterized bitmap.
        // Draw at the (possibly adjusted) boundsPx origin; no extra drawX/drawY shift needed.
        // Skip snapping during transitions to avoid visible jumping.
        double drawX = _textPixelSnap
            ? RenderingUtil.RoundToPixelInt(boundsPx.Left / DpiScale, DpiScale) / DpiScale
            : boundsPx.Left / DpiScale;
        double drawY = _textPixelSnap
            ? RenderingUtil.RoundToPixelInt(boundsPx.Top / DpiScale, DpiScale) / DpiScale
            : boundsPx.Top / DpiScale;
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
            (int)wrapping,
            (int)trimming));

        if (!_textCache.TryGet(key, out var entry))
        {
            var bmp = FreeTypeText.Rasterize(
                text,
                ftFont,
                widthPx,
                heightPx,
                color,
                horizontalAlignment,
                verticalAlignment,
                wrapping,
                trimming);
            entry = _textCache.CreateImage(key, ref bmp);
        }

        if (entry.ImageId == 0)
        {
            return;
        }

        var drawRect = new Rect(drawX, drawY, widthDip, heightDip);
        var srcRect = new Rect(entry.X, entry.Y, entry.WidthPx, entry.HeightPx);
        DrawImagePattern(entry.ImageId, drawRect, alpha: 1f, sourceRect: srcRect, entry.AtlasWidthPx, entry.AtlasHeightPx);
    }


    private Size MeasureTextCore(ReadOnlySpan<char> text, IFont font)
    {
        if (font is FreeTypeFont ftFont)
        {
            var px = FreeTypeText.Measure(text, ftFont);
            return new Size(px.Width / DpiScale, px.Height / DpiScale);
        }

        using var fallback = new OpenGLMeasurementContext((uint)Math.Round(DpiScale * 96.0));
        return fallback.MeasureText(text, font);
    }

    private Size MeasureTextCore(ReadOnlySpan<char> text, IFont font, double maxWidth)
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
        if (format.Font is not FreeTypeFont ftFont) return;

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
            double worldLeft = bounds.X + _transform.M31;
            double worldTop = bounds.Y + _transform.M32;
            double worldRight = worldLeft + widthPx / DpiScale;
            double worldBottom = worldTop + heightPx / DpiScale;
            if (worldRight <= c.X || worldLeft >= c.Right || worldBottom <= c.Y || worldTop >= c.Bottom)
                return;
        }

        // FreeType bakes alignment into the rasterized bitmap.
        double drawX = _textPixelSnap
            ? RenderingUtil.RoundToPixelInt(boundsPx.Left / DpiScale, DpiScale) / DpiScale
            : boundsPx.Left / DpiScale;
        double drawY = _textPixelSnap
            ? RenderingUtil.RoundToPixelInt(boundsPx.Top / DpiScale, DpiScale) / DpiScale
            : boundsPx.Top / DpiScale;
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
            (int)format.HorizontalAlignment,
            (int)format.VerticalAlignment,
            (int)format.Wrapping,
            (int)format.Trimming));

        if (!_textCache.TryGet(key, out var entry))
        {
            var bmp = FreeTypeText.Rasterize(
                text,
                ftFont,
                widthPx,
                heightPx,
                color,
                format.HorizontalAlignment,
                format.VerticalAlignment,
                format.Wrapping,
                format.Trimming);
            entry = _textCache.CreateImage(key, ref bmp);
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

        if (image is MewVGExternalRasterImage extImage)
        {
            var lease = EnsureExternalAcquired(extImage.Source);
            if (!IsExternalRasterLeaseCompatible(lease))
            {
                return;
            }

            int extImageId = extImage.GetOrCreateImageId(_vg, lease, GetImageFlags());
            if (extImageId == 0) return;
            DrawImagePattern(extImageId, destRect, alpha: 1f, sourceRect: null,
                image.PixelWidth, image.PixelHeight);
            return;
        }

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

        if (image is MewVGExternalRasterImage extImage)
        {
            var lease = EnsureExternalAcquired(extImage.Source);
            if (!IsExternalRasterLeaseCompatible(lease))
            {
                return;
            }

            int extImageId = extImage.GetOrCreateImageId(_vg, lease, GetImageFlags());
            if (extImageId == 0) return;
            DrawImagePattern(extImageId, destRect, alpha: 1f, sourceRect: sourceRect,
                image.PixelWidth, image.PixelHeight);
            return;
        }

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

    private bool IsExternalRasterLeaseCompatible(IExternalRasterLease lease)
    {
        // No-opinion bail-outs: nothing to compare against. The share-group equality check below
        // is the real signal — cross-API pointer collisions don't happen in practice, so a
        // typed-backend discriminator isn't needed.
        if (lease is not IGpuResourceAffinityProvider affinityProvider ||
            affinityProvider.Affinity?.Device is not { } sourceDevice ||
            sourceDevice.IsEmpty)
        {
            return true;
        }

        nint currentContext = LibGL.glXGetCurrentContext();
        nint targetShareGroup = _frameSession.OpenGLShareGroup;
        if (currentContext == 0 ||
            sourceDevice.NativeHandle == currentContext ||
            (targetShareGroup != 0 && sourceDevice.NativeHandle == targetShareGroup))
        {
            return true;
        }

        if (_reportedExternalMismatches.Add(sourceDevice))
        {
            _gpuInteropInvalidated?.Invoke(new GpuInteropInvalidatedEventArgs(
                GpuInteropInvalidationReason.ExternalResourceMismatch,
                renderTargetDeviceChanged: true,
                externalResourceMismatch: true));
        }

        return false;
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

        // boundsPx.Left/Top is in this context's LOCAL space; viewport is in world (FBO) space.
        // When rendering into an offscreen cache the context is translated by -elementOrigin, so the
        // local position must be shifted by the transform to find the true start within the viewport.
        // Without this, an element at a non-zero layout X clamps the raster extent as if the text
        // started that far into a viewport that is only element-sized → text truncated (only works at
        // X=0 where translate==local). The window path has an identity transform, so it is unchanged.
        int translatePx = RenderingUtil.RoundToPixelInt(axis == 0 ? _transform.M31 : _transform.M32, DpiScale);
        int worldStart = (axis == 0 ? boundsPx.Left : boundsPx.Top) + translatePx;
        int remaining = Math.Max(1, viewport - worldStart);
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

    private interface IX11FrameSession
    {
        NanoVGGL Vg { get; }
        MewVGTextCache TextCache { get; }
        nint OpenGLShareGroup { get; }
        void BeginFrame();
        void BindFrameTarget();
        void EndFrame();
        void AbortFrame();
        void DisposeContext(MewVGX11GraphicsContext context);
    }

    private sealed class X11WindowFrameSession : IX11FrameSession
    {
        private readonly MewVGX11WindowResources _resources;
        private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider;
        private nint _display;
        private nint _window;

        public X11WindowFrameSession(MewVGX11WindowResources resources, IMewVGOffscreenSurfaceProvider offscreenProvider)
        {
            _resources = resources;
            _offscreenProvider = offscreenProvider;
        }

        public NanoVGGL Vg => _resources.Vg;

        public MewVGTextCache TextCache => _resources.TextCache;

        public nint OpenGLShareGroup => _resources.OpenGLShareGroup;

        public void SetTarget(nint display, nint window)
        {
            _display = display;
            _window = window;
        }

        public void BeginFrame()
        {
            _resources.MakeCurrent(_display);
            _offscreenProvider.EnterSession();
        }

        public void BindFrameTarget()
            => OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);

        public void EndFrame()
        {
            _offscreenProvider.ReleasePendingImagesForVg(_resources.Vg);
            // Outermost session — drain FBO disposals only after every NVG (window + nested
            // offscreen) has flushed; nested sessions wrap scratch FBO textures via
            // CreateImageFromHandle and the outer's queued draws still reference them.
            if (_offscreenProvider.ExitSession())
            {
                _offscreenProvider.ReleasePendingTargetsUnderCurrentContext();
            }
            TextCache.ReleasePendingDeletes();
            _resources.SetSwapInterval(GetSwapInterval());
            _resources.SwapBuffers(_display, _window);
            _resources.ReleaseCurrent();
        }

        public void AbortFrame()
        {
            _offscreenProvider.ExitSession();
            _resources.ReleaseCurrent();
        }

        public void DisposeContext(MewVGX11GraphicsContext context)
            => _resources.InvalidateCachedContext(context);
    }

    private sealed class X11OffscreenFrameSession : IX11FrameSession
    {
        private readonly MewVGGLOffscreenSurface _offscreen;
        private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider;
        private readonly OpenGLPixelRenderSurface _pixelSurface;

        public X11OffscreenFrameSession(
            MewVGGLOffscreenSurface offscreen,
            IMewVGOffscreenSurfaceProvider offscreenProvider,
            OpenGLPixelRenderSurface pixelSurface)
        {
            _offscreen = offscreen;
            _offscreenProvider = offscreenProvider;
            _pixelSurface = pixelSurface;
        }

        public NanoVGGL Vg => _offscreen.Vg;

        public MewVGTextCache TextCache => _offscreen.TextCache;

        public nint OpenGLShareGroup => _pixelSurface.CreationContext;

        public void BeginFrame()
        {
            _offscreenProvider.EnterSession();
            PreparePixelSurface(_offscreenProvider, _pixelSurface);
        }

        public void BindFrameTarget()
            => OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, _pixelSurface.Fbo);

        public void EndFrame()
        {
            _pixelSurface.RequestDeferredReadback();
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
            // Per-NVG drain — only this offscreen NVG's pending image-id deletions, on its
            // own thread inside its EndFrame. Avoids racing the window NVG mid-frame.
            _offscreenProvider.ReleasePendingImagesForVg(_offscreen.Vg);
            if (_offscreenProvider.ExitSession())
            {
                _offscreenProvider.ReleasePendingTargetsUnderCurrentContext();
            }
        }

        public void AbortFrame()
        {
            _offscreenProvider.ExitSession();
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
        }

        public void DisposeContext(MewVGX11GraphicsContext context)
            => _offscreenProvider.ReturnSurface(_offscreen);
    }

    private static void PreparePixelSurface(IMewVGOffscreenSurfaceProvider offscreenProvider, OpenGLPixelRenderSurface pixelSurface)
    {
        // Don't drain pending target disposals here — see Win32 PreparePixelSurface for
        // the rationale. Drain happens at the outermost session's EndFrame instead.
        pixelSurface.InitializeFbo();
        if (!pixelSurface.IsFboInitialized || pixelSurface.Fbo == 0)
        {
            throw new PlatformNotSupportedException("OpenGL FBOs are required for X11 pixel-surface rendering.");
        }

        // Record the GLXContext that owns the FBO/RB handles — see Win32 path for
        // rationale (FBOs are not shared via glXCreateContext share, only textures).
        pixelSurface.RecordCreationContext(LibGL.glXGetCurrentContext());

        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, pixelSurface.Fbo);

        // Force colormask + stencil mask to "all writes enabled" before clear. NanoVG's
        // path-fill flush sets colormask=(F,F,F,F) for the stencil-marking pass; if its
        // restore at flush end is incomplete, the next glClear leaves alpha untouched
        // (= undefined / 0xFF on a fresh texture), producing opaque-black filter
        // results in transparent regions. See Win32 PreparePixelSurface.
        GL.ColorMask(true, true, true, true);
        GL.ClearColor(0f, 0f, 0f, 0f);

        uint clearMask = GL.GL_COLOR_BUFFER_BIT;
        if (pixelSurface.HasStencil)
        {
            GL.StencilMask(0xFF);
            GL.ClearStencil(0);
            clearMask |= GL.GL_STENCIL_BUFFER_BIT;
        }

        GL.Clear(clearMask);
    }
}
