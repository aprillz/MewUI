using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// GLX implementation of <see cref="IX11GLBackend"/> - the default X11 GL path.
/// </summary>
internal sealed class GlxBackend : IX11GLBackend
{
    public IX11GLVisualChooser VisualChooser { get; } = new GlxVisualChooser();

    public IOpenGLWindowResources CreateWindowResources(nint display, nint window, X11GLVisualInfo visualInfo, nint shareContext)
        => GlxOpenGLWindowResources.Create(display, window, visualInfo, shareContext);

    public IOpenGLWindowResources CreateWorkerResources(nint display, nint drawable, X11GLVisualInfo visualInfo)
        => GlxWorkerResources.Create(display, drawable, visualInfo);

    public nint GetCurrentContext() => LibGL.glXGetCurrentContext();
}
