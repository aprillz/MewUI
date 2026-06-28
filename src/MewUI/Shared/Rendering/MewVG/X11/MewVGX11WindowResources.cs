using Aprillz.MewUI.Platform.Linux.X11;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGX11WindowResources : IDisposable
{
    private readonly nint _display;
    private readonly IOpenGLWindowResources _gl;
    private bool _disposed;

    public NanoVGGL Vg { get; }

    public MewVGTextCache TextCache { get; }

    public bool SupportsBgra => _gl.SupportsBgra;

    public nint OpenGLShareGroup { get; }

    private MewVGX11GraphicsContext? _cachedContext;

    internal MewVGX11GraphicsContext GetOrCreateContext(
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
        => _cachedContext ??= MewVGX11GraphicsContext.CreateForWindow(this, offscreenProvider, gpuInteropInvalidated);

    internal void InvalidateCachedContext(MewVGX11GraphicsContext context)
    {
        if (ReferenceEquals(_cachedContext, context))
        {
            _cachedContext = null;
        }
    }

    private MewVGX11WindowResources(nint display, IOpenGLWindowResources gl, NanoVGGL vg, nint shareContext)
    {
        _display = display;
        _gl = gl;
        Vg = vg;
        TextCache = new MewVGTextCache(vg);
        OpenGLShareGroup = shareContext != 0 ? shareContext : gl.NativeContext;
    }

    public static MewVGX11WindowResources Create(nint display, nint window, X11GLVisualInfo visualInfo, nint shareContext = 0)
    {
        DiagLog.Write($"MewVG X11 create: display=0x{display.ToInt64():X} window=0x{window.ToInt64():X} share=0x{shareContext.ToInt64():X} backend={X11GLBackendRegistry.Current?.GetType().Name}");

        // NanoVG uses stencil for AA and clipping; request a stencil buffer via the visual info.
        // shareContext = factory's worker context, so worker-rendered FBO textures are sample-able
        // from this window context (background offscreen handoff). The active backend (GLX by
        // default, EGL when selected for dma_buf/EGLImage zero-copy) creates the window context.
        IOpenGLWindowResources gl = X11GLBackendRegistry.Current!.CreateWindowResources(display, window, visualInfo, shareContext);
        gl.MakeCurrent(display);
        try
        {
            MewVGGLBootstrapX11.EnsureInitialized();
            var vg = new NanoVGGL(NVGcreateFlags.Antialias);
            return new MewVGX11WindowResources(display, gl, vg, shareContext);
        }
        finally
        {
            gl.ReleaseCurrent();
        }
    }

    public void MakeCurrent(nint display) => _gl.MakeCurrent(display);

    public void ReleaseCurrent() => _gl.ReleaseCurrent();

    public void SwapBuffers(nint display, nint window) => _gl.SwapBuffers(display, window);

    public void SetSwapInterval(int interval) => _gl.SetSwapInterval(interval);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _cachedContext?.Dispose();
        _cachedContext = null;

        _gl.MakeCurrent(_display);

        TextCache.Dispose();

        if (Vg is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _gl.ReleaseCurrent();
        _gl.Dispose();
    }
}
