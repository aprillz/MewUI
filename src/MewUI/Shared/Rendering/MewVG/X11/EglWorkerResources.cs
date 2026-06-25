using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Surfaceless EGL share-root worker context (EGL_KHR_surfaceless_context) for background
/// offscreen (FBO) rendering. shareContext = EGL_NO_CONTEXT, so it is the root all window EGL
/// contexts share with. Exposed as an <see cref="IOpenGLWindowResources"/>;
/// <see cref="SwapBuffers"/> / <see cref="SetSwapInterval"/> are no-ops.
/// </summary>
internal sealed class EglWorkerResources : IOpenGLWindowResources
{
    private readonly nint _eglDisplay;
    private bool _disposed;

    public nint NativeContext { get; }
    public bool SupportsBgra => false;
    public bool SupportsNpotTextures => true;

    private EglWorkerResources(nint eglDisplay, nint ctx)
    {
        _eglDisplay = eglDisplay;
        NativeContext = ctx;
    }

    public static EglWorkerResources Create(nint display, X11GLVisualInfo visualInfo)
    {
        nint edpy = LibEgl.eglGetDisplay(display);
        if (edpy == LibEgl.EGL_NO_DISPLAY || !LibEgl.eglInitialize(edpy, out _, out _))
        {
            throw new InvalidOperationException("Worker EGL context: eglGetDisplay/eglInitialize failed.");
        }

        LibEgl.eglBindAPI(LibEgl.EGL_OPENGL_API);
        nint config = EglOpenGLWindowResources.FindConfigForVisual(edpy, (int)visualInfo.VisualId);
        if (config == 0)
        {
            throw new InvalidOperationException("Worker EGL context: no config matches the window visual.");
        }

        int[] ctxAttribs = { LibEgl.EGL_NONE };
        nint ctx = LibEgl.eglCreateContext(edpy, config, LibEgl.EGL_NO_CONTEXT, ctxAttribs);
        if (ctx == LibEgl.EGL_NO_CONTEXT)
        {
            throw new InvalidOperationException("Worker EGL context: eglCreateContext failed.");
        }

        DiagLog.Write($"[EGL] Worker context created ctx=0x{ctx.ToInt64():X}");
        return new EglWorkerResources(edpy, ctx);
    }

    public void MakeCurrent(nint deviceOrDisplay)
    {
        if (_disposed) return;
        LibEgl.eglMakeCurrent(_eglDisplay, LibEgl.EGL_NO_SURFACE, LibEgl.EGL_NO_SURFACE, NativeContext);
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
        LibEgl.eglDestroyContext(_eglDisplay, NativeContext);
    }
}
