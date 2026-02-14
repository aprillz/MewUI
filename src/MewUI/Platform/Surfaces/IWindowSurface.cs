namespace Aprillz.MewUI.Platform;

public enum WindowSurfaceKind
{
    Default = 0,

    /// <summary>
    /// Prefer an OpenGL-backed native view/surface when available.
    /// </summary>
    OpenGL = 1,

    /// <summary>
    /// Prefer a Metal-backed native layer/surface when available.
    /// </summary>
    Metal = 2,

    /// <summary>
    /// Prefer a layered/composited presentation surface (e.g., Win32 UpdateLayeredWindow).
    /// </summary>
    Layered = 3,
}

/// <summary>
/// Represents a platform-provided drawing/presentation surface for a window.
/// Implementations are platform-specific and are consumed by graphics backends that support them.
/// </summary>
public interface IWindowSurface
{
    WindowSurfaceKind Kind { get; }

    /// <summary>
    /// Gets the primary native handle of the surface.
    /// For Win32 this is typically an HWND.
    /// </summary>
    nint Handle { get; }

    int PixelWidth { get; }

    int PixelHeight { get; }

    double DpiScale { get; }
}

/// <summary>
/// Optional graphics capability for factories that can present frames via platform window surfaces.
/// This is used to support platform-specific presentation modes (e.g., layered windows).
/// </summary>
public interface IWindowSurfacePresenter
{
    /// <summary>
    /// Presents a frame for the specified window onto the provided surface.
    /// Returns <see langword="true"/> if the surface was handled, otherwise <see langword="false"/>.
    /// </summary>
    bool Present(Window window, IWindowSurface surface, double opacity);
}
