using Aprillz.MewUI.Platform.Linux.X11;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGX11WindowResources : IDisposable
{
    private readonly nint _display;
    private readonly GlxOpenGLWindowResources _gl;
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

    private MewVGX11WindowResources(nint display, GlxOpenGLWindowResources gl, NanoVGGL vg, nint shareContext)
    {
        _display = display;
        _gl = gl;
        Vg = vg;
        TextCache = new MewVGTextCache(vg);
        OpenGLShareGroup = shareContext != 0 ? shareContext : gl.GlxContext;
    }

    public static MewVGX11WindowResources Create(nint display, nint window, X11GlxVisualInfo visualInfo, nint shareContext = 0)
    {
        DiagLog.Write($"MewVG X11 create: display=0x{display.ToInt64():X} window=0x{window.ToInt64():X} share=0x{shareContext.ToInt64():X}");

        // NanoVG uses stencil for AA and clipping; request a stencil buffer via GLX visual info.
        // shareContext = factory's worker GLX context, so worker-rendered FBO textures are
        // sample-able from this window context (background offscreen handoff).
        var gl = GlxOpenGLWindowResources.Create(display, window, visualInfo, shareContext);
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
