using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// The active X11 GL rendering backend (GLX or EGL). Chosen once at registration
/// (<c>MewVGX11Backend.Register</c> / <c>RegisterEgl</c>) and stored in
/// <see cref="X11GLBackendRegistry"/>, so every GL-API-specific construction goes through one
/// object and the MewVG X11 factory stays branch-free. EGL exists because dma_buf/EGLImage
/// zero-copy segfaults under a GLX context.
/// </summary>
internal interface IX11GLBackend
{
    /// <summary>Visual chooser to publish to the platform layer (<see cref="X11GLVisualChooserRegistry"/>).</summary>
    IX11GLVisualChooser VisualChooser { get; }

    /// <summary>Creates a window's GL context, optionally sharing with the worker context.</summary>
    IOpenGLWindowResources CreateWindowResources(nint display, nint window, X11GLVisualInfo visualInfo, nint shareContext);

    /// <summary>Creates the surfaceless share-root context used for background offscreen (FBO)
    /// rendering. GLX reuses <paramref name="drawable"/> for make-current; EGL is surfaceless.</summary>
    IOpenGLWindowResources CreateWorkerResources(nint display, nint drawable, X11GLVisualInfo visualInfo);

    /// <summary>Current GL context handle on the calling thread (0 if none).</summary>
    nint GetCurrentContext();
}
