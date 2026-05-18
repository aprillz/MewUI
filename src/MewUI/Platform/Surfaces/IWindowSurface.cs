namespace Aprillz.MewUI.Platform;

/// <summary>
/// Identifies the platform display/output associated with a window surface as an opaque
/// (IdLow, IdHigh, NativeHandle) tuple. Equality is structural, so backends only need to
/// know how to extract their own meaningful field — typically <see cref="NativeHandle"/>
/// (Win32 HMONITOR, macOS NSScreen*, etc.) plus an optional secondary index
/// (<see cref="IdHigh"/> carries the X11 screen number).
/// The Core type is platform-neutral; per-platform construction lives in the corresponding
/// platform backend.
/// </summary>
public readonly record struct PlatformDisplayIdentity(ulong IdLow, long IdHigh, nint NativeHandle)
{
    public bool IsEmpty => IdLow == 0 && IdHigh == 0 && NativeHandle == 0;
}

/// <summary>
/// Represents a platform-provided drawing/presentation surface for a window.
/// Implementations are platform-specific and are consumed by graphics backends that support them.
/// </summary>
public interface IWindowSurface
{
    /// <summary>
    /// Gets the primary native handle of the surface.
    /// For Win32 this is typically an HWND.
    /// </summary>
    nint Handle { get; }

    int PixelWidth { get; }

    int PixelHeight { get; }

    double DpiScale { get; }

    /// <summary>
    /// Gets the platform display/output identity currently associated with this surface.
    /// Backends should treat an empty value as "unknown" and fall back to their existing
    /// platform-specific discovery path.
    /// </summary>
    PlatformDisplayIdentity DisplayIdentity => default;
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
