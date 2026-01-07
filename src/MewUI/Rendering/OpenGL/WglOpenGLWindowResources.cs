using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed class WglOpenGLWindowResources : IOpenGLWindowResources
{
    private readonly nint _hwnd;
    private bool _disposed;

    public nint Hglrc { get; }
    public bool SupportsBgra { get; }
    public OpenGLTextCache TextCache { get; } = new();

    private WglOpenGLWindowResources(nint hwnd, nint hglrc, bool supportsBgra)
    {
        _hwnd = hwnd;
        Hglrc = hglrc;
        SupportsBgra = supportsBgra;
    }

    public static WglOpenGLWindowResources Create(nint hwnd, nint hdc)
    {
        var pfd = PIXELFORMATDESCRIPTOR.CreateOpenGlDoubleBuffered();
        int pixelFormat = Gdi32.ChoosePixelFormat(hdc, ref pfd);
        if (pixelFormat == 0)
            throw new InvalidOperationException($"ChoosePixelFormat failed: {Marshal.GetLastWin32Error()}");

        if (!Gdi32.SetPixelFormat(hdc, pixelFormat, ref pfd))
            throw new InvalidOperationException($"SetPixelFormat failed: {Marshal.GetLastWin32Error()}");

        nint hglrc = OpenGL32.wglCreateContext(hdc);
        if (hglrc == 0)
            throw new InvalidOperationException($"wglCreateContext failed: {Marshal.GetLastWin32Error()}");

        if (!OpenGL32.wglMakeCurrent(hdc, hglrc))
            throw new InvalidOperationException($"wglMakeCurrent failed: {Marshal.GetLastWin32Error()}");

        bool supportsBgra = DetectBgraSupport();

        // Baseline state for 2D.
        GL.Disable(0x0B71 /* GL_DEPTH_TEST */);
        GL.Disable(0x0B44 /* GL_CULL_FACE */);
        GL.Enable(GL.GL_BLEND);
        GL.BlendFunc(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA);
        GL.Enable(GL.GL_TEXTURE_2D);
        GL.Enable(GL.GL_LINE_SMOOTH);
        GL.Hint(GL.GL_LINE_SMOOTH_HINT, GL.GL_NICEST);

        OpenGL32.wglMakeCurrent(0, 0);

        return new WglOpenGLWindowResources(hwnd, hglrc, supportsBgra);
    }

    private static bool DetectBgraSupport()
    {
        string? extensions = GL.GetExtensions();
        return !string.IsNullOrEmpty(extensions) &&
               extensions.Contains("GL_EXT_bgra", StringComparison.OrdinalIgnoreCase);
    }

    public void MakeCurrent(nint deviceOrDisplay)
    {
        if (_disposed)
            return;
        OpenGL32.wglMakeCurrent(deviceOrDisplay, Hglrc);
    }

    public void ReleaseCurrent() => OpenGL32.wglMakeCurrent(0, 0);

    public void SwapBuffers(nint deviceOrDisplay, nint nativeWindow)
        => Gdi32.SwapBuffers(deviceOrDisplay);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_hwnd == 0 || Hglrc == 0)
            return;

        nint hdc = User32.GetDC(_hwnd);
        try
        {
            MakeCurrent(hdc);
            TextCache.Dispose();
            ReleaseCurrent();
        }
        finally
        {
            if (hdc != 0)
                User32.ReleaseDC(_hwnd, hdc);
        }

        OpenGL32.wglDeleteContext(Hglrc);
    }
}

