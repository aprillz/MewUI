using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// EGL-backed window GL resources (the EGL twin of <see cref="GlxOpenGLWindowResources"/>). Creates
/// a desktop-GL context via EGL (<c>EGL_OPENGL_API</c>) so the existing NanoVG GL3 path is unchanged,
/// while enabling dma_buf/EGLImage zero-copy (which segfaults in a GLX context). Selected by the
/// MewVG X11 factory when the EGL path is enabled.
/// </summary>
internal sealed class EglOpenGLWindowResources : IOpenGLWindowResources
{
    private readonly nint _eglDisplay;
    private readonly nint _surface;
    private readonly nint _display;
    private readonly nint _window;
    private readonly HashSet<uint> _textures = new();
    private int _currentSwapInterval = int.MinValue;
    private bool _disposed;

    public nint EglContext { get; }
    public nint EglDisplay => _eglDisplay;
    public nint NativeContext => EglContext;
    public bool SupportsBgra { get; }
    public bool SupportsNpotTextures { get; }

    private EglOpenGLWindowResources(nint eglDisplay, nint surface, nint context, nint display, nint window, bool supportsBgra, bool supportsNpot)
    {
        _eglDisplay = eglDisplay;
        _surface = surface;
        EglContext = context;
        _display = display;
        _window = window;
        SupportsBgra = supportsBgra;
        SupportsNpotTextures = supportsNpot;
    }

    public static EglOpenGLWindowResources Create(nint display, nint window, X11GLVisualInfo visualInfo, nint shareContext)
    {
        DiagLog.Write($"EGL create: display=0x{display.ToInt64():X} window=0x{window.ToInt64():X} share=0x{shareContext.ToInt64():X}");

        nint eglDisplay = LibEgl.eglGetDisplay(display);
        if (eglDisplay == LibEgl.EGL_NO_DISPLAY)
        {
            throw new InvalidOperationException("eglGetDisplay returned EGL_NO_DISPLAY.");
        }

        if (!LibEgl.eglInitialize(eglDisplay, out _, out _))
        {
            throw new InvalidOperationException($"eglInitialize failed: 0x{LibEgl.eglGetError():X}.");
        }

        if (!LibEgl.eglBindAPI(LibEgl.EGL_OPENGL_API))
        {
            throw new InvalidOperationException("eglBindAPI(EGL_OPENGL_API) failed.");
        }

        nint config = FindConfigForVisual(eglDisplay, (int)visualInfo.VisualId);
        if (config == 0)
        {
            throw new InvalidOperationException("No EGL config matches the window visual.");
        }

        nint surface = LibEgl.eglCreateWindowSurface(eglDisplay, config, window, null);
        if (surface == LibEgl.EGL_NO_SURFACE)
        {
            throw new InvalidOperationException($"eglCreateWindowSurface failed: 0x{LibEgl.eglGetError():X}.");
        }

        // shareContext = factory's worker EGL context, so worker-rendered FBO textures are
        // sample-able from this window context (matches the GLX share-list behavior).
        int[] contextAttribs = { LibEgl.EGL_NONE };
        nint ctx = LibEgl.eglCreateContext(eglDisplay, config, shareContext, contextAttribs);
        if (ctx == LibEgl.EGL_NO_CONTEXT)
        {
            throw new InvalidOperationException($"eglCreateContext failed: 0x{LibEgl.eglGetError():X}.");
        }

        if (!LibEgl.eglMakeCurrent(eglDisplay, surface, surface, ctx))
        {
            throw new InvalidOperationException($"eglMakeCurrent failed: 0x{LibEgl.eglGetError():X}.");
        }

        bool supportsBgra = DetectBgraSupport();
        bool supportsNpot = DetectNpotSupport();

        GL.Disable(0x0B71 /* GL_DEPTH_TEST */);
        GL.Disable(0x0B44 /* GL_CULL_FACE */);
        GL.Enable(GL.GL_BLEND);
        GL.BlendFuncSeparate(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA, GL.GL_ONE, GL.GL_ONE_MINUS_SRC_ALPHA);
        GL.Enable(GL.GL_TEXTURE_2D);
        GL.Enable(GL.GL_MULTISAMPLE);
        GL.Enable(GL.GL_LINE_SMOOTH);
        GL.Hint(GL.GL_LINE_SMOOTH_HINT, GL.GL_NICEST);

        DiagLog.Write($"EGL context ok: ctx=0x{ctx.ToInt64():X} BGRA={supportsBgra}");

        LibEgl.eglMakeCurrent(eglDisplay, LibEgl.EGL_NO_SURFACE, LibEgl.EGL_NO_SURFACE, LibEgl.EGL_NO_CONTEXT);

        return new EglOpenGLWindowResources(eglDisplay, surface, ctx, display, window, supportsBgra, supportsNpot);
    }

    internal static nint FindConfigForVisual(nint eglDisplay, int visualId)
    {
        int[] attribs =
        {
            LibEgl.EGL_SURFACE_TYPE, LibEgl.EGL_WINDOW_BIT,
            LibEgl.EGL_RENDERABLE_TYPE, LibEgl.EGL_OPENGL_BIT,
            LibEgl.EGL_RED_SIZE, 8,
            LibEgl.EGL_GREEN_SIZE, 8,
            LibEgl.EGL_BLUE_SIZE, 8,
            LibEgl.EGL_DEPTH_SIZE, 24,
            LibEgl.EGL_STENCIL_SIZE, 8,
            LibEgl.EGL_NONE,
        };

        var configs = new nint[64];
        if (!LibEgl.eglChooseConfig(eglDisplay, attribs, configs, configs.Length, out int num) || num == 0)
        {
            return 0;
        }

        for (int i = 0; i < num; i++)
        {
            if (LibEgl.eglGetConfigAttrib(eglDisplay, configs[i], LibEgl.EGL_NATIVE_VISUAL_ID, out int vid) && vid == visualId)
            {
                return configs[i];
            }
        }

        return 0;
    }

    private static bool DetectBgraSupport()
    {
        string? extensions = GL.GetExtensions();
        return !string.IsNullOrEmpty(extensions) &&
               extensions.Contains("GL_EXT_bgra", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DetectNpotSupport()
    {
        // The EGL path always runs on modern desktop GL (>= 3.0 via EGL_OPENGL_API), which has NPOT.
        return true;
    }

    public void MakeCurrent(nint deviceOrDisplay)
    {
        if (_disposed)
        {
            return;
        }

        LibEgl.eglMakeCurrent(_eglDisplay, _surface, _surface, EglContext);
    }

    public void ReleaseCurrent()
        => LibEgl.eglMakeCurrent(_eglDisplay, LibEgl.EGL_NO_SURFACE, LibEgl.EGL_NO_SURFACE, LibEgl.EGL_NO_CONTEXT);

    public void SwapBuffers(nint deviceOrDisplay, nint nativeWindow)
        => LibEgl.eglSwapBuffers(_eglDisplay, _surface);

    public void SetSwapInterval(int interval)
    {
        if (_disposed || _currentSwapInterval == interval)
        {
            return;
        }

        if (LibEgl.eglSwapInterval(_eglDisplay, interval))
        {
            _currentSwapInterval = interval;
        }
    }

    public void TrackTexture(uint textureId)
    {
        if (textureId == 0 || _disposed)
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
        ReleaseCurrent();

        LibEgl.eglDestroyContext(_eglDisplay, EglContext);
        LibEgl.eglDestroySurface(_eglDisplay, _surface);
    }
}
