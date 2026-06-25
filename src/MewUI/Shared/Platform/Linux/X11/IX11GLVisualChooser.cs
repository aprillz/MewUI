namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// Port for choosing the X11 window visual compatible with the active GL rendering backend.
/// Implemented by the rendering backend (GLX today, EGL later) and registered via
/// <see cref="X11GLVisualChooserRegistry"/>. This keeps GL-API specifics (<c>glX*</c>/<c>egl*</c>)
/// out of the platform/windowing layer: the platform only creates the window from the returned
/// neutral <see cref="X11GLVisualInfo"/> (X11 <c>XVisualInfo</c> data, no GL API).
/// </summary>
public interface IX11GLVisualChooser
{
    /// <summary>
    /// Selects an X11 visual suitable for GL rendering on <paramref name="display"/>/<paramref name="screen"/>.
    /// Returns false when no suitable visual exists (caller decides how to handle).
    /// </summary>
    bool TryChooseVisual(nint display, int screen, bool allowsTransparency, out X11GLVisualInfo visual);
}

/// <summary>
/// Process-wide slot for the active <see cref="IX11GLVisualChooser"/>. The rendering backend sets
/// this in its <c>Register()</c> before any window is created; the X11 window backend reads it at
/// window creation to pick a GL-compatible visual.
/// </summary>
public static class X11GLVisualChooserRegistry
{
    public static IX11GLVisualChooser? Current { get; set; }
}
