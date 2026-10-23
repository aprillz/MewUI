using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Share-root worker context on Mesa's surfaceless EGL platform (EGL_MESA_platform_surfaceless), for
/// offscreen (FBO) rendering in a process that has no display connection at all. Exposed as an
/// <see cref="IOpenGLWindowResources"/>; <see cref="SwapBuffers"/> / <see cref="SetSwapInterval"/> are no-ops.
/// </summary>
internal sealed class EglSurfacelessWorkerResources : IOpenGLWindowResources
{
    private readonly nint _eglDisplay;
    // A 1x1 pbuffer stands in for a surface only where EGL_KHR_surfaceless_context is missing.
    private readonly nint _pbuffer;
    private bool _disposed;

    public nint NativeContext { get; }
    public bool SupportsBgra => false;
    public bool SupportsNpotTextures => true;

    private EglSurfacelessWorkerResources(nint eglDisplay, nint context, nint pbuffer)
    {
        _eglDisplay = eglDisplay;
        NativeContext = context;
        _pbuffer = pbuffer;
    }

    /// <summary>
    /// Opens the surfaceless EGL display and creates a desktop GL context on it; throws
    /// <see cref="PlatformNotSupportedException"/> when libEGL, the surfaceless platform or desktop GL is missing.
    /// </summary>
    public static EglSurfacelessWorkerResources Create()
    {
        nint display;
        try
        {
            if (!LibEgl.HasExtension(LibEgl.EGL_NO_DISPLAY, "EGL_MESA_platform_surfaceless"))
            {
                throw new PlatformNotSupportedException(
                    "Offscreen rendering without a display needs an EGL implementation with EGL_MESA_platform_surfaceless (Mesa libEGL).");
            }

            display = LibEgl.GetPlatformDisplay(LibEgl.EGL_PLATFORM_SURFACELESS_MESA, LibEgl.EGL_DEFAULT_DISPLAY);
        }
        catch (DllNotFoundException ex)
        {
            throw new PlatformNotSupportedException(
                "Offscreen rendering without a display needs libEGL.so.1 (Mesa), which is not installed.", ex);
        }

        if (display == LibEgl.EGL_NO_DISPLAY)
        {
            throw new PlatformNotSupportedException("EGL: the surfaceless platform returned no display.");
        }

        if (!LibEgl.eglInitialize(display, out int major, out int minor))
        {
            throw new InvalidOperationException($"EGL: eglInitialize on the surfaceless display failed (0x{LibEgl.eglGetError():X}).");
        }

        // MewVG's GL3 shaders need desktop GL, as the window paths use.
        if (!LibEgl.eglBindAPI(LibEgl.EGL_OPENGL_API))
        {
            throw new PlatformNotSupportedException("EGL: the surfaceless display offers no desktop OpenGL, which MewVG needs.");
        }

        nint config = ChooseConfig(display);
        if (config == 0)
        {
            throw new PlatformNotSupportedException("EGL: the surfaceless display offers no RGB8 desktop GL config.");
        }

        nint context = LibEgl.eglCreateContext(display, config, LibEgl.EGL_NO_CONTEXT, [LibEgl.EGL_NONE]);
        if (context == LibEgl.EGL_NO_CONTEXT)
        {
            throw new InvalidOperationException($"EGL: eglCreateContext on the surfaceless display failed (0x{LibEgl.eglGetError():X}).");
        }

        nint pbuffer = LibEgl.EGL_NO_SURFACE;
        if (!LibEgl.HasExtension(display, "EGL_KHR_surfaceless_context"))
        {
            pbuffer = LibEgl.eglCreatePbufferSurface(display, config, [LibEgl.EGL_WIDTH, 1, LibEgl.EGL_HEIGHT, 1, LibEgl.EGL_NONE]);
            if (pbuffer == LibEgl.EGL_NO_SURFACE)
            {
                LibEgl.eglDestroyContext(display, context);
                throw new InvalidOperationException($"EGL: eglCreatePbufferSurface failed (0x{LibEgl.eglGetError():X}).");
            }
        }

        DiagLog.Write($"[EGL] Surfaceless worker context created ctx=0x{context.ToInt64():X} EGL {major}.{minor}");
        return new EglSurfacelessWorkerResources(display, context, pbuffer);
    }

    private static nint ChooseConfig(nint display)
    {
        int[] attributes =
        [
            LibEgl.EGL_SURFACE_TYPE, LibEgl.EGL_PBUFFER_BIT,
            LibEgl.EGL_RENDERABLE_TYPE, LibEgl.EGL_OPENGL_BIT,
            LibEgl.EGL_RED_SIZE, 8,
            LibEgl.EGL_GREEN_SIZE, 8,
            LibEgl.EGL_BLUE_SIZE, 8,
            LibEgl.EGL_NONE,
        ];

        var configs = new nint[1];
        return LibEgl.eglChooseConfig(display, attributes, configs, configs.Length, out int count) && count > 0
            ? configs[0]
            : 0;
    }

    public void MakeCurrent(nint deviceOrDisplay)
    {
        if (_disposed) return;
        LibEgl.eglMakeCurrent(_eglDisplay, _pbuffer, _pbuffer, NativeContext);
    }

    public void ReleaseCurrent()
        => LibEgl.eglMakeCurrent(_eglDisplay, LibEgl.EGL_NO_SURFACE, LibEgl.EGL_NO_SURFACE, LibEgl.EGL_NO_CONTEXT);

    public void SwapBuffers(nint deviceOrDisplay, nint nativeWindow) { }

    public void SetSwapInterval(int interval) { }

    public void TrackTexture(uint textureId) { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseCurrent();
        if (_pbuffer != LibEgl.EGL_NO_SURFACE)
        {
            LibEgl.eglDestroySurface(_eglDisplay, _pbuffer);
        }

        LibEgl.eglDestroyContext(_eglDisplay, NativeContext);
    }
}
