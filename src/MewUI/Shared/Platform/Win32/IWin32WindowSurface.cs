namespace Aprillz.MewUI.Platform.Win32;

/// <summary>
/// Win32 window surface (HWND-based).
/// </summary>
public interface IWin32WindowSurface : IWindowSurface
{
    nint Hwnd { get; }

    /// <summary>
    /// Indicates the surface requires an alpha channel for transparent composition
    /// (e.g., system backdrop effects). Graphics backends should use premultiplied alpha
    /// rendering and alpha-preserving presentation when this is <see langword="true"/>.
    /// </summary>
    bool TransparentComposition => false;
}

/// <summary>
/// Win32 window surface that provides an HDC valid for the current render pass.
/// </summary>
public interface IWin32HdcWindowSurface : IWin32WindowSurface
{
    nint Hdc { get; }
}

