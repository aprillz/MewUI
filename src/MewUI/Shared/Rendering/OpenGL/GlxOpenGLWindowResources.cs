using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed unsafe class GlxOpenGLWindowResources : IOpenGLWindowResources
{
    private readonly nint _display;
    private readonly nint _window;
    private readonly delegate* unmanaged<nint, nint, int, void> _swapIntervalExt;
    private readonly delegate* unmanaged<int, int> _swapIntervalMesa;
    private readonly delegate* unmanaged<int, int> _swapIntervalSgi;
    private int _currentSwapInterval = int.MinValue;
    private readonly HashSet<uint> _textures = new();
    private bool _disposed;

    public nint GlxContext { get; }
    public bool SupportsBgra { get; }
    public bool SupportsNpotTextures { get; }
    public OpenGLTextCache TextCache { get; } = new();

    private GlxOpenGLWindowResources(
        nint display,
        nint window,
        nint ctx,
        bool supportsBgra,
        bool supportsNpotTextures,
        delegate* unmanaged<nint, nint, int, void> swapIntervalExt,
        delegate* unmanaged<int, int> swapIntervalMesa,
        delegate* unmanaged<int, int> swapIntervalSgi)
    {
        _display = display;
        _window = window;
        GlxContext = ctx;
        SupportsBgra = supportsBgra;
        SupportsNpotTextures = supportsNpotTextures;
        _swapIntervalExt = swapIntervalExt;
        _swapIntervalMesa = swapIntervalMesa;
        _swapIntervalSgi = swapIntervalSgi;
    }

    public static GlxOpenGLWindowResources Create(nint display, nint window)
    {
        DiagLog.Write($"GLX create: display=0x{display.ToInt64():X} window=0x{window.ToInt64():X}");

        int screen = X11.XDefaultScreen(display);

        // Try MSAA first (matches X11WindowBackend behavior), then fall back to a minimal RGBA + double-buffer visual.
        // GLX_SAMPLE_BUFFERS / GLX_SAMPLES are from GLX_ARB_multisample.
        const int GLX_SAMPLE_BUFFERS = 100000;
        const int GLX_SAMPLES = 100001;

        int[] attribsMsaa =
        {
            4,  // GLX_RGBA
            5,  // GLX_DOUBLEBUFFER
            8,  // GLX_RED_SIZE
            8,
            9,  // GLX_GREEN_SIZE
            8,
            10, // GLX_BLUE_SIZE
            8,
            11, // GLX_ALPHA_SIZE
            8,
            GLX_SAMPLE_BUFFERS, 1,
            GLX_SAMPLES, 4,
            0   // None
        };

        nint visualInfoPtr;
        unsafe
        {
            fixed (int* p = attribsMsaa)
            {
                visualInfoPtr = LibGL.glXChooseVisual(display, screen, (nint)p);
            }
        }

        if (visualInfoPtr == 0)
        {
            int[] attribs =
            {
                4,  // GLX_RGBA
                5,  // GLX_DOUBLEBUFFER
                8,  // GLX_RED_SIZE
                8,
                9,  // GLX_GREEN_SIZE
                8,
                10, // GLX_BLUE_SIZE
                8,
                11, // GLX_ALPHA_SIZE
                8,
                0   // None
            };

            unsafe
            {
                fixed (int* p = attribs)
                {
                    visualInfoPtr = LibGL.glXChooseVisual(display, screen, (nint)p);
                }
            }

            if (visualInfoPtr == 0)
            {
                throw new InvalidOperationException("glXChooseVisual failed.");
            }
        }

        var visualInfo = Marshal.PtrToStructure<XVisualInfo>(visualInfoPtr);
        X11.XFree(visualInfoPtr);

        nint visualInfoMem = Marshal.AllocHGlobal(Marshal.SizeOf<XVisualInfo>());
        try
        {
            Marshal.StructureToPtr(visualInfo, visualInfoMem, fDeleteOld: false);

            nint ctx = LibGL.glXCreateContext(display, visualInfoMem, 0, 1);
            if (ctx == 0)
            {
                throw new InvalidOperationException("glXCreateContext failed.");
            }

            if (!LibGL.glXMakeCurrent(display, window, ctx))
            {
                throw new InvalidOperationException("glXMakeCurrent failed.");
            }

            bool supportsBgra = DetectBgraSupport();
            bool supportsNpot = DetectNpotSupport();
            delegate* unmanaged<nint, nint, int, void> swapIntervalExt = null;
            delegate* unmanaged<int, int> swapIntervalMesa = null;
            delegate* unmanaged<int, int> swapIntervalSgi = null;

            nint swapPtr = LibGL.glXGetProcAddress("glXSwapIntervalEXT");
            if (swapPtr != 0)
            {
                swapIntervalExt = (delegate* unmanaged<nint, nint, int, void>)swapPtr;
            }
            else
            {
                swapPtr = LibGL.glXGetProcAddress("glXSwapIntervalMESA");
                if (swapPtr != 0)
                {
                    swapIntervalMesa = (delegate* unmanaged<int, int>)swapPtr;
                }
                else
                {
                    swapPtr = LibGL.glXGetProcAddress("glXSwapIntervalSGI");
                    if (swapPtr != 0)
                    {
                        swapIntervalSgi = (delegate* unmanaged<int, int>)swapPtr;
                    }
                }
            }
            DiagLog.Write($"GLX context ok: ctx=0x{ctx.ToInt64():X} BGRA={supportsBgra}");

            GL.Disable(0x0B71 /* GL_DEPTH_TEST */);
            GL.Disable(0x0B44 /* GL_CULL_FACE */);
            GL.Enable(GL.GL_BLEND);
            GL.BlendFuncSeparate(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA, GL.GL_ONE, GL.GL_ONE_MINUS_SRC_ALPHA);
            GL.Enable(GL.GL_TEXTURE_2D);
            GL.Enable(GL.GL_MULTISAMPLE);
            GL.Enable(GL.GL_LINE_SMOOTH);
            GL.Hint(GL.GL_LINE_SMOOTH_HINT, GL.GL_NICEST);

            LibGL.glXMakeCurrent(display, 0, 0);

            return new GlxOpenGLWindowResources(display, window, ctx, supportsBgra, supportsNpot, swapIntervalExt, swapIntervalMesa, swapIntervalSgi);
        }
        finally
        {
            Marshal.FreeHGlobal(visualInfoMem);
        }
    }

    public static GlxOpenGLWindowResources Create(nint display, nint window, X11GlxVisualInfo visualInfo)
    {
        DiagLog.Write($"GLX create: display=0x{display.ToInt64():X} window=0x{window.ToInt64():X} (provided visual)");

        var native = new XVisualInfo
        {
            visual = visualInfo.Visual,
            visualid = visualInfo.VisualId,
            screen = visualInfo.Screen,
            depth = visualInfo.Depth,
            @class = visualInfo.Class,
            red_mask = visualInfo.RedMask,
            green_mask = visualInfo.GreenMask,
            blue_mask = visualInfo.BlueMask,
            colormap_size = visualInfo.ColormapSize,
            bits_per_rgb = visualInfo.BitsPerRgb,
        };

        nint visualInfoMem = Marshal.AllocHGlobal(Marshal.SizeOf<XVisualInfo>());
        try
        {
            Marshal.StructureToPtr(native, visualInfoMem, fDeleteOld: false);

            nint ctx = LibGL.glXCreateContext(display, visualInfoMem, 0, 1);
            if (ctx == 0)
            {
                throw new InvalidOperationException("glXCreateContext failed.");
            }

            if (!LibGL.glXMakeCurrent(display, window, ctx))
            {
                throw new InvalidOperationException("glXMakeCurrent failed.");
            }

            bool supportsBgra = DetectBgraSupport();
            bool supportsNpot = DetectNpotSupport();
            delegate* unmanaged<nint, nint, int, void> swapIntervalExt = null;
            delegate* unmanaged<int, int> swapIntervalMesa = null;
            delegate* unmanaged<int, int> swapIntervalSgi = null;

            nint swapPtr = LibGL.glXGetProcAddress("glXSwapIntervalEXT");
            if (swapPtr != 0)
            {
                swapIntervalExt = (delegate* unmanaged<nint, nint, int, void>)swapPtr;
            }
            else
            {
                swapPtr = LibGL.glXGetProcAddress("glXSwapIntervalMESA");
                if (swapPtr != 0)
                {
                    swapIntervalMesa = (delegate* unmanaged<int, int>)swapPtr;
                }
                else
                {
                    swapPtr = LibGL.glXGetProcAddress("glXSwapIntervalSGI");
                    if (swapPtr != 0)
                    {
                        swapIntervalSgi = (delegate* unmanaged<int, int>)swapPtr;
                    }
                }
            }

            return new GlxOpenGLWindowResources(
                display,
                window,
                ctx,
                supportsBgra,
                supportsNpot,
                swapIntervalExt,
                swapIntervalMesa,
                swapIntervalSgi);
        }
        finally
        {
            Marshal.FreeHGlobal(visualInfoMem);
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

        LibGL.glXMakeCurrent(deviceOrDisplay, _window, GlxContext);
    }

    public void ReleaseCurrent() => LibGL.glXMakeCurrent(_display, 0, 0);

    public void SwapBuffers(nint deviceOrDisplay, nint nativeWindow)
        => LibGL.glXSwapBuffers(deviceOrDisplay, nativeWindow);

    public void SetSwapInterval(int interval)
    {
        if (_disposed)
        {
            return;
        }

        if (_currentSwapInterval == interval)
        {
            return;
        }

        if (_swapIntervalExt != null)
        {
            _swapIntervalExt(_display, _window, interval);
            _currentSwapInterval = interval;
            return;
        }

        if (_swapIntervalMesa != null)
        {
            _swapIntervalMesa(interval);
            _currentSwapInterval = interval;
            return;
        }

        if (_swapIntervalSgi != null)
        {
            _swapIntervalSgi(interval);
            _currentSwapInterval = interval;
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

        MakeCurrent(_display);
        foreach (var tex in _textures)
        {
            uint t = tex;
            GL.DeleteTextures(1, ref t);
        }
        _textures.Clear();
        TextCache.Dispose();
        ReleaseCurrent();

        LibGL.glXDestroyContext(_display, GlxContext);
    }
}
