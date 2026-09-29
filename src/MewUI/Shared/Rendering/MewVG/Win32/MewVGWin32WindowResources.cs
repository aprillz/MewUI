using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGWin32WindowResources : IDisposable, IMewVGWindowCacheMaintenance
{
    private readonly nint _hwnd;
    private readonly WglOpenGLWindowResources _gl;
    private readonly IMewVGOffscreenSurfaceProvider _offscreenProvider;
    private readonly Func<nint, IDisposable?> _makeCurrentElsewhere;
    private bool _disposed;

    public MewVGGL Vg { get; }

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

    private MewVGWin32WindowResources(
        nint hwnd,
        WglOpenGLWindowResources gl,
        MewVGGL vg,
        nint shareContext,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        Func<nint, IDisposable?> makeCurrentElsewhere)
    {
        _hwnd = hwnd;
        _gl = gl;
        _offscreenProvider = offscreenProvider;
        _makeCurrentElsewhere = makeCurrentElsewhere;
        Vg = vg;
        TextCache = new MewVGTextCache(vg);
        OpenGLShareGroup = shareContext != 0 ? shareContext : gl.Hglrc;
    }

    /// <summary>
    /// Creates the context for a window. <paramref name="makeCurrentElsewhere"/> makes a context
    /// current on another device context of the same pixel format, for releasing this one's objects
    /// once the window's own device context no longer takes it.
    /// </summary>
    public static MewVGWin32WindowResources Create(
        nint hwnd,
        nint hdc,
        nint shareContext,
        IMewVGOffscreenSurfaceProvider offscreenProvider,
        Func<nint, IDisposable?> makeCurrentElsewhere)
    {
        // MewVG renders without depth or stencil, so the window takes an exact color-only
        // pixel format; a driver without one fails here instead of falling back.
        var gl = WglOpenGLWindowResources.Create(hwnd, hdc,
            new WglOpenGLWindowResources.WglPixelFormatOptions(
                DepthBits: 0,
                StencilBits: 0),
            shareContext);
        gl.MakeCurrent(hdc);
        try
        {
            MewVGGLBootstrap.EnsureInitialized();

            var vg = new MewVGGL();
            return new MewVGWin32WindowResources(hwnd, gl, vg, shareContext, offscreenProvider, makeCurrentElsewhere);
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

    public void TrimCaches()
    {
        if (_disposed || _hwnd == 0)
        {
            return;
        }

        nint hdc = User32.GetDC(_hwnd);
        try
        {
            if (hdc == 0)
            {
                return;
            }

            _gl.MakeCurrent(hdc);
            TextCache.Clear();
            TextCache.ReleasePendingDeletes();
        }
        finally
        {
            _gl.ReleaseCurrent();
            if (hdc != 0)
            {
                User32.ReleaseDC(_hwnd, hdc);
            }
        }
    }

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
            IDisposable? elsewhere = null;
            try
            {
                // A window being destroyed can refuse its own context: the device context of a
                // closing layered popup reports another pixel format by then.
                bool current = hdc != 0 && _gl.TryMakeCurrent(hdc);
                if (!current)
                {
                    elsewhere = _makeCurrentElsewhere(_gl.Hglrc);
                    current = elsewhere != null;
                }

                // Queued targets made under this context are only released under it, and it goes away here.
                if (current)
                {
                    _offscreenProvider.ReleasePendingTargetsUnderCurrentContext();
                }

                TextCache.Dispose();

                if (Vg is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                if (elsewhere == null)
                {
                    _gl.ReleaseCurrent();
                }
            }
            finally
            {
                elsewhere?.Dispose();
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
