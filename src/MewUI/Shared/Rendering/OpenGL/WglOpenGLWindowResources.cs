using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed unsafe class WglOpenGLWindowResources : IOpenGLWindowResources
{
    internal readonly record struct WglPixelFormatOptions(int DepthBits, int StencilBits);

    private readonly nint _hwnd;
    private readonly delegate* unmanaged<int, int> _swapIntervalExt;
    private int _currentSwapInterval = int.MinValue;
    private int _swapIntervalApplyCountdown;
    private readonly HashSet<uint> _textures = new();
    private bool _disposed;

    public nint Hglrc { get; }

    public nint NativeContext => Hglrc;

    public bool SupportsBgra { get; }

    public bool SupportsNpotTextures { get; }

    private WglOpenGLWindowResources(
        nint hwnd,
        nint hglrc,
        bool supportsBgra,
        bool supportsNpotTextures,
        delegate* unmanaged<int, int> swapIntervalExt)
    {
        _hwnd = hwnd;
        Hglrc = hglrc;
        SupportsBgra = supportsBgra;
        SupportsNpotTextures = supportsNpotTextures;
        _swapIntervalExt = swapIntervalExt;
    }

    public static WglOpenGLWindowResources Create(nint hwnd, nint hdc)
        => Create(hwnd, hdc, new WglPixelFormatOptions(
            DepthBits: 0,
            StencilBits: 8), shareContext: 0);

    public static WglOpenGLWindowResources Create(nint hwnd, nint hdc, WglPixelFormatOptions options)
        => Create(hwnd, hdc, options, shareContext: 0);

    /// <summary>
    /// Create a window's GL context, optionally sharing texture/buffer namespaces with
    /// <paramref name="shareContext"/> (the factory's worker HGLRC). Sharing is the
    /// foundation of the background-rebuild pipeline: a worker thread renders an SVG
    /// FBO into a texture, the UI thread samples that same texture from the window
    /// context. <c>wglShareLists</c> must be called before either context renders, so
    /// we issue it immediately after <c>wglCreateContext</c> and before
    /// <c>wglMakeCurrent</c>.
    /// </summary>
    public static WglOpenGLWindowResources Create(nint hwnd, nint hdc, WglPixelFormatOptions options, nint shareContext)
    {
        var pfd = PIXELFORMATDESCRIPTOR.CreateOpenGLDoubleBuffered();
        pfd.cDepthBits = (byte)Math.Clamp(options.DepthBits, 0, 32);
        pfd.cStencilBits = (byte)Math.Clamp(options.StencilBits, 0, 16);

        int pixelFormat = Gdi32.ChoosePixelFormat(hdc, ref pfd);
        if (pixelFormat == 0)
        {
            throw new InvalidOperationException($"ChoosePixelFormat failed: {Marshal.GetLastWin32Error()}");
        }

        if (!Gdi32.SetPixelFormat(hdc, pixelFormat, ref pfd))
        {
            throw new InvalidOperationException($"SetPixelFormat failed: {Marshal.GetLastWin32Error()}");
        }

        nint hglrc = OpenGL32.wglCreateContext(hdc);
        if (hglrc == 0)
        {
            throw new InvalidOperationException($"wglCreateContext failed: {Marshal.GetLastWin32Error()}");
        }

        // Share textures/buffers with the worker context (if provided). Must happen before either context
        // starts rendering. Failure is non-fatal: the window context still works, but background-rebuild
        // texture handoff falls back to synchronous readback.
        if (shareContext != 0)
        {
            OpenGL32.wglShareLists(shareContext, hglrc);
        }

        if (!OpenGL32.wglMakeCurrent(hdc, hglrc))
        {
            throw new InvalidOperationException($"wglMakeCurrent failed: {Marshal.GetLastWin32Error()}");
        }

        bool supportsBgra = DetectBgraSupport();
        bool supportsNpot = DetectNpotSupport();
        delegate* unmanaged<int, int> swapIntervalExt = null;
        nint swapPtr = OpenGL32.wglGetProcAddress("wglSwapIntervalEXT");
        if (swapPtr != 0)
        {
            swapIntervalExt = (delegate* unmanaged<int, int>)swapPtr;
        }

        // Baseline state for 2D.
        GL.Disable(0x0B71 /* GL_DEPTH_TEST */);
        GL.Disable(0x0B44 /* GL_CULL_FACE */);
        GL.Enable(GL.GL_BLEND);
        GL.BlendFuncSeparate(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA, GL.GL_ONE, GL.GL_ONE_MINUS_SRC_ALPHA);
        GL.Enable(GL.GL_TEXTURE_2D);
        GL.Enable(GL.GL_LINE_SMOOTH);
        GL.Hint(GL.GL_LINE_SMOOTH_HINT, GL.GL_NICEST);

        OpenGL32.wglMakeCurrent(0, 0);

        return new WglOpenGLWindowResources(hwnd, hglrc, supportsBgra, supportsNpot, swapIntervalExt);
    }

    private static bool DetectBgraSupport()
    {
        string? extensions = GL.GetExtensions();
        return !string.IsNullOrEmpty(extensions) &&
               extensions.Contains("GL_EXT_bgra", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DetectNpotSupport()
    {
        // Full NPOT (including mipmaps) is core in OpenGL 2.0+ and is also available via ARB extension.
        // Avoid treating ES-style "OES_texture_npot" as full NPOT on desktop; it can be restricted.
        if (TryGetMajorMinor(GL.GetVersionString(), out int major, out int minor))
        {
            if (major > 2 || (major == 2 && minor >= 0))
            {
                return true;
            }
        }

        string? extensions = GL.GetExtensions();
        return !string.IsNullOrEmpty(extensions) &&
               extensions.Contains("GL_ARB_texture_non_power_of_two", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetMajorMinor(string? version, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        // Common formats:
        // - "4.6.0 NVIDIA 551.61"
        // - "1.1.0"
        // - "OpenGL ES 3.2 ..."
        int i = 0;
        while (i < version.Length && !(char.IsDigit(version[i])))
        {
            i++;
        }

        int dot = version.IndexOf('.', i);
        if (dot <= i)
        {
            return false;
        }

        int j = dot + 1;
        while (j < version.Length && char.IsDigit(version[j]))
        {
            j++;
        }

        if (!int.TryParse(version.AsSpan(i, dot - i), out major))
        {
            return false;
        }

        if (!int.TryParse(version.AsSpan(dot + 1, j - (dot + 1)), out minor))
        {
            minor = 0;
        }

        return true;
    }

    public void MakeCurrent(nint deviceOrDisplay)
    {
        if (_disposed)
        {
            return;
        }

        if (OpenGL32.wglGetCurrentContext() == Hglrc &&
            OpenGL32.wglGetCurrentDC() == deviceOrDisplay)
        {
            return;
        }

        OpenGL32.wglMakeCurrent(deviceOrDisplay, Hglrc);
    }

    public void ReleaseCurrent()
    {
        OpenGL32.wglMakeCurrent(0, 0);
    }

    public void SwapBuffers(nint deviceOrDisplay, nint nativeWindow)
    {
        _ = Gdi32.SwapBuffers(deviceOrDisplay);
    }

    public void SetSwapInterval(int interval)
    {
        if (_disposed)
        {
            return;
        }

        if (_swapIntervalExt == null)
        {
            return;
        }

        if (_currentSwapInterval == interval && _swapIntervalApplyCountdown > 0)
        {
            _swapIntervalApplyCountdown--;
            return;
        }

        int result = _swapIntervalExt(interval);
        if (result != 0)
        {
            _currentSwapInterval = interval;
            _swapIntervalApplyCountdown = 60;
        }
        else
        {
            _currentSwapInterval = int.MinValue;
            _swapIntervalApplyCountdown = 0;
        }
    }

    public void TrackTexture(uint textureId)
    {
        if (textureId == 0)
        {
            return;
        }

        if (_disposed)
        {
            return;
        }

        _textures.Add(textureId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hwnd == 0 || Hglrc == 0)
        {
            return;
        }

        nint hdc = User32.GetDC(_hwnd);
        try
        {
            MakeCurrent(hdc);
            foreach (var tex in _textures)
            {
                uint t = tex;
                GL.DeleteTextures(1, ref t);
            }
            _textures.Clear();
            ReleaseCurrent();
        }
        finally
        {
            if (hdc != 0)
            {
                User32.ReleaseDC(_hwnd, hdc);
            }
        }

        OpenGL32.wglDeleteContext(Hglrc);
    }
}
