using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// The EGL display, config and share-root context the framebuffer backend renders with. Desktop GL is taken
/// when the driver offers it, so the GL3 shaders run unchanged; GLES 3 is the fallback.
/// </summary>
internal sealed class FramebufferEglDevice : IDisposable
{
    // MEWUI_FB_GL=gles forces the GLES path on a driver that also offers desktop GL.
    private const string API_OVERRIDE_VARIABLE = "MEWUI_FB_GL";

    private readonly nint _pbuffer;
    private bool _disposed;

    private FramebufferEglDevice(nint display, nint config, nint workerContext, bool isGles, bool supportsSurfaceless, nint pbuffer, string description)
    {
        Display = display;
        Config = config;
        WorkerContext = workerContext;
        IsGles = isGles;
        SupportsSurfaceless = supportsSurfaceless;
        _pbuffer = pbuffer;
        Description = description;
    }

    public nint Display { get; }

    public nint Config { get; }

    /// <summary>Context every other context shares with; made current without a window for offscreen work.</summary>
    public nint WorkerContext { get; }

    public bool IsGles { get; }

    public bool SupportsSurfaceless { get; }

    public string Description { get; }

    /// <summary>Surface a context renders through when it has no window: none, or a 1x1 pbuffer without surfaceless support.</summary>
    public nint OffscreenSurface => _pbuffer;

    /// <summary>Opens EGL on a GBM device with a config whose native visual is <paramref name="gbmFormat"/>.</summary>
    public static FramebufferEglDevice CreateForGbm(nint gbmDevice, uint gbmFormat)
    {
        nint display = LibEgl.GetPlatformDisplay(LibEgl.EGL_PLATFORM_GBM_KHR, gbmDevice);
        if (display == LibEgl.EGL_NO_DISPLAY)
        {
            display = LibEgl.eglGetDisplay(gbmDevice);
        }

        return Create(display, "GBM", LibEgl.EGL_WINDOW_BIT, (int)gbmFormat);
    }

    /// <summary>Opens EGL without any window system, for rendering that is read back by the CPU.</summary>
    public static FramebufferEglDevice CreateSurfaceless()
    {
        nint display = LibEgl.GetPlatformDisplay(LibEgl.EGL_PLATFORM_SURFACELESS_MESA, LibEgl.EGL_DEFAULT_DISPLAY);
        if (display == LibEgl.EGL_NO_DISPLAY)
        {
            display = LibEgl.eglGetDisplay(LibEgl.EGL_DEFAULT_DISPLAY);
        }

        return Create(display, "surfaceless", LibEgl.EGL_PBUFFER_BIT, nativeVisualId: 0);
    }

    private static FramebufferEglDevice Create(nint display, string platformName, int surfaceType, int nativeVisualId)
    {
        if (display == LibEgl.EGL_NO_DISPLAY)
        {
            throw new InvalidOperationException($"EGL: no {platformName} display.");
        }

        if (!LibEgl.eglInitialize(display, out int major, out int minor))
        {
            throw new InvalidOperationException($"EGL: eglInitialize on the {platformName} display failed (0x{LibEgl.eglGetError():X}).");
        }

        bool supportsSurfaceless = LibEgl.HasExtension(display, "EGL_KHR_surfaceless_context");
        bool forceGles = string.Equals(Environment.GetEnvironmentVariable(API_OVERRIDE_VARIABLE), "gles", StringComparison.OrdinalIgnoreCase);
        foreach (bool gles in forceGles ? new[] { true } : new[] { false, true })
        {
            if (!LibEgl.eglBindAPI(gles ? LibEgl.EGL_OPENGL_ES_API : LibEgl.EGL_OPENGL_API))
            {
                continue;
            }

            nint config = ChooseConfig(display, surfaceType, gles ? LibEgl.EGL_OPENGL_ES3_BIT : LibEgl.EGL_OPENGL_BIT, nativeVisualId);
            if (config == 0)
            {
                continue;
            }

            nint worker = LibEgl.eglCreateContext(display, config, LibEgl.EGL_NO_CONTEXT, ContextAttributes(gles));
            if (worker == LibEgl.EGL_NO_CONTEXT)
            {
                continue;
            }

            nint pbuffer = LibEgl.EGL_NO_SURFACE;
            if (!supportsSurfaceless && surfaceType == LibEgl.EGL_PBUFFER_BIT)
            {
                pbuffer = LibEgl.eglCreatePbufferSurface(display, config, [LibEgl.EGL_WIDTH, 1, LibEgl.EGL_HEIGHT, 1, LibEgl.EGL_NONE]);
            }

            string description = $"EGL {major}.{minor} {platformName}, {(gles ? "GLES 3" : "desktop GL")}{(supportsSurfaceless ? string.Empty : ", no surfaceless contexts")}";
            return new FramebufferEglDevice(display, config, worker, gles, supportsSurfaceless, pbuffer, description);
        }

        throw new InvalidOperationException($"EGL: the {platformName} display offers no RGB8 config for desktop GL or GLES 3.");
    }

    /// <summary>Attributes for a context of the API this device chose.</summary>
    public static int[] ContextAttributes(bool gles)
        => gles ? [LibEgl.EGL_CONTEXT_CLIENT_VERSION, 3, LibEgl.EGL_NONE] : [LibEgl.EGL_NONE];

    private static nint ChooseConfig(nint display, int surfaceType, int renderableType, int nativeVisualId)
    {
        int[] attributes =
        [
            LibEgl.EGL_SURFACE_TYPE, surfaceType,
            LibEgl.EGL_RENDERABLE_TYPE, renderableType,
            LibEgl.EGL_RED_SIZE, 8,
            LibEgl.EGL_GREEN_SIZE, 8,
            LibEgl.EGL_BLUE_SIZE, 8,
            LibEgl.EGL_NONE,
        ];

        var configs = new nint[64];
        if (!LibEgl.eglChooseConfig(display, attributes, configs, configs.Length, out int count) || count == 0)
        {
            return 0;
        }

        if (nativeVisualId == 0)
        {
            return configs[0];
        }

        for (int index = 0; index < count; index++)
        {
            if (LibEgl.eglGetConfigAttrib(display, configs[index], LibEgl.EGL_NATIVE_VISUAL_ID, out int visualId) && visualId == nativeVisualId)
            {
                return configs[index];
            }
        }

        return 0;
    }

    /// <summary>Makes the worker context current on this thread with no window surface.</summary>
    public bool MakeWorkerCurrent()
        => LibEgl.eglMakeCurrent(Display, _pbuffer, _pbuffer, WorkerContext);

    public void ReleaseCurrent()
        => LibEgl.eglMakeCurrent(Display, LibEgl.EGL_NO_SURFACE, LibEgl.EGL_NO_SURFACE, LibEgl.EGL_NO_CONTEXT);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseCurrent();
        if (_pbuffer != LibEgl.EGL_NO_SURFACE)
        {
            LibEgl.eglDestroySurface(Display, _pbuffer);
        }

        LibEgl.eglDestroyContext(Display, WorkerContext);
    }
}
