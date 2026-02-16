using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed unsafe class WglOpenGLWindowResources : IOpenGLWindowResources
{
    internal readonly record struct WglPixelFormatOptions(int PreferredMsaaSamples, int DepthBits, int StencilBits);

    private readonly nint _hwnd;
    private readonly delegate* unmanaged<int, int> _swapIntervalExt;
    private int _currentSwapInterval = int.MinValue;
    private readonly HashSet<uint> _textures = new();
    private bool _disposed;

    public nint Hglrc { get; }
    public bool SupportsBgra { get; }
    public bool SupportsNpotTextures { get; }
    public OpenGLTextCache TextCache { get; } = new();

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

    private const int WGL_DRAW_TO_WINDOW_ARB = 0x2001;
    private const int WGL_SUPPORT_OPENGL_ARB = 0x2010;
    private const int WGL_DOUBLE_BUFFER_ARB = 0x2011;
    private const int WGL_PIXEL_TYPE_ARB = 0x2013;
    private const int WGL_COLOR_BITS_ARB = 0x2014;
    private const int WGL_ALPHA_BITS_ARB = 0x201B;
    private const int WGL_DEPTH_BITS_ARB = 0x2022;
    private const int WGL_STENCIL_BITS_ARB = 0x2023;
    private const int WGL_TYPE_RGBA_ARB = 0x202B;
    private const int WGL_SAMPLE_BUFFERS_ARB = 0x2041;
    private const int WGL_SAMPLES_ARB = 0x2042;

    public static WglOpenGLWindowResources Create(nint hwnd, nint hdc)
        => Create(hwnd, hdc, new WglPixelFormatOptions(
            PreferredMsaaSamples: Math.Max(0, 4),
            DepthBits: 0,
            StencilBits: 0));

    public static WglOpenGLWindowResources Create(nint hwnd, nint hdc, WglPixelFormatOptions options)
    {
        var pfd = PIXELFORMATDESCRIPTOR.CreateOpenGlDoubleBuffered();
        pfd.cDepthBits = (byte)Math.Clamp(options.DepthBits, 0, 32);
        pfd.cStencilBits = (byte)Math.Clamp(options.StencilBits, 0, 16);

        int preferredSamples = Math.Max(0, options.PreferredMsaaSamples);
        bool choseMultisample = false;

        int pixelFormat = 0;

        // Try MSAA first (reduces jaggies on filled round-rects and triangles).
        if (preferredSamples > 1 &&
            !TryChooseMultisamplePixelFormat(
                hdc,
                preferredSamples: preferredSamples,
                depthBits: options.DepthBits,
                stencilBits: options.StencilBits,
                out pixelFormat,
                out pfd))
        {
            pixelFormat = Gdi32.ChoosePixelFormat(hdc, ref pfd);
            if (pixelFormat == 0)
            {
                throw new InvalidOperationException($"ChoosePixelFormat failed: {Marshal.GetLastWin32Error()}");
            }
        }
        else if (preferredSamples > 1 && pixelFormat != 0)
        {
            choseMultisample = true;
        }
        else if (pixelFormat == 0)
        {
            pixelFormat = Gdi32.ChoosePixelFormat(hdc, ref pfd);
            if (pixelFormat == 0)
            {
                throw new InvalidOperationException($"ChoosePixelFormat failed: {Marshal.GetLastWin32Error()}");
            }
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

        if (DiagLog.Enabled)
        {
            // Note: chosen pixel format attributes (especially sample count) can vary by driver.
            int sampleBuffers = GL.GetInteger(GL.GL_SAMPLE_BUFFERS);
            int samples = GL.GetInteger(GL.GL_SAMPLES);
            string? vendor = GL.GetVendorString();
            string? renderer = GL.GetRendererString();
            string? version = GL.GetVersionString();
            DiagLog.WriteAlways(
                $"[WGL] hwnd=0x{hwnd.ToInt64():X} msaaPreferred={preferredSamples} choseMsaaPf={choseMultisample} depthBits={options.DepthBits} stencilBits={options.StencilBits} " +
                $"pfd(color={pfd.cColorBits} alpha={pfd.cAlphaBits} depth={pfd.cDepthBits} stencil={pfd.cStencilBits}) " +
                $"gl(samplesBuf={sampleBuffers} samples={samples}) '{vendor}' '{renderer}' '{version}'");
            DiagLog.WriteProcessMemory("after WGL context create");
        }

        // Baseline state for 2D.
        GL.Disable(0x0B71 /* GL_DEPTH_TEST */);
        GL.Disable(0x0B44 /* GL_CULL_FACE */);
        GL.Enable(GL.GL_BLEND);
        GL.BlendFuncSeparate(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA, GL.GL_ONE, GL.GL_ONE_MINUS_SRC_ALPHA);
        GL.Enable(GL.GL_TEXTURE_2D);
        if (preferredSamples > 1)
        {
            GL.Enable(GL.GL_MULTISAMPLE);
        }
        GL.Enable(GL.GL_LINE_SMOOTH);
        GL.Hint(GL.GL_LINE_SMOOTH_HINT, GL.GL_NICEST);

        OpenGL32.wglMakeCurrent(0, 0);

        return new WglOpenGLWindowResources(hwnd, hglrc, supportsBgra, supportsNpot, swapIntervalExt);
    }

    private static unsafe bool TryChooseMultisamplePixelFormat(
        nint targetHdc,
        int preferredSamples,
        int depthBits,
        int stencilBits,
        out int pixelFormat,
        out PIXELFORMATDESCRIPTOR describedPfd)
    {
        pixelFormat = 0;
        describedPfd = default;

        var choose = GetWglChoosePixelFormatArb();
        if (choose == null)
        {
            return false;
        }

        // Try a small descending set of sample counts.
        Span<int> samplesToTry = stackalloc int[] { preferredSamples, 8, 4, 2 };
        for (int i = 0; i < samplesToTry.Length; i++)
        {
            int samples = samplesToTry[i];
            if (samples <= 1)
            {
                continue;
            }

            Span<int> attribs = stackalloc int[]
            {
                WGL_DRAW_TO_WINDOW_ARB, 1,
                WGL_SUPPORT_OPENGL_ARB, 1,
                WGL_DOUBLE_BUFFER_ARB, 1,
                WGL_PIXEL_TYPE_ARB, WGL_TYPE_RGBA_ARB,
                WGL_COLOR_BITS_ARB, 32,
                WGL_ALPHA_BITS_ARB, 8,
                WGL_DEPTH_BITS_ARB, Math.Max(0, depthBits),
                WGL_STENCIL_BITS_ARB, Math.Max(0, stencilBits),
                WGL_SAMPLE_BUFFERS_ARB, 1,
                WGL_SAMPLES_ARB, samples,
                0, 0
            };

            int pf;
            uint num;
            fixed (int* pAttribs = attribs)
            {
                pf = 0;
                num = 0;
                int outPf = 0;
                uint outNum = 0;
                int ok = choose(targetHdc, pAttribs, null, 1, &outPf, &outNum);
                if (ok == 0 || outNum == 0 || outPf == 0)
                {
                    continue;
                }

                pf = outPf;
                num = outNum;
            }

            var pfd = default(PIXELFORMATDESCRIPTOR);
            int described = Gdi32.DescribePixelFormat(
                targetHdc,
                pf,
                (uint)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
                ref pfd);
            if (described == 0)
            {
                continue;
            }

            pixelFormat = pf;
            describedPfd = pfd;
            return true;
        }

        return false;
    }

    private static unsafe delegate* unmanaged<nint, int*, float*, uint, int*, uint*, int> GetWglChoosePixelFormatArb()
    {
        nint hwnd = 0;
        nint hdc = 0;
        nint hglrc = 0;

        try
        {
            // Create a tiny dummy window so we can load WGL extensions without touching the target HDC.
            hwnd = User32.CreateWindowEx(
                dwExStyle: 0,
                lpClassName: "STATIC",
                lpWindowName: string.Empty,
                dwStyle: 0x80000000u, // WS_POPUP
                x: 0,
                y: 0,
                nWidth: 1,
                nHeight: 1,
                hWndParent: 0,
                hMenu: 0,
                hInstance: 0,
                lpParam: 0);

            if (hwnd == 0)
            {
                return null;
            }

            hdc = User32.GetDC(hwnd);
            if (hdc == 0)
            {
                return null;
            }

            var pfd = PIXELFORMATDESCRIPTOR.CreateOpenGlDoubleBuffered();
            int pixelFormat = Gdi32.ChoosePixelFormat(hdc, ref pfd);
            if (pixelFormat == 0)
            {
                return null;
            }

            if (!Gdi32.SetPixelFormat(hdc, pixelFormat, ref pfd))
            {
                return null;
            }

            hglrc = OpenGL32.wglCreateContext(hdc);
            if (hglrc == 0)
            {
                return null;
            }

            if (!OpenGL32.wglMakeCurrent(hdc, hglrc))
            {
                return null;
            }

            nint p = OpenGL32.wglGetProcAddress("wglChoosePixelFormatARB");
            if (p == 0)
            {
                return null;
            }

            return (delegate* unmanaged<nint, int*, float*, uint, int*, uint*, int>)p;
        }
        finally
        {
            if (hdc != 0 && hglrc != 0)
            {
                OpenGL32.wglMakeCurrent(0, 0);
            }

            if (hglrc != 0)
            {
                OpenGL32.wglDeleteContext(hglrc);
            }

            if (hdc != 0 && hwnd != 0)
            {
                User32.ReleaseDC(hwnd, hdc);
            }

            if (hwnd != 0)
            {
                User32.DestroyWindow(hwnd);
            }
        }
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

        OpenGL32.wglMakeCurrent(deviceOrDisplay, Hglrc);
    }

    public void ReleaseCurrent() => OpenGL32.wglMakeCurrent(0, 0);

    public void SwapBuffers(nint deviceOrDisplay, nint nativeWindow)
        => Gdi32.SwapBuffers(deviceOrDisplay);

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

        if (_currentSwapInterval == interval)
        {
            return;
        }

        _swapIntervalExt(interval);
        _currentSwapInterval = interval;
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
            TextCache.Dispose();
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
