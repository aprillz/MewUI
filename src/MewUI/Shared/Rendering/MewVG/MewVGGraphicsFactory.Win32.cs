using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.MewVG;

public sealed partial class MewVGGraphicsFactory
{
    private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider =
        new MewVGGlOffscreenSurfaceProvider(OpenGL32.wglGetCurrentContext, "MewVG Win32");

#pragma warning disable CS0649
    [ThreadStatic] private static nint _pixelSurfacePresentHwnd;
    [ThreadStatic] private static nint _pixelSurfacePresentHdc;
    /// <summary>
    /// The specific pixel surface that the layered-window present is rendering
    /// INTO. Set by <see cref="MewVGWin32LayeredPresenter.Present"/>. Distinguishes
    /// the window's primary draw target from any scratch FBO that a nested filter
    /// or pattern may create during the same render pass; those scratch
    /// targets must not share the layered window's NVG instance, otherwise
    /// their <c>BeginFrame</c> wipes out main's accumulated draw commands.
    /// </summary>
    [ThreadStatic] private static OpenGLPixelRenderSurface? _pixelSurfacePresentTarget;
#pragma warning restore CS0649
    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    private MewVGWin32LayeredPresenter LayeredPresenter => _layeredPresenterField ??= new MewVGWin32LayeredPresenter(_offscreenProvider, () => SharedWorkerContext);
    private MewVGWin32LayeredPresenter? _layeredPresenterField;

    private partial IFont CreateFontCore(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        uint dpi = DpiHelper.GetSystemDpi();
        family = ResolveWin32FontFamilyOrFile(family);
        return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
    }

    private partial IFont CreateFontCore(string family, double size, uint dpi, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        family = ResolveWin32FontFamilyOrFile(family);
        return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
    }

    private static string ResolveWin32FontFamilyOrFile(string familyOrPath)
    {
        // 1. Check FontRegistry (registered via FontResources.Register)
        var resolved = FontRegistry.Resolve(familyOrPath);
        if (resolved != null)
        {
            _ = Win32Fonts.EnsurePrivateFontFamily(resolved.Value.FilePath);
            return resolved.Value.FamilyName;
        }

        // 2. Legacy: file path directly in FontFamily
        if (!FontResources.LooksLikeFontFilePath(familyOrPath))
        {
            return familyOrPath;
        }

        var path = Path.GetFullPath(familyOrPath);
        _ = Win32Fonts.EnsurePrivateFontFamily(path);

        return FontResources.TryGetParsedFamilyName(path, out var parsed) && !string.IsNullOrWhiteSpace(parsed)
            ? parsed
            : "Segoe UI";
    }

    private partial IDisposable CreateWindowResources(IWindowSurface surface)
    {
        if (surface is not IWin32HdcWindowSurface win32 || win32.Hwnd == 0 || win32.Hdc == 0)
        {
            throw new ArgumentException("MewVG (Win32) requires a Win32 HDC window surface.", nameof(surface));
        }

        // Share textures/buffers with the worker context so background offscreen render
        // tasks (Task.Run) can hand off FBO textures to this window for sampling.
        return MewVGWindowResources.Create(win32.Hwnd, win32.Hdc, SharedWorkerContext);
    }

    private partial IGraphicsContext CreateContextCore(WindowRenderTarget target, IDisposable resources)
    {
        if (target.Surface is not IWin32HdcWindowSurface win32 || win32.Hwnd == 0 || win32.Hdc == 0)
        {
            throw new ArgumentException("MewVG (Win32) requires a Win32 HDC window surface.", nameof(target));
        }

        var res = (MewVGWindowResources)resources;
        return res.GetOrCreateContext(_offscreenProvider, win32.Hwnd, win32.Hdc);
    }

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi)
        => new GdiMeasurementContext(User32.GetDC(0), dpi);

    partial void TryCreatePixelSurface(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha, ref bool handled, ref IRenderSurface? renderTarget)
    {
        if (handled)
        {
            return;
        }

        renderTarget = new OpenGLPixelRenderSurface(
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

        if (target is not OpenGLPixelRenderSurface pixelSurface)
        {
            return;
        }

        var hwnd = _pixelSurfacePresentHwnd;
        var hdc = _pixelSurfacePresentHdc;
        // Layered window's primary pixel surface is the one passed to Present;
        // anything else (filter / pattern scratch FBOs) must use the
        // single-context offscreen path so its NVG.BeginFrame doesn't reset
        // the layered window's shared NVG mid-render.
        bool isLayeredPresentTarget = hwnd != 0 && hdc != 0
            && ReferenceEquals(_pixelSurfacePresentTarget, pixelSurface);
        if (!isLayeredPresentTarget)
        {
            // Single-context offscreen: stay on whatever HGLRC is currently
            // active (main's, since we're inside main's render call) and use a
            // pooled NVG instance bound to that same context. No
            // wglMakeCurrent roundtrip occurs, so main's GL state cannot be
            // disturbed by the offscreen pass. Borrow gives this pass its own
            // NVG so nested offscreen (e.g. cache -> pattern -> filter)
            // does not have inner BeginFrame stomping outer's state.
            var offscreenResources = _offscreenProvider.AcquireSurface();
            var offscreenContext = MewVGWin32GraphicsContext.CreateForOffscreen(
                offscreenResources,
                _offscreenProvider,
                pixelSurface,
                OpenGL32.wglGetCurrentDC());
            context = offscreenContext;
            handled = true;
            return;
        }

        var resources = LayeredPresenter.GetOrCreateWindowResources(hwnd, hdc);
        // Layered pixel-surface rendering creates a fresh context per Present call so the caller
        // can Dispose() it as a one-shot. Heavy resources (NanoVGGL,
        // text cache) live on MewVGWindowResources and are reused.
        context = MewVGWin32GraphicsContext.CreateForLayeredWindow(
            resources,
            _offscreenProvider,
            hwnd,
            hdc,
            pixelSurface);
        handled = true;
    }

    partial void TryReleaseWindowResources(nint hwnd)
    {
        _layeredPresenterField?.Release(hwnd);
    }

    partial void TryPresentWindowSurface(Window window, IWindowSurface surface, double opacity, ref bool handled, ref bool result)
    {
        if (handled)
        {
            return;
        }

        if (surface is not IWin32LayeredWindowSurface win32Surface ||
            win32Surface.Hwnd == 0 ||
            surface.Kind != WindowSurfaceKind.Layered)
        {
            return;
        }

        handled = true;
        result = LayeredPresenter.Present(
            window,
            win32Surface,
            opacity,
            render: ctx =>
            {
                _pixelSurfacePresentHwnd = ctx.Hwnd;
                _pixelSurfacePresentHdc = ctx.Hdc;
                _pixelSurfacePresentTarget = ctx.RenderTarget;
                try
                {
                    window.RenderFrameToSurface(ctx.RenderTarget);
                }
                finally
                {
                    _pixelSurfacePresentTarget = null;
                    _pixelSurfacePresentHwnd = 0;
                    _pixelSurfacePresentHdc = 0;
                }
            });
    }

    partial void DisposePlatformResources()
    {
        _layeredPresenterField?.Dispose();
        _offscreenProvider.Dispose();
        DisposeWorkerContext();
    }

    // -------------------------------------------------------------------------
    // Shared worker GL context (background offscreen render support).
    //
    // A single hidden-window GL context that worker threads activate to render
    // offscreen FBOs concurrently with the UI thread. All window contexts
    // wglShareLists with this one so worker-created textures are visible to
    // the UI's window context (see WglOpenGLWindowResources.Create).
    //
    // Lazy: created on first call to SharedWorkerContext / SharedWorkerHdc.
    // Worker NVG instances are managed separately by the offscreen provider —
    // this factory only owns the HGLRC/HDC/HWND triple.
    // -------------------------------------------------------------------------

    private readonly object _workerContextLock = new();
    // Serializes worker-context activation across threads. wglMakeCurrent on a
    // shared HGLRC from multiple threads is a race: a context can only be current
    // on ONE thread at a time, and a second thread's wglMakeCurrent either fails
    // or invalidates the first thread's binding (driver-dependent). Without
    // serialization, two background rebuilds dispatched concurrently to the
    // ThreadPool intermittently produce black filter output (the second worker
    // steals the context mid-render). Held for the lifetime of each
    // <see cref="Win32WorkerContextScope"/>.
    private readonly object _workerActivationLock = new();
    private nint _workerHwnd;
    private nint _workerHdc;
    private nint _workerHglrc;
    private bool _workerInitFailed;

    /// <summary>HGLRC of the shared worker context. 0 if init failed.
    /// Window contexts pass this to <c>wglShareLists</c> at creation time.</summary>
    internal nint SharedWorkerContext
    {
        get
        {
            EnsureWorkerContext();
            return _workerHglrc;
        }
    }

    /// <summary>HDC of the worker context's hidden window. Worker threads call
    /// <c>wglMakeCurrent(SharedWorkerHdc, SharedWorkerContext)</c> to activate.</summary>
    internal nint SharedWorkerHdc
    {
        get
        {
            EnsureWorkerContext();
            return _workerHdc;
        }
    }

    private void EnsureWorkerContext()
    {
        if (_workerHglrc != 0 || _workerInitFailed)
        {
            return;
        }

        lock (_workerContextLock)
        {
            if (_workerHglrc != 0 || _workerInitFailed)
            {
                return;
            }

            nint hwnd = 0;
            nint hdc = 0;
            nint hglrc = 0;
            try
            {
                hwnd = User32.CreateWindowEx(
                    dwExStyle: 0,
                    lpClassName: "STATIC",
                    lpWindowName: string.Empty,
                    dwStyle: 0x80000000u, // WS_POPUP
                    x: 0, y: 0,
                    nWidth: 1, nHeight: 1,
                    hWndParent: 0, hMenu: 0, hInstance: 0, lpParam: 0);
                if (hwnd == 0)
                {
                    throw new InvalidOperationException(
                        $"Worker GL context: CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
                }

                hdc = User32.GetDC(hwnd);
                if (hdc == 0)
                {
                    throw new InvalidOperationException("Worker GL context: GetDC failed.");
                }

                // Pixel format must be share-compatible with window contexts. Match the
                // pfd seed that MewVGWindowResources uses (stencil bits for NanoVG AA/clip;
                // MSAA off — worker FBOs do their own AA).
                var pfd = PIXELFORMATDESCRIPTOR.CreateOpenGlDoubleBuffered();
                pfd.cStencilBits = (byte)Math.Max(0, GraphicsRuntimeOptions.PreferredMewVGStencilBits);

                int pf = Gdi32.ChoosePixelFormat(hdc, ref pfd);
                if (pf == 0)
                {
                    throw new InvalidOperationException(
                        $"Worker GL context: ChoosePixelFormat failed: {Marshal.GetLastWin32Error()}");
                }
                if (!Gdi32.SetPixelFormat(hdc, pf, ref pfd))
                {
                    throw new InvalidOperationException(
                        $"Worker GL context: SetPixelFormat failed: {Marshal.GetLastWin32Error()}");
                }

                hglrc = OpenGL32.wglCreateContext(hdc);
                if (hglrc == 0)
                {
                    throw new InvalidOperationException(
                        $"Worker GL context: wglCreateContext failed: {Marshal.GetLastWin32Error()}");
                }

                _workerHwnd = hwnd;
                _workerHdc = hdc;
                _workerHglrc = hglrc;

                if (DiagLog.Enabled)
                {
                    DiagLog.Write(
                        $"[WGL] Worker context created hwnd=0x{hwnd.ToInt64():X} " +
                        $"hdc=0x{hdc.ToInt64():X} hglrc=0x{hglrc.ToInt64():X}");
                }
            }
            catch
            {
                _workerInitFailed = true;
                if (hglrc != 0) OpenGL32.wglDeleteContext(hglrc);
                if (hdc != 0 && hwnd != 0) User32.ReleaseDC(hwnd, hdc);
                if (hwnd != 0) User32.DestroyWindow(hwnd);
                throw;
            }
        }
    }

    private void DisposeWorkerContext()
    {
        lock (_workerContextLock)
        {
            if (_workerHglrc != 0)
            {
                OpenGL32.wglDeleteContext(_workerHglrc);
                _workerHglrc = 0;
            }
            if (_workerHdc != 0 && _workerHwnd != 0)
            {
                User32.ReleaseDC(_workerHwnd, _workerHdc);
                _workerHdc = 0;
            }
            if (_workerHwnd != 0)
            {
                User32.DestroyWindow(_workerHwnd);
                _workerHwnd = 0;
            }
        }
    }

    private partial IDisposable AcquireBackgroundRenderScopeCore()
    {
        // Safety: if a context is already current on this thread (e.g. UI thread
        // re-entering during its render pass, or a re-entrant Task.Run), don't
        // unbind it. The caller can render directly on whatever context is active.
        if (OpenGL32.wglGetCurrentContext() != 0)
        {
            return MewVGNoOpRenderScope.Instance;
        }

        EnsureWorkerContext();
        if (_workerHglrc == 0)
        {
            // Init failed earlier — let the caller proceed without a worker scope.
            return MewVGNoOpRenderScope.Instance;
        }

        // Hold the activation lock for the entire scope. Two purposes:
        // 1. Worker-vs-worker: a GL context can only be current on one thread at a
        //    time, so two ThreadPool workers calling wglMakeCurrent on the shared
        //    worker HGLRC race.
        // 2. Worker-vs-UI: empirically, having the worker context current on a
        //    worker thread WHILE the UI's window context is current on the UI
        //    thread (both share-listed) corrupts the worker's filter source layer
        //    on Intel iGPU. The UI window frame sessions take this same lock for
        //    their BeginFrame → EndFrame bracket, so the two contexts are never
        //    simultaneously current on different threads.
        Monitor.Enter(_workerActivationLock);
        try
        {
            if (!OpenGL32.wglMakeCurrent(_workerHdc, _workerHglrc))
            {
                throw new InvalidOperationException(
                    $"wglMakeCurrent (worker) failed: {Marshal.GetLastWin32Error()}");
            }
        }
        catch
        {
            Monitor.Exit(_workerActivationLock);
            throw;
        }
        return new Win32WorkerContextScope(_workerActivationLock);
    }

    private sealed class Win32WorkerContextScope : IDisposable
    {
        private readonly object _activationLock;
        private bool _disposed;

        public Win32WorkerContextScope(object activationLock)
        {
            _activationLock = activationLock;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                // Block until all pending GPU work on this worker context is done so
                // the FBO texture content rendered in this scope is fully committed
                // before any UI window context (share-listed with us) samples it.
                OpenGL32.glFinish();
                OpenGL32.wglMakeCurrent(0, 0);
            }
            finally
            {
                Monitor.Exit(_activationLock);
            }
        }
    }

    /// <summary>Cross-backend entrypoint for <see cref="IGraphicsFactory.AcquireConcurrentRenderUnit"/>.
    /// No-op on the MewVG GL backend: UI ↔ worker serialization is no longer enforced at
    /// the frame-bracket level (the previous broad mutex was removed). The remaining
    /// concurrent-access concern — scratch surface pool reuse mid-flight — is handled in
    /// <c>DefaultFilterContext.AcquireScratch</c> via <c>IImage.TrySetPostReleaseCallback</c>,
    /// which defers pool return until the consumer's NVG draw queue has flushed.
    /// Worker-vs-worker MakeCurrent serialization still happens inside the worker context
    /// scope (<c>_workerActivationLock</c> in <c>AcquireBackgroundRenderScopeCore</c>).</summary>
    public IDisposable AcquireConcurrentRenderUnit() => MewVGNoOpRenderScope.Instance;

    private readonly PboFenceUploaderPool _pboPool = new();

    partial void TryCreateAsyncUploadImage(IPixelBufferSource source, ref IImage? image)
    {
        if (!PboFenceUploader.IsSupported) return;
        try
        {
            // Pool reuses the GL texture + PBO ring across short-lived sources (e.g.
            // per-frame video objects). Without pooling the per-frame alloc/free of a
            // 33 MB texture + two 33 MB PBOs dominates and makes the PBO path slower
            // than plain sync upload. ownsTexture: true routes IImage.Dispose through
            // PooledPboTexture which returns the uploader to the pool instead of
            // destroying the GL resources.
            var uploader = _pboPool.Rent(source);
            image = new MewVGExternalLockedImage(new PooledPboTexture(uploader, _pboPool), ownsTexture: true);
        }
        catch
        {
            // Async is opt-in for performance — silent fall-through to the sync path.
        }
    }
}

internal sealed class MewVGWin32LayeredPresenter : IDisposable
{
    private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider;
    private readonly Func<nint> _getShareContext;
    private readonly object _lock = new();
    private readonly Dictionary<nint, OpenGLPixelRenderSurface> _layeredTargets = new();
    private readonly Dictionary<nint, Win32LayeredBitmap> _layeredStagingTargets = new();
    private readonly Dictionary<nint, MewVGWindowResources> _layeredWindowResources = new();

    public MewVGWin32LayeredPresenter(IMewVGOffscreenSurfaceProvider offscreenProvider, Func<nint> getShareContext)
    {
        _offscreenProvider = offscreenProvider;
        _getShareContext = getShareContext;
    }

    internal readonly record struct LayeredRenderContext(nint Hwnd, nint Hdc, OpenGLPixelRenderSurface RenderTarget);

    internal bool Present(Window window, IWin32LayeredWindowSurface surface, double opacity, Action<LayeredRenderContext> render)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(render);

        var hwnd = surface.Hwnd;
        if (hwnd == 0 || surface.Kind != WindowSurfaceKind.Layered)
        {
            return false;
        }

        int w = Math.Max(1, surface.PixelWidth);
        int h = Math.Max(1, surface.PixelHeight);
        double dpiScale = surface.DpiScale <= 0 ? 1.0 : surface.DpiScale;

        // OpenGL rendering requires an HDC to make the WGL context current.
        nint hdc = User32.GetDC(hwnd);
        if (hdc == 0)
        {
            return true;
        }

        try
        {
            var glTarget = GetOrCreateLayeredTarget(hwnd, w, h, dpiScale);
            render(new LayeredRenderContext(hwnd, hdc, glTarget));

            var staging = GetOrCreateLayeredStagingTarget(hwnd, w, h, dpiScale);
            var pixelSrc = glTarget.GetPixelSpan();
            var pixelDst = staging.GetPixelSpan();
            CopyPixels(pixelSrc, pixelDst);

            // NOTE: UpdateLayeredWindow interprets pptDst as the WINDOW top-left in screen coordinates.
            // Passing ClientToScreen(0,0) will move the window every time we present (drift), because
            // client-origin != window-origin for any style with a non-client border.
            //
            // For per-pixel transparency we enforce a borderless popup window style on Win32 so
            // client-size == window-size and input/render stay aligned.
            if (!User32.GetWindowRect(hwnd, out var windowRect))
            {
                return true;
            }

            var dst = new POINT(windowRect.left, windowRect.top);
            var size = new SIZE(w, h);
            var src = new POINT(0, 0);
            byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255.0);
            var blend = BLENDFUNCTION.SourceOver(alpha);

            const uint ULW_ALPHA = 0x00000002;
            _ = User32.UpdateLayeredWindow(
                hwnd: hwnd,
                hdcDst: 0,
                pptDst: ref dst,
                psize: ref size,
                hdcSrc: staging.Hdc,
                pptSrc: ref src,
                crKey: 0,
                pblend: ref blend,
                dwFlags: ULW_ALPHA);

            return true;
        }
        finally
        {
            _ = User32.ReleaseDC(hwnd, hdc);
        }
    }

    internal MewVGWindowResources GetOrCreateWindowResources(nint hwnd, nint hdc)
    {
        if (hwnd == 0 || hdc == 0)
        {
            throw new ArgumentException("Invalid window handles.");
        }

        lock (_lock)
        {
            if (_layeredWindowResources.TryGetValue(hwnd, out var existing))
            {
                return existing;
            }

            var created = MewVGWindowResources.Create(hwnd, hdc, _getShareContext());
            _layeredWindowResources[hwnd] = created;
            return created;
        }
    }

    internal void Release(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (_layeredTargets.Remove(hwnd, out var gl))
            {
                gl.Dispose();
            }

            if (_layeredStagingTargets.Remove(hwnd, out var staging))
            {
                staging.Dispose();
            }

            if (_layeredWindowResources.Remove(hwnd, out var resources))
            {
                resources.Dispose();
            }
        }
    }

    private static void CopyPixels(ReadOnlySpan<byte> srcBgra, Span<byte> dstBgra)
    {
        if (srcBgra.Length == 0 || dstBgra.Length == 0)
        {
            return;
        }

        int byteCount = Math.Min(srcBgra.Length, dstBgra.Length);
        if (byteCount <= 0)
        {
            return;
        }

        srcBgra.Slice(0, byteCount).CopyTo(dstBgra);
    }

    private OpenGLPixelRenderSurface GetOrCreateLayeredTarget(nint hwnd, int pixelWidth, int pixelHeight, double dpiScale)
    {
        lock (_lock)
        {
            if (_layeredTargets.TryGetValue(hwnd, out var existing) &&
                existing.PixelWidth == pixelWidth &&
                existing.PixelHeight == pixelHeight &&
                Math.Abs(existing.DpiScale - dpiScale) < 0.001)
            {
                return existing;
            }

            if (_layeredTargets.Remove(hwnd, out var old))
            {
                old.Dispose();
            }

            var created = new OpenGLPixelRenderSurface(
                pixelWidth,
                pixelHeight,
                dpiScale,
                _offscreenProvider.QueueTargetDisposal);
            _layeredTargets[hwnd] = created;
            return created;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (_, target) in _layeredTargets)
            {
                target.Dispose();
            }
            _layeredTargets.Clear();

            foreach (var (_, staging) in _layeredStagingTargets)
            {
                staging.Dispose();
            }
            _layeredStagingTargets.Clear();

            foreach (var (_, resources) in _layeredWindowResources)
            {
                resources.Dispose();
            }
            _layeredWindowResources.Clear();
        }
    }

    private Win32LayeredBitmap GetOrCreateLayeredStagingTarget(nint hwnd, int pixelWidth, int pixelHeight, double dpiScale)
    {
        lock (_lock)
        {
            if (_layeredStagingTargets.TryGetValue(hwnd, out var existing) &&
                existing.PixelWidth == pixelWidth &&
                existing.PixelHeight == pixelHeight &&
                Math.Abs(existing.DpiScale - dpiScale) < 0.001)
            {
                return existing;
            }

            if (_layeredStagingTargets.Remove(hwnd, out var old))
            {
                old.Dispose();
            }

            var created = new Win32LayeredBitmap(pixelWidth, pixelHeight, dpiScale);
            _layeredStagingTargets[hwnd] = created;
            return created;
        }
    }

    private sealed class Win32LayeredBitmap : IDisposable
    {
        private readonly nint _dibSection;
        private readonly nint _oldBitmap;
        private readonly nint _dibBits;
        private bool _disposed;

        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public double DpiScale { get; }
        public nint Hdc { get; }

        public Win32LayeredBitmap(int pixelWidth, int pixelHeight, double dpiScale)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dpiScale, 0);

            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale;

            var screenDc = User32.GetDC(0);
            Hdc = Gdi32.CreateCompatibleDC(screenDc);
            _ = User32.ReleaseDC(0, screenDc);

            if (Hdc == 0)
            {
                throw new InvalidOperationException("Failed to create memory DC for layered presentation.");
            }

            var bmi = BITMAPINFO.Create32bpp(pixelWidth, pixelHeight);
            _dibSection = Gdi32.CreateDIBSection(Hdc, ref bmi, 0, out _dibBits, 0, 0);
            if (_dibSection == 0 || _dibBits == 0)
            {
                Gdi32.DeleteDC(Hdc);
                throw new InvalidOperationException("Failed to create DIB section for layered presentation.");
            }

            _oldBitmap = Gdi32.SelectObject(Hdc, _dibSection);
        }

        public unsafe Span<byte> GetPixelSpan()
        {
            if (_disposed || _dibBits == 0)
            {
                return Span<byte>.Empty;
            }

            return new Span<byte>((void*)_dibBits, PixelWidth * PixelHeight * 4);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_oldBitmap != 0 && Hdc != 0)
            {
                Gdi32.SelectObject(Hdc, _oldBitmap);
            }

            if (_dibSection != 0)
            {
                Gdi32.DeleteObject(_dibSection);
            }

            if (Hdc != 0)
            {
                Gdi32.DeleteDC(Hdc);
            }
        }
    }
}
