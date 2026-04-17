using SkiaSharp;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Optional graphics-factory feature that exposes GPU-backed Skia surfaces for
/// controls such as <see cref="Controls.SKElement"/>.
/// </summary>
public interface ISkiaGpuControlFactory
{
    /// <summary>
    /// Creates a GPU-backed Skia surface bound to an active window.
    /// </summary>
    /// <param name="windowHandle">The platform window handle.</param>
    /// <param name="pixelWidth">Initial surface width in pixels.</param>
    /// <param name="pixelHeight">Initial surface height in pixels.</param>
    /// <param name="dpiScale">Initial DPI scale.</param>
    /// <returns>A GPU-backed Skia control surface.</returns>
    ISkiaGpuControlSurface CreateSkiaGpuControlSurface(
        nint windowHandle,
        int pixelWidth,
        int pixelHeight,
        double dpiScale);
}

/// <summary>
/// Represents a retained GPU-backed Skia surface that can paint and composite
/// into the current MewUI render pass without CPU readback.
/// </summary>
public interface ISkiaGpuControlSurface : IDisposable
{
    /// <summary>
    /// Gets the surface width in pixels.
    /// </summary>
    int PixelWidth { get; }

    /// <summary>
    /// Gets the surface height in pixels.
    /// </summary>
    int PixelHeight { get; }

    /// <summary>
    /// Gets the current DPI scale associated with the surface.
    /// </summary>
    double DpiScale { get; }

    /// <summary>
    /// Gets the backing surface color type.
    /// </summary>
    SKColorType ColorType { get; }

    /// <summary>
    /// Gets the backing surface alpha type.
    /// </summary>
    SKAlphaType AlphaType { get; }

    /// <summary>
    /// Resizes the backing surface.
    /// </summary>
    void Resize(int pixelWidth, int pixelHeight, double dpiScale);

    /// <summary>
    /// Paints the Skia surface when needed and composites it into the supplied
    /// graphics context.
    /// </summary>
    /// <param name="context">The active MewUI graphics context.</param>
    /// <param name="bounds">Destination bounds in DIPs.</param>
    /// <param name="redraw">Whether the retained Skia content must be repainted.</param>
    /// <param name="painter">Callback that paints into the retained Skia surface.</param>
    void Draw(IGraphicsContext context, Rect bounds, bool redraw, Action<SKSurface> painter);
}
