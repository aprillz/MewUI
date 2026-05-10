using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Linux.X11;
using Aprillz.MewUI.Rendering.FreeType;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.MewVG;

public sealed partial class MewVGGraphicsFactory
{
    private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider =
        new MewVGGlOffscreenSurfaceProvider(LibGL.glXGetCurrentContext, "MewVG X11");

    // -------------------------------------------------------------------------
    // Shared worker GLX context (background SVG rebuild support).
    // Mirrors the Win32 hidden-window worker context — a single GLX context
    // share-listed with all window contexts, made current on worker threads
    // for offscreen FBO rendering. _workerActivationLock now serializes only
    // worker-vs-worker MakeCurrent (a single shared GLX context can only be
    // current on one thread at a time). UI render and worker scope no longer
    // share the lock — the previous broad UI mutex was removed, with the
    // scratch RT pool reuse race instead handled at DefaultFilterContext via
    // IImage.TrySetPostReleaseCallback.
    // -------------------------------------------------------------------------

    private readonly object _workerActivationLock = new();
    private readonly object _workerCtxInitLock = new();
    private nint _workerCtx;
    // First-window display + drawable + visual — captured at first window
    // creation and reused for worker context init / activation. Single-display
    // X11 process is the assumed common case; multi-display would need per-
    // display worker contexts (not supported here).
    private nint _workerDisplay;
    private nint _workerDrawable;
    private X11GlxVisualInfo _workerVisualInfo;
    private bool _workerHasVisualInfo;
    private bool _workerInitFailed;

    /// <summary>GLXContext of the shared worker context. 0 if not yet created
    /// or init failed. Window contexts pass this as <c>shareList</c> at
    /// <c>glXCreateContext</c> so worker-created textures are visible from
    /// window contexts.</summary>
    internal nint SharedWorkerContext
    {
        get
        {
            EnsureWorkerContext();
            return _workerCtx;
        }
    }

    /// <summary>Captures the display / drawable / visual of the FIRST window
    /// created so the worker context can be initialized lazily against the same
    /// GLX visual. Called from <see cref="CreateWindowResources"/> before the
    /// window's own context is made.</summary>
    private void CaptureFirstWindowGlxInfo(nint display, nint window, X11GlxVisualInfo visualInfo)
    {
        if (_workerHasVisualInfo) return;
        lock (_workerCtxInitLock)
        {
            if (_workerHasVisualInfo) return;
            _workerDisplay = display;
            _workerDrawable = window;
            _workerVisualInfo = visualInfo;
            _workerHasVisualInfo = true;
        }
    }

    private void EnsureWorkerContext()
    {
        if (_workerCtx != 0 || _workerInitFailed) return;
        lock (_workerCtxInitLock)
        {
            if (_workerCtx != 0 || _workerInitFailed) return;
            if (!_workerHasVisualInfo)
            {
                // No window has been created yet — can't initialize without a
                // GLX visual. Caller should retry after a window exists.
                return;
            }

            try
            {
                var native = new XVisualInfo
                {
                    visual = _workerVisualInfo.Visual,
                    visualid = _workerVisualInfo.VisualId,
                    screen = _workerVisualInfo.Screen,
                    depth = _workerVisualInfo.Depth,
                    @class = _workerVisualInfo.Class,
                    red_mask = _workerVisualInfo.RedMask,
                    green_mask = _workerVisualInfo.GreenMask,
                    blue_mask = _workerVisualInfo.BlueMask,
                    colormap_size = _workerVisualInfo.ColormapSize,
                    bits_per_rgb = _workerVisualInfo.BitsPerRgb,
                };

                nint visualInfoMem = Marshal.AllocHGlobal(Marshal.SizeOf<XVisualInfo>());
                try
                {
                    Marshal.StructureToPtr(native, visualInfoMem, fDeleteOld: false);
                    // shareList = 0 — worker context is the SHARE-LIST ROOT. All
                    // window contexts created henceforth share with it.
                    nint ctx = LibGL.glXCreateContext(_workerDisplay, visualInfoMem, 0, 1);
                    if (ctx == 0)
                    {
                        throw new InvalidOperationException("Worker GLX context: glXCreateContext failed.");
                    }
                    _workerCtx = ctx;
                    DiagLog.Write($"[GLX] Worker context created ctx=0x{ctx.ToInt64():X}");
                }
                finally
                {
                    Marshal.FreeHGlobal(visualInfoMem);
                }
            }
            catch
            {
                _workerInitFailed = true;
                throw;
            }
        }
    }

    private void DisposeWorkerContext()
    {
        lock (_workerCtxInitLock)
        {
            if (_workerCtx != 0 && _workerDisplay != 0)
            {
                LibGL.glXDestroyContext(_workerDisplay, _workerCtx);
                _workerCtx = 0;
            }
        }
    }


    public IDisposable AcquireConcurrentRenderUnit() => MewVGNoOpRenderScope.Instance;

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    private partial IFont CreateFontCore(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size)); // Assume 96 dpi.
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
    }

    private partial IFont CreateFontCore(string family, double size, uint dpi, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero));
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
    }

    private partial IDisposable CreateWindowResources(IWindowSurface surface)
    {
        if (surface is not IX11GlxWindowSurface glx)
        {
            throw new ArgumentException("MewVG (X11) requires an X11 GLX window surface.", nameof(surface));
        }

        // Capture display / drawable / visual for lazy worker-context creation
        // and ensure the worker context exists BEFORE this window's context, so
        // we can pass the worker context as shareList. Window contexts created
        // before any worker scope can still proceed (no share); only background
        // rendering benefits from sharing.
        CaptureFirstWindowGlxInfo(glx.Display, glx.Window, glx.VisualInfo);
        EnsureWorkerContext();

        return MewVGX11WindowResources.Create(glx.Display, glx.Window, glx.VisualInfo, _workerCtx);
    }

    private partial IGraphicsContext CreateContextCore(WindowRenderTarget target, IDisposable resources)
    {
        if (target.Surface is not IX11GlxWindowSurface glx)
        {
            throw new ArgumentException("MewVG (X11) requires an X11 GLX window surface.", nameof(target));
        }

        var res = (MewVGX11WindowResources)resources;
        var ctx = res.GetOrCreateContext(_offscreenProvider);
        ctx.SetTarget(glx.Display, glx.Window);
        return ctx;
    }

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi)
        => new OpenGLMeasurementContext(dpi);

    partial void TryCreateBitmapSurfaceTarget(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha, ref bool handled, ref IRenderSurface? renderTarget)
    {
        if (handled)
        {
            return;
        }

        renderTarget = new OpenGLBitmapRenderTarget(
            pixelWidth,
            pixelHeight,
            dpiScale,
            _offscreenProvider.QueueTargetDisposal,
            hasAlpha);
        handled = true;
    }

    partial void TryGetImageDisposeHandler(ref Action<MewVGImage>? handler)
        => handler ??= _offscreenProvider.QueueImageDisposal;

    partial void TryCreateImageFilterExecutor(ref Filters.IImageFilterExecutor? executor)
        => executor ??= new OpenGLImageFilterExecutor();

    partial void TryCreateContextForTarget(IRenderTarget target, ref bool handled, ref IGraphicsContext? context)
    {
        if (handled)
        {
            return;
        }

        if (target is not OpenGLBitmapRenderTarget bitmapTarget)
        {
            return;
        }

        var offscreenResources = _offscreenProvider.AcquireSurface();
        context = MewVGX11GraphicsContext.CreateForOffscreen(
            offscreenResources,
            _offscreenProvider,
            bitmapTarget);
        handled = true;
    }

    partial void DisposePlatformResources()
    {
        _offscreenProvider.Dispose();
        DisposeWorkerContext();
    }

    private partial IDisposable AcquireBackgroundRenderScopeCore()
    {
        // Safety: if a context is already current on this thread (UI thread
        // re-entering during render, or re-entrant Task.Run), don't disturb it.
        if (LibGL.glXGetCurrentContext() != 0)
        {
            return MewVGNoOpRenderScope.Instance;
        }

        EnsureWorkerContext();
        if (_workerCtx == 0)
        {
            // Worker context isn't available yet (no window has been created, so
            // we don't have a GLX visual). Caller proceeds without a worker scope
            // — they'll fail to CreateContext on this thread, which their try /
            // catch handles by skipping the rebuild.
            return MewVGNoOpRenderScope.Instance;
        }

        // Same broad mutex as Win32: worker scope ↔ UI window frame fully
        // serialize. UI freezes for the duration of any worker rebuild —
        // accepted in favor of correctness on share-listed GLX contexts.
        Monitor.Enter(_workerActivationLock);
        try
        {
            // Worker context renders only into FBOs, so the drawable just needs
            // to be GLX-compatible — reusing the first window's drawable is
            // safe and avoids the Pbuffer setup boilerplate.
            if (!LibGL.glXMakeCurrent(_workerDisplay, _workerDrawable, _workerCtx))
            {
                throw new InvalidOperationException("glXMakeCurrent (worker) failed.");
            }
        }
        catch
        {
            Monitor.Exit(_workerActivationLock);
            throw;
        }
        return new X11WorkerContextScope(_workerActivationLock, _workerDisplay);
    }

    private sealed class X11WorkerContextScope : IDisposable
    {
        private readonly object _activationLock;
        private readonly nint _display;
        private bool _disposed;

        public X11WorkerContextScope(object activationLock, nint display)
        {
            _activationLock = activationLock;
            _display = display;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                // Block until all pending GPU work on the worker context completes
                // so FBO texture content is fully committed before any UI window
                // context (share-listed with us) samples it.
                LibGL.glFinish();
                LibGL.glXMakeCurrent(_display, 0, 0);
            }
            finally
            {
                Monitor.Exit(_activationLock);
            }
        }
    }

    private readonly PboFenceUploaderPool _pboPool = new();

    partial void TryCreateAsyncUploadImage(IPixelBufferSource source, ref IImage? image)
    {
        if (!PboFenceUploader.IsSupported) return;
        try
        {
            // See Win32 partial for the pooling rationale — same applies on X11/GLX.
            var uploader = _pboPool.Rent(source);
            image = new MewVGExternalLockedImage(new PooledPboTexture(uploader, _pboPool), ownsTexture: true);
        }
        catch
        {
            // Async is opt-in for performance — silent fall-through to the sync path.
        }
    }
}
