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
    int PixelWidth { get; }

    int PixelHeight { get; }

    /// <summary>Human-readable description of the active path (GPU zero-copy, software zero-copy, GL readback, etc.) for diagnostics overlays.</summary>
    string Description { get; }

    /// <summary>True when the host's affinity changed since the last <see cref="Paint"/> and the caller should retry after another <see cref="EnsureSurface"/>.</summary>
    bool SurfaceInvalidated { get; }

    bool EnsureSurface(int pixelWidth, int pixelHeight);

    IImage? Paint(Action<SKSurface> painter);
}

/// <summary>Opt-in for hosts that take a faster non-blended present when output is fully opaque (today: GDI SRCCOPY vs AlphaBlend).</summary>
public interface IOpaqueAwareSurfaceHost
{
    bool IsOpaque { set; }
}
