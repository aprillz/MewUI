using System.Numerics;

using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Factory interface for creating graphics resources.
/// Allows different graphics backends to be plugged in.
/// </summary>
public interface IGraphicsFactory   
{
    /// <summary>
    /// Identifies which built-in backend this factory represents.
    /// Custom factories can return <see cref="GraphicsBackend.Custom"/>.
    /// </summary>
    GraphicsBackend Backend { get; }

    // ── Brush / Pen ───────────────────────────────────────────────────────────

    /// <summary>Creates a solid-color brush.</summary>
    /// <remarks>
    /// The default DIM returns a <see cref="SimpleSolidColorBrush"/> with no backend resources.
    /// Backends may override for resource lifetime tracking.
    /// The caller is responsible for disposing the returned brush.
    /// </remarks>
    ISolidColorBrush CreateSolidColorBrush(Color color) => new SimpleSolidColorBrush(color);

    /// <summary>Creates a pen that strokes with a solid color.</summary>
    /// <param name="color">Stroke color.</param>
    /// <param name="thickness">Stroke thickness in device-independent pixels.</param>
    /// <param name="strokeStyle">
    /// Stroke attributes, or <see langword="null"/> for <see cref="StrokeStyle.Default"/>
    /// (flat caps, miter join, miter limit 10).
    /// </param>
    /// <remarks>
    /// The default DIM returns a <see cref="SimplePen"/>.
    /// The caller is responsible for disposing the returned pen.
    /// </remarks>
    IPen CreatePen(Color color, double thickness = 1.0, StrokeStyle? strokeStyle = null) =>
        new SimplePen(color, thickness, strokeStyle ?? StrokeStyle.Default);

    /// <summary>Creates a pen using an existing brush.</summary>
    /// <param name="brush">The brush to use for the stroke.  The pen does not take ownership.</param>
    /// <param name="thickness">Stroke thickness in device-independent pixels.</param>
    /// <param name="strokeStyle">Stroke attributes, or <see langword="null"/> for the default.</param>
    /// <remarks>The caller is responsible for disposing the returned pen (and the brush separately).</remarks>
    IPen CreatePen(IBrush brush, double thickness = 1.0, StrokeStyle? strokeStyle = null) =>
        new SimplePen(brush, thickness, strokeStyle ?? StrokeStyle.Default);

    // ── Gradient Brushes ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a linear gradient brush.
    /// </summary>
    /// <param name="startPoint">Start point (in <paramref name="units"/> coordinates).</param>
    /// <param name="endPoint">End point (in <paramref name="units"/> coordinates).</param>
    /// <param name="stops">Color stops defining the gradient.</param>
    /// <param name="spreadMethod">How to extend the gradient beyond the start/end points.</param>
    /// <param name="units">Coordinate space for <paramref name="startPoint"/> and <paramref name="endPoint"/>.</param>
    /// <param name="gradientTransform">Optional additional transform applied to the gradient geometry.</param>
    /// <remarks>
    /// The default DIM returns a <see cref="SimpleLinearGradientBrush"/>.
    /// Backends that support GPU gradient rendering should override this method.
    /// The caller is responsible for disposing the returned brush.
    /// </remarks>
    ILinearGradientBrush CreateLinearGradientBrush(
        Point startPoint,
        Point endPoint,
        IReadOnlyList<GradientStop> stops,
        SpreadMethod spreadMethod = SpreadMethod.Pad,
        GradientUnits units = GradientUnits.UserSpaceOnUse,
        Matrix3x2? gradientTransform = null)
        => new SimpleLinearGradientBrush(startPoint, endPoint, stops, spreadMethod, units, gradientTransform);

    /// <summary>
    /// Creates a radial gradient brush.
    /// </summary>
    /// <param name="center">Center of the gradient ellipse.</param>
    /// <param name="gradientOrigin">Focal point from which the gradient radiates (SVG: fx/fy).</param>
    /// <param name="radiusX">X radius of the gradient ellipse.</param>
    /// <param name="radiusY">Y radius of the gradient ellipse.</param>
    /// <param name="stops">Color stops defining the gradient.</param>
    /// <param name="spreadMethod">How to extend the gradient beyond the ellipse boundary.</param>
    /// <param name="units">Coordinate space for geometry parameters.</param>
    /// <param name="gradientTransform">Optional additional transform applied to the gradient geometry.</param>
    /// <remarks>
    /// The default DIM returns a <see cref="SimpleRadialGradientBrush"/>.
    /// The caller is responsible for disposing the returned brush.
    /// </remarks>
    IRadialGradientBrush CreateRadialGradientBrush(
        Point center,
        Point gradientOrigin,
        double radiusX,
        double radiusY,
        IReadOnlyList<GradientStop> stops,
        SpreadMethod spreadMethod = SpreadMethod.Pad,
        GradientUnits units = GradientUnits.UserSpaceOnUse,
        Matrix3x2? gradientTransform = null)
        => new SimpleRadialGradientBrush(center, gradientOrigin, radiusX, radiusY, stops, spreadMethod, units, gradientTransform);

    // ── Font ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a font resource.
    /// </summary>
    IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false);

    /// <summary>
    /// Creates a font resource for a specific DPI.
    /// Font size is specified in DIPs (1/96 inch).
    /// </summary>
    IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false);

    /// <summary>
    /// Creates an image from a file path.
    /// </summary>
    IImage CreateImageFromFile(string path);

    /// <summary>
    /// Creates an image from a byte array.
    /// </summary>
    IImage CreateImageFromBytes(byte[] data);

    /// <summary>
    /// Creates an image backed by a versioned pixel source (e.g. <see cref="WriteableBitmap"/>).
    /// Backends should reflect updates when the source's <see cref="IPixelBufferSource.Version"/> changes.
    /// </summary>
    IImage CreateImageFromPixelSource(IPixelBufferSource source);

    /// <summary>
    /// Creates a graphics context for the specified render target.
    /// </summary>
    /// <param name="target">The render target to draw to.</param>
    /// <returns>A graphics context for drawing operations.</returns>
    IGraphicsContext CreateContext(IRenderTarget target);

    /// <summary>
    /// Creates a measurement-only graphics context for text measurement.
    /// </summary>
    IGraphicsContext CreateMeasurementContext(uint dpi);

    /// <summary>
    /// Creates a bitmap render target for offscreen rendering.
    /// </summary>
    /// <param name="pixelWidth">Width in pixels.</param>
    /// <param name="pixelHeight">Height in pixels.</param>
    /// <param name="dpiScale">DPI scale factor (default 1.0 for 96 DPI).</param>
    /// <returns>A bitmap render target with platform-appropriate resources.</returns>
    IBitmapRenderTarget CreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale = 1.0);
}

/// <summary>
/// Optional capability for factories that must release per-window resources when a window is destroyed.
/// </summary>
public interface IWindowResourceReleaser
{
    void ReleaseWindowResources(nint hwnd);
}
