namespace Aprillz.MewUI;

/// <summary>
/// Flags indicating which native window chrome features the platform supports.
/// </summary>
[Flags]
public enum WindowChromeCapabilities
{
    /// <summary>No native chrome features supported.</summary>
    None = 0,

    /// <summary>Client area can be extended into the title bar.</summary>
    ExtendClientArea = 1 << 0,

    /// <summary>Native window border color can be set programmatically.</summary>
    NativeBorderColor = 1 << 1,

    /// <summary>Native chrome buttons (close/minimize/maximize) are preserved when extending client area.</summary>
    NativeChromeButtons = 1 << 2,

    /// <summary>OS draws a window border (frame) with rounded corners if applicable.</summary>
    NativeWindowBorder = 1 << 3,
}
