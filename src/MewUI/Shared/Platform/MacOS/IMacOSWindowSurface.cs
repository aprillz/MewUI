namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// macOS Metal surface: an NSView* plus a CAMetalLayer*.
/// </summary>
public interface IMacOSMetalWindowSurface : IWindowSurface
{
    nint View { get; }

    nint MetalLayer { get; }
}

