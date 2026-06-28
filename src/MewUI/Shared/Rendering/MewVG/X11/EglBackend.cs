using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// EGL implementation of <see cref="IX11GLBackend"/>. Renders desktop GL through EGL
/// (<c>EGL_OPENGL_API</c>) so NanoVG/MewVG is unchanged, while enabling dma_buf/EGLImage
/// zero-copy (which segfaults under GLX). Selected via <c>MewVGX11Backend.RegisterEgl</c>.
/// </summary>
internal sealed class EglBackend : IX11GLBackend
{
    public IX11GLVisualChooser VisualChooser { get; } = new EglVisualChooser();

    public IOpenGLWindowResources CreateWindowResources(nint display, nint window, X11GLVisualInfo visualInfo, nint shareContext)
        => EglOpenGLWindowResources.Create(display, window, visualInfo, shareContext);

    public IOpenGLWindowResources CreateWorkerResources(nint display, nint drawable, X11GLVisualInfo visualInfo)
        => EglWorkerResources.Create(display, visualInfo);

    public nint GetCurrentContext() => LibEgl.eglGetCurrentContext();
}
