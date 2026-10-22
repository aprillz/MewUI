using Aprillz.MewUI.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.Skia;

/// <summary>
/// Backend-specific GPU host that wraps an <see cref="SKSurface"/> over a backend-owned
/// render target so the produced <see cref="IImage"/> can be sampled by the backend without
/// a CPU readback. Surface lifecycle (size, recreation) is managed by the host; callers
/// invoke <see cref="EnsureSurface"/> before <see cref="Paint"/> each render pass.
/// </summary>
public interface ISkiaSurfaceHost : IDisposable
{
    /// <summary>Gets the width of the current surface in device pixels.</summary>
    int PixelWidth { get; }

    /// <summary>Gets the height of the current surface in device pixels.</summary>
    int PixelHeight { get; }

    /// <summary>Gets a human-readable description of the active path (GPU zero-copy, software zero-copy, GL readback) for diagnostics overlays.</summary>
    string Description { get; }

    /// <summary>Gets whether the host's affinity changed since the last <see cref="Paint"/>, so the caller should retry after another <see cref="EnsureSurface"/>.</summary>
    bool SurfaceInvalidated { get; }

    /// <summary>Creates or resizes the surface to the given size in device pixels; returns false when the host cannot provide one.</summary>
    bool EnsureSurface(int pixelWidth, int pixelHeight);

    /// <summary>Runs <paramref name="painter"/> on the surface and returns the image the backend samples, or null when there is no surface.</summary>
    IImage? Paint(Action<SKSurface> painter);
}

/// <summary>Opt-in for hosts that take a faster non-blended present when the output is fully opaque.</summary>
public interface IOpaqueAwareSurfaceHost
{
    /// <summary>Sets whether the painted output is fully opaque, allowing a non-blended present.</summary>
    bool IsOpaque { set; }
}
