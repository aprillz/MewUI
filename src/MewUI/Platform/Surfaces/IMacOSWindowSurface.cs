namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// macOS OpenGL surface: an NSView* plus an NSOpenGLContext*.
/// </summary>
public interface IMacOSOpenGLWindowSurface : IWindowSurface
{
    nint View { get; }

    nint OpenGLContext { get; }
}

/// <summary>
/// macOS Metal surface: an NSView* plus a CAMetalLayer*.
/// </summary>
public interface IMacOSMetalWindowSurface : IWindowSurface
{
    nint View { get; }

    nint MetalLayer { get; }
}

