namespace Aprillz.MewUI.Platform;

/// <summary>
/// Identifies the platform display associated with a window surface as an opaque
/// (IdLow, IdHigh, NativeHandle) tuple. Equality is structural, so a backend only needs to know
/// which field its own platform fills, typically <see cref="NativeHandle"/> plus an optional
/// secondary index in <see cref="IdHigh"/>. Per-platform construction lives in the platform assembly.
/// </summary>
/// <param name="IdLow">Low part of the opaque id.</param>
/// <param name="IdHigh">High part of the opaque id, or a secondary index such as a screen number.</param>
/// <param name="NativeHandle">Native display handle, when the platform has one.</param>
public readonly record struct PlatformDisplayIdentity(ulong IdLow, long IdHigh, nint NativeHandle)
{
    /// <summary>Gets whether no field is set.</summary>
    public bool IsEmpty => IdLow == 0 && IdHigh == 0 && NativeHandle == 0;
}

/// <summary>
/// Represents a platform-provided drawing/presentation surface for a window.
/// Implementations are platform-specific and are consumed by graphics backends that support them.
/// </summary>
public interface IWindowSurface
{
    /// <summary>
    /// Gets the primary native handle of the surface; its concrete type is defined by the platform.
    /// </summary>
    nint Handle { get; }

    /// <summary>Gets the width of the surface in device pixels.</summary>
    int PixelWidth { get; }

    /// <summary>Gets the height of the surface in device pixels.</summary>
    int PixelHeight { get; }

    /// <summary>Gets the DPI scale factor of the surface (1.0 at 96 DPI).</summary>
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
/// This is used to support platform-specific presentation modes (for example per-pixel-transparent windows).
/// </summary>
public interface IWindowSurfacePresenter
{
    /// <summary>
    /// Presents a frame for the specified window onto the provided surface.
    /// Returns <see langword="true"/> if the surface was handled, otherwise <see langword="false"/>.
    /// </summary>
    bool Present(Window window, IWindowSurface surface, double opacity);
}
