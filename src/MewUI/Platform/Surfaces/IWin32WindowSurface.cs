namespace Aprillz.MewUI.Platform.Win32;

/// <summary>
/// Win32 window surface (HWND-based).
/// </summary>
public interface IWin32WindowSurface : IWindowSurface
{
    nint Hwnd { get; }
}

/// <summary>
/// Win32 window surface that provides an HDC valid for the current render pass.
/// </summary>
public interface IWin32HdcWindowSurface : IWin32WindowSurface
{
    nint Hdc { get; }
}

