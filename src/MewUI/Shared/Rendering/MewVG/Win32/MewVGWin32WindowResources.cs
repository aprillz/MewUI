using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGWin32WindowResources : IDisposable
{
    private readonly nint _hwnd;
    private readonly WglOpenGLWindowResources _gl;
    private bool _disposed;

    public NanoVGGL Vg { get; }

    public MewVGTextCache TextCache { get; }

    public bool SupportsBgra => _gl.SupportsBgra;

    public nint OpenGLShareGroup { get; }

    private MewVGWin32GraphicsContext? _cachedContext;

    internal MewVGWin32GraphicsContext GetOrCreateContext(
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        nint hwnd,
        nint hdc,
        Action<GpuInteropInvalidatedEventArgs>? gpuInteropInvalidated)
    {
        var context = _cachedContext ??= MewVGWin32GraphicsContext.CreateForWindow(this, offscreenProvider, hwnd, hdc, gpuInteropInvalidated);
        context.SetWindowTarget(hwnd, hdc);
        return context;
    }

    /// <summary>
    /// Drops the cached graphics context reference. Called when the context is
    /// disposed externally (e.g. on window resize) so the next
    /// <see cref="GetOrCreateContext"/> creates a fresh instance instead of
    /// handing out the dead one; the dead context's pooled
    /// <c>_saveStack</c> has already been returned to <c>CollectionPool</c>,
    /// and reusing it would let an offscreen context Rent the same instance
    /// and share state.
    /// </summary>
    internal void InvalidateCachedContext(MewVGWin32GraphicsContext ctx)
    {
        if (ReferenceEquals(_cachedContext, ctx))
        {
            _cachedContext = null;
        }
    }

    private MewVGWin32WindowResources(nint hwnd, WglOpenGLWindowResources gl, NanoVGGL vg, nint shareContext)
    {
        _hwnd = hwnd;
        _gl = gl;
        Vg = vg;
        TextCache = new MewVGTextCache(vg);
        OpenGLShareGroup = shareContext != 0 ? shareContext : gl.Hglrc;
    }

    public static MewVGWin32WindowResources Create(nint hwnd, nint hdc, nint shareContext = 0)
    {
        // NanoVG uses stencil for AA and clipping; request a stencil buffer when selecting pixel format.
        var gl = WglOpenGLWindowResources.Create(hwnd, hdc,
            new WglOpenGLWindowResources.WglPixelFormatOptions(
                DepthBits: 0,
                StencilBits: 8),
            shareContext);
        gl.MakeCurrent(hdc);
        try
        {
            MewVGGLBootstrap.EnsureInitialized();

            var vg = new NanoVGGL(NVGcreateFlags.Antialias);
            return new MewVGWin32WindowResources(hwnd, gl, vg, shareContext);
        }
        finally
        {
            gl.ReleaseCurrent();
        }
    }

    public void MakeCurrent(nint hdc) => _gl.MakeCurrent(hdc);

    public void ReleaseCurrent() => _gl.ReleaseCurrent();

    public void SwapBuffers(nint hdc, nint hwnd) => _gl.SwapBuffers(hdc, hwnd);

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

        if (_hwnd != 0)
        {
            nint hdc = User32.GetDC(_hwnd);
            try
            {
                if (hdc != 0)
                {
                    _gl.MakeCurrent(hdc);
                }

                TextCache.Dispose();

                if (Vg is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                _gl.ReleaseCurrent();
            }
            finally
            {
                if (hdc != 0)
                {
                    User32.ReleaseDC(_hwnd, hdc);
                }
            }
        }
        else
        {
            TextCache.Dispose();

            if (Vg is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _gl.Dispose();
    }
}
