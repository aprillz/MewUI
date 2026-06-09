using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed partial class MewVGWin32GraphicsContext
{
    private readonly IWin32FrameSession _frameSession;
    private readonly MewVGTextCache _textCache;
    private readonly Action<GpuInteropInvalidatedEventArgs>? _gpuInteropInvalidated;
    private readonly HashSet<GpuDeviceIdentity> _reportedExternalMismatches = new();
    private GdiMeasurementContext? _measureContext;

    private MewVGWin32GraphicsContext(
        IWin32FrameSession frameSession,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
    {
        _frameSession = frameSession;
        _vg = frameSession.Vg;
        _textCache = frameSession.TextCache;
        _gpuInteropInvalidated = gpuInteropInvalidated;
    }

    internal static MewVGWin32GraphicsContext CreateForWindow(
        MewVGWin32WindowResources resources,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        nint hwnd,
        nint hdc,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
        => new(new WindowBackbufferFrameSession(resources, offscreenProvider, hwnd, hdc), gpuInteropInvalidated);

    /// <summary>
    /// Builds a graphics context that renders into a pixel surface (FBO) while
    /// piggybacking on the WGL context that is already current on the calling
    /// thread. The supplied <paramref name="offscreen"/> resources must have
    /// been borrowed via
    /// <see cref="IMewVGOffscreenSurfaceProvider.AcquireSurface"/>; the
    /// context takes ownership and returns it to the provider on Dispose.
    /// </summary>
    internal static MewVGWin32GraphicsContext CreateForOffscreen(
        MewVGGLOffscreenSurface offscreen,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        OpenGLPixelRenderSurface pixelSurface,
        nint hdc)
        => new(new OffscreenPixelSurfaceFrameSession(offscreen, offscreenProvider, pixelSurface, hdc), gpuInteropInvalidated: null);

    internal static MewVGWin32GraphicsContext CreateForLayeredWindow(
        MewVGWin32WindowResources resources,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        nint hwnd,
        nint hdc,
        OpenGLPixelRenderSurface pixelSurface)
        => new(new WindowPixelSurfaceFrameSession(resources, offscreenProvider, hwnd, hdc, pixelSurface), gpuInteropInvalidated: null);

    internal void SetWindowTarget(nint hwnd, nint hdc)
        => (_frameSession as WindowBackbufferFrameSession)?.SetTarget(hwnd, hdc);

    partial void DestroyPlatform()
    {
        // Drop the resources' cached reference to this instance. Without this
        // step, MewVGWindowResources.GetOrCreateContext keeps handing out the
        // disposed context after a window resize. Reusing it is doubly broken:
        // its _saveStack has already been Returned to the pool, so any later
        // Rent (e.g. by an offscreen context creation) hands back the
        // same Stack instance and the two contexts end up sharing state.
        _frameSession.DisposeContext(this);

        // Return our borrowed offscreen NVG to the pool so a sibling or
        // subsequent offscreen pass can reuse it. Done after the NVG's frame
        // has fully ended (OnEndFrame was called before OnDispose).
    }

    partial void BeginFramePlatform()
    {
        // Main rendering: take ownership of the WGL context for this frame.
        // Offscreen rendering: caller already has the main context current;
        // reuse it so there is no wglMakeCurrent roundtrip.
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

    partial void EndFramePlatform()
    {
        _measureContext?.Dispose();
        _measureContext = null;

        // NanoVG.Flush rebinds program / VAO / textures unconditionally, but
        // does NOT touch glViewport or the framebuffer binding. Restore both
        // to OUR target before flushing so nested offscreen passes (which
        // bind their own FBO and unbind to 0 on EndFrame) cannot leave us
        // flushing into the wrong target. Without this, an outer pass whose
        // BeginFrame bound FBO_A, then ran an inner pass that ended with
        // FBO=0, would Flush into the default backbuffer (or a tiny inner
        // viewport), losing every queued outer draw.
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
            double worldLeft = bounds.X + _transform.M31;
            double worldTop = bounds.Y + _transform.M32;
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

        if (!_textCache.TryGet(key, out var entry))
        {
            var bmp = OpenGLTextRasterizer.Rasterize(
                _frameSession.Hdc, gdiFont, text, widthPx, heightPx, color,
                horizontalAlignment, verticalAlignment, wrapping, trimming);
            entry = _textCache.CreateImage(key, ref bmp);
        }

        if (entry.ImageId == 0) return;

        var drawRect = new Rect(drawX, drawY, widthDip, heightDip);
        var srcRect = new Rect(entry.X, entry.Y, entry.WidthPx, entry.HeightPx);
        DrawImagePattern(entry.ImageId, drawRect, alpha: 1f, sourceRect: srcRect, entry.AtlasWidthPx, entry.AtlasHeightPx);
    }

    private GdiMeasurementContext EnsureMeasureContext()
        => _measureContext ??= new GdiMeasurementContext(User32.GetDC(0), (uint)Math.Round(DpiScale * 96));

    private Size MeasureTextCore(ReadOnlySpan<char> text, IFont font)
        => EnsureMeasureContext().MeasureText(text, font);

    private Size MeasureTextCore(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => EnsureMeasureContext().MeasureText(text, font, maxWidth);

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
            double worldLeft = bounds.X + _transform.M31;
            double worldTop = bounds.Y + _transform.M32;
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

        if (!_textCache.TryGet(key, out var entry))
        {
            var bmp = OpenGLTextRasterizer.Rasterize(
                _frameSession.Hdc,
                gdiFont,
                text,
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
        // is the real signal — cross-API pointer collisions (a Metal device pointer matching a
        // WGL HGLRC) don't happen in practice, so a typed-backend discriminator isn't needed.
        if (lease is not IGpuResourceAffinityProvider affinityProvider ||
            affinityProvider.Affinity?.Device is not { } sourceDevice ||
            sourceDevice.IsEmpty)
        {
            return true;
        }

        nint currentContext = OpenGL32.wglGetCurrentContext();
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

    private interface IWin32FrameSession
    {
        NanoVGGL Vg { get; }
        MewVGTextCache TextCache { get; }
        nint Hdc { get; }
        nint OpenGLShareGroup { get; }
        void BeginFrame();
        void BindFrameTarget();
        void EndFrame();
        void AbortFrame();
        void DisposeContext(MewVGWin32GraphicsContext context);
    }

    private sealed class WindowBackbufferFrameSession : IWin32FrameSession
    {
        private readonly MewVGWin32WindowResources _resources;
        private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider;
        private nint _hwnd;
        private nint _hdc;

        public WindowBackbufferFrameSession(MewVGWin32WindowResources resources, IMewVGOffscreenSurfaceProvider offscreenProvider, nint hwnd, nint hdc)
        {
            _resources = resources;
            _offscreenProvider = offscreenProvider;
            _hwnd = hwnd;
            _hdc = hdc;
        }

        public NanoVGGL Vg => _resources.Vg;
        public MewVGTextCache TextCache => _resources.TextCache;
        public nint Hdc => _hdc;
        public nint OpenGLShareGroup => _resources.OpenGLShareGroup;

        public void SetTarget(nint hwnd, nint hdc)
        {
            _hwnd = hwnd;
            _hdc = hdc;
        }

        public void BeginFrame()
        {
            _resources.MakeCurrent(_hdc);
        }

        public void BindFrameTarget()
            => OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);

        public void EndFrame()
        {
            // Per-NVG drain only — we mustn't touch image-ids that belong to other NVG
            // instances (e.g. worker offscreen NVGs currently mid-frame). Each NVG drains
            // its own pending bucket from its own EndFrame on the thread that owns it.
            _offscreenProvider.ReleasePendingImagesForVg(_resources.Vg);
            TextCache.ReleasePendingDeletes();
            _resources.SetSwapInterval(GetSwapInterval());
            _resources.SwapBuffers(_hdc, _hwnd);
        }

        public void AbortFrame()
        {
            _resources.ReleaseCurrent();
        }

        public void DisposeContext(MewVGWin32GraphicsContext context)
            => _resources.InvalidateCachedContext(context);
    }

    private sealed class WindowPixelSurfaceFrameSession : IWin32FrameSession
    {
        private readonly MewVGWin32WindowResources _resources;
        private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider;
        private readonly nint _hdc;
        private readonly OpenGLPixelRenderSurface _pixelSurface;

        public WindowPixelSurfaceFrameSession(MewVGWin32WindowResources resources, IMewVGOffscreenSurfaceProvider offscreenProvider, nint hwnd, nint hdc, OpenGLPixelRenderSurface pixelSurface)
        {
            _resources = resources;
            _offscreenProvider = offscreenProvider;
            _hdc = hdc;
            _pixelSurface = pixelSurface;
        }

        public NanoVGGL Vg => _resources.Vg;
        public MewVGTextCache TextCache => _resources.TextCache;
        public nint Hdc => _hdc;
        public nint OpenGLShareGroup => _resources.OpenGLShareGroup;

        public void BeginFrame()
        {
            _resources.MakeCurrent(_hdc);
            _offscreenProvider.EnterSession();
            PreparePixelSurface(_offscreenProvider, _pixelSurface);
        }

        public void BindFrameTarget()
            => OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, _pixelSurface.Fbo);

        public void EndFrame()
        {
            // Eager readback — the layered-window present path consumes pixels via
            // GetPixelSpan() **after** EndFrame returns, by which time ReleaseCurrent()
            // has dropped the GL context. A deferred RequestDeferredReadback flag would
            // then trigger glReadPixels with no current context — silently a no-op on
            // most drivers — leaving the staging DIB zero-filled and the layered window
            // fully transparent. The offscreen frame session can stay deferred (its
            // consumer reads back under the same active context).
            _pixelSurface.ReadbackFromFbo();
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
            _offscreenProvider.ReleasePendingImagesForVg(_resources.Vg);
            bool outermost = _offscreenProvider.ExitSession();
            if (outermost)
            {
                _offscreenProvider.ReleasePendingTargetsUnderCurrentContext();
            }
            TextCache.ReleasePendingDeletes();
            _resources.ReleaseCurrent();
        }

        public void AbortFrame()
        {
            _offscreenProvider.ExitSession();
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
            _resources.ReleaseCurrent();
        }

        public void DisposeContext(MewVGWin32GraphicsContext context)
        {
        }
    }

    private sealed class OffscreenPixelSurfaceFrameSession : IWin32FrameSession
    {
        private readonly MewVGGLOffscreenSurface _offscreen;
        private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider;
        private readonly OpenGLPixelRenderSurface _pixelSurface;

        public OffscreenPixelSurfaceFrameSession(
            MewVGGLOffscreenSurface offscreen,
            IMewVGOffscreenSurfaceProvider offscreenProvider,
            OpenGLPixelRenderSurface pixelSurface,
            nint hdc)
        {
            _offscreen = offscreen;
            _offscreenProvider = offscreenProvider;
            _pixelSurface = pixelSurface;
            Hdc = hdc;
        }

        public NanoVGGL Vg => _offscreen.Vg;
        public MewVGTextCache TextCache => _offscreen.TextCache;
        public nint Hdc { get; }
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
            // Deferred — Lock/CopyPixels/GetPixelSpan flush lazily. Avoids 100s of sync
            // barriers per frame when many filtered elements each create their own
            // source layer (each EndFrame here was a glReadPixels + flip + RGBA→BGRA pass).
            _pixelSurface.RequestDeferredReadback();
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
            // Drain pending image deletions queued against this offscreen NVG. We're inside
            // its EndFrame on the thread that owns it — only safe time to call DeleteImage
            // on this NVG instance without racing a concurrent BeginFrame...EndFrame on the
            // window thread.
            _offscreenProvider.ReleasePendingImagesForVg(_offscreen.Vg);
            bool outermost = _offscreenProvider.ExitSession();
            if (outermost)
            {
                // Headless / standalone offscreen rendering case (no window wrapper). All
                // NVG queues have flushed; safe to drain FBO disposals.
                _offscreenProvider.ReleasePendingTargetsUnderCurrentContext();
            }
        }

        public void AbortFrame()
        {
            _offscreenProvider.ExitSession();
            OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, 0);
        }

        public void DisposeContext(MewVGWin32GraphicsContext context)
            => _offscreenProvider.ReturnSurface(_offscreen);
    }

    private static void PreparePixelSurface(IMewVGOffscreenSurfaceProvider offscreenProvider, OpenGLPixelRenderSurface pixelSurface)
    {
        // Don't drain pending target disposals here. NVG defers its draw commands until the
        // OUTER frame's EndFrame, and the pending queue holds textures (e.g. blur scratch
        // FBOs) that prior filter passes wrapped via CreateImageFromHandle in the deferred
        // batch. Draining at offscreen-session BeginFrame deletes those textures while NVG
        // still has draw commands referencing them → samples garbage at flush time
        // (visible as horizontal stripes / wrong patches in filtered output).
        // The drain now happens after each session's EndFrame (post-NVG-flush).
        pixelSurface.InitializeFbo();
        if (!pixelSurface.IsFboInitialized || pixelSurface.Fbo == 0)
        {
            throw new PlatformNotSupportedException("OpenGL FBOs are required for Win32 pixel-surface rendering.");
        }

        // Record the HGLRC that owns the FBO/RB handles so deferred disposal can
        // route the glDeleteFramebuffers / glDeleteRenderbuffers calls back to this
        // same context (worker FBOs must not be released under the UI window context
        // — different namespaces, silent no-op + leak).
        pixelSurface.RecordCreationContext(OpenGL32.wglGetCurrentContext());

        OpenGLExt.BindFramebuffer(OpenGLExt.GL_FRAMEBUFFER, pixelSurface.Fbo);

        // Explicit colormask + stencil mask BEFORE clear: NanoVG_GL3's flush may have
        // left these in a stencil-only-pass state (alpha or stencil writes disabled).
        // glClear honors masks, so a sticky mask leaves alpha undefined / stencil
        // untouched on a freshly allocated FBO — rendering as opaque-black filter
        // results downstream when the alpha channel reads as 1 instead of 0. Setting
        // (true,true,true,true) is cheap and a hard guarantee.
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
