using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// <see cref="IX11GLBackend"/> of a process that renders without any display: the worker context lives on
/// Mesa's surfaceless EGL platform and no window context can be made. Installed by the MewVG X11 factory
/// in place of the registered backend when the platform host has no display.
/// </summary>
internal sealed class EglSurfacelessBackend : IX11GLBackend
{
    private readonly IX11GLBackend _replaced;

    public EglSurfacelessBackend(IX11GLBackend replaced)
    {
        _replaced = replaced;
    }

    public IX11GLVisualChooser VisualChooser => _replaced.VisualChooser;

    public IOpenGLWindowResources CreateWindowResources(nint display, nint window, X11GLVisualInfo visualInfo, nint shareContext)
        => throw new InvalidOperationException("A window context cannot be created while the process renders without a display.");

    public IOpenGLWindowResources CreateWorkerResources(nint display, X11GLVisualInfo visualInfo)
        => EglSurfacelessWorkerResources.Create();

    public nint GetCurrentContext() => LibEgl.eglGetCurrentContext();
}
