namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Process-wide slot for the active <see cref="IX11GLBackend"/>. Set by the MewVG X11 backend
/// registration (GLX by default, EGL via <c>RegisterEgl</c>) before any window is created; the
/// factory and window resources read it instead of branching on a path flag.
/// </summary>
internal static class X11GLBackendRegistry
{
    public static IX11GLBackend? Current { get; set; }
}
