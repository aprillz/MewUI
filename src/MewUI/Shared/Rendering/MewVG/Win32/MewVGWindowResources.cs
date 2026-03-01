using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGWindowResources : IDisposable
{
    /// <summary>
    /// MSAA sample count. 1 = no MSAA, 4 or 8 for hardware multisampling.
    /// </summary>
    internal const int MsaaSampleCount = 4;

    private readonly nint _hwnd;
    private readonly WglOpenGLWindowResources _gl;
    private bool _disposed;

    public NanoVGGL Vg { get; }

    public MewVGTextCache TextCache { get; }

    public bool SupportsBgra => _gl.SupportsBgra;

    private MewVGWindowResources(nint hwnd, WglOpenGLWindowResources gl, NanoVGGL vg)
    {
        _hwnd = hwnd;
        _gl = gl;
        Vg = vg;
        TextCache = new MewVGTextCache(vg);
    }

    public static MewVGWindowResources Create(nint hwnd, nint hdc)
    {
        // NanoVG uses stencil for AA and clipping; request a stencil buffer when selecting pixel format.
        var gl = WglOpenGLWindowResources.Create(hwnd, hdc,
            new WglOpenGLWindowResources.WglPixelFormatOptions(
                PreferredMsaaSamples: MsaaSampleCount,
                DepthBits: 0,
                StencilBits: Math.Max(0, GraphicsRuntimeOptions.PreferredMewVGStencilBits)));
        gl.MakeCurrent(hdc);
        try
        {
            MewVGGLBootstrap.EnsureInitialized();

            // With MSAA enabled, disable geometry-based AA (fringe triangles are not needed).
            var flags = MsaaSampleCount > 1
                ? NVGcreateFlags.None
                : NVGcreateFlags.Antialias;
            var vg = new NanoVGGL(flags);
            return new MewVGWindowResources(hwnd, gl, vg);
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
