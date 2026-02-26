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

    private MewVGX11WindowResources(nint display, GlxOpenGLWindowResources gl, NanoVGGL vg)
    {
        _display = display;
        _gl = gl;
        Vg = vg;
        TextCache = new MewVGTextCache(vg);
    }

    public static MewVGX11WindowResources Create(nint display, nint window, X11GlxVisualInfo visualInfo)
    {
        DiagLog.Write($"MewVG X11 create: display=0x{display.ToInt64():X} window=0x{window.ToInt64():X}");

        // NanoVG uses stencil for AA and clipping; request a stencil buffer via GLX visual info.
        var gl = GlxOpenGLWindowResources.Create(display, window, visualInfo);
        gl.MakeCurrent(display);
        try
        {
            MewVGGLBootstrapX11.EnsureInitialized();
            var vg = new NanoVGGL();
            return new MewVGX11WindowResources(display, gl, vg);
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
