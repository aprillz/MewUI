using System.Numerics;

using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Factory interface for creating graphics resources.
/// Allows different graphics backends to be plugged in.
/// </summary>
public interface IGraphicsFactory : IRenderDevice, IDisposable
{
    /// <summary>Creates a solid-color brush.</summary>
    /// <remarks>
    /// The default DIM returns a <see cref="SolidColorBrush"/> with no backend resources.
    /// Backends may override for resource lifetime tracking.
    /// The caller is responsible for disposing the returned brush.
    /// </remarks>
    ISolidColorBrush CreateSolidColorBrush(Color color) => new SolidColorBrush(color);

    /// <summary>Creates a pen that strokes with a solid color.</summary>
    /// <param name="color">Stroke color.</param>
    /// <param name="thickness">Stroke thickness in device-independent pixels.</param>
    /// <param name="strokeStyle">
    /// Stroke attributes, or <see langword="null"/> for <see cref="StrokeStyle.Default"/>
    /// (flat caps, miter join, miter limit 10).
    /// </param>
    /// <remarks>
    /// The default DIM returns a <see cref="Pen"/>.
    /// The caller is responsible for disposing the returned pen.
    /// </remarks>
    IPen CreatePen(Color color, double thickness = 1.0, StrokeStyle? strokeStyle = null) =>
        new Pen(color, thickness, strokeStyle ?? StrokeStyle.Default);

    /// <summary>Creates a pen using an existing brush.</summary>
    /// <param name="brush">The brush to use for the stroke.  The pen does not take ownership.</param>
    /// <param name="thickness">Stroke thickness in device-independent pixels.</param>
    /// <param name="strokeStyle">Stroke attributes, or <see langword="null"/> for the default.</param>
    /// <remarks>The caller is responsible for disposing the returned pen (and the brush separately).</remarks>
    IPen CreatePen(IBrush brush, double thickness = 1.0, StrokeStyle? strokeStyle = null) =>
        new Pen(brush, thickness, strokeStyle ?? StrokeStyle.Default);

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
    /// The default DIM returns a <see cref="LinearGradientBrush"/>.
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
        => new LinearGradientBrush(startPoint, endPoint, stops, spreadMethod, units, gradientTransform);

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
    /// The default DIM returns a <see cref="RadialGradientBrush"/>.
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
        => new RadialGradientBrush(center, gradientOrigin, radiusX, radiusY, stops, spreadMethod, units, gradientTransform);

    /// <summary>
    /// Creates an image brush that tiles <paramref name="image"/> to fill a region.
    /// </summary>
    /// <param name="image">Source image supplying the tile.</param>
    /// <param name="sourceRect">Region within <paramref name="image"/> to use as the tile (in DIPs, image coordinates).</param>
    /// <param name="destinationRect">Destination rectangle for one tile (in DIPs, local coordinates).</param>
    /// <param name="tileMode">Tile extension mode beyond <paramref name="destinationRect"/>.</param>
    /// <param name="opacity">Overall opacity multiplier.</param>
    /// <param name="transform">Optional pre-fill transform applied to the tile geometry.</param>
    /// <param name="ownedResources">
    /// Optional disposables whose lifetime is tied to the brush. Use this to transfer ownership
    /// of an offscreen render target and its <see cref="IImage"/> to the returned brush, so that
    /// disposing the brush releases them. Disposed in array order when the brush is disposed.
    /// </param>
    /// <remarks>
    /// The caller is responsible for disposing the returned brush.
    /// </remarks>
    IImageBrush CreateImageBrush(
        IImage image,
        Rect sourceRect,
        Rect destinationRect,
        TileMode tileMode = TileMode.Tile,
        double opacity = 1.0,
        Matrix3x2? transform = null,
        IDisposable[]? ownedResources = null)
        => new ImageBrush(image, sourceRect, destinationRect, tileMode, opacity, transform, ownedResources);

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
    /// The returned context is not yet started; call
    /// <see cref="IGraphicsContext.BeginFrame"/> before drawing.
    /// </summary>
    /// <param name="target">The render target to draw to.</param>
    /// <returns>A new graphics context.</returns>
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
    /// <param name="hasAlpha">
    /// True when the rendered content has a meaningful alpha channel. Default true preserves
    /// alpha-aware blending for arbitrary content. Pass false when the consumer guarantees
    /// every pixel is opaque (video frames, opaque image thumbnails, etc.) — backends use
    /// this to pick <c>ALPHA_MODE.IGNORE</c> over <c>PREMULTIPLIED</c> on the underlying
    /// GPU bitmap, skipping per-fragment blend math, and to bypass alpha scans on CPU
    /// upload paths.
    /// </param>
    /// <returns>A bitmap render target with platform-appropriate resources.</returns>
    IBitmapRenderTarget CreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale = 1.0, bool hasAlpha = true);

    /// <summary>
    /// Creates a render target intended for transient offscreen passes (SVG filter source
    /// layers, scratch buffers, etc.). Backends that support a true GPU pipeline return a
    /// GPU-resident target so subsequent effect / image-filter operations can stay on-GPU
    /// end-to-end (no CPU readback). Backends without a GPU path fall back to the same
    /// surface as <see cref="CreateBitmapRenderTarget"/>.
    /// </summary>
    /// <param name="pixelWidth">Width in pixels.</param>
    /// <param name="pixelHeight">Height in pixels.</param>
    /// <param name="dpiScale">DPI scale factor (default 1.0 for 96 DPI).</param>
    /// <param name="hasAlpha">See <see cref="CreateBitmapRenderTarget"/>.</param>
    /// <remarks>
    /// Default implementation forwards to <see cref="CreateBitmapRenderTarget"/>. The D2D
    /// backend overrides to return a GPU-only <c>ID2D1Bitmap1</c> when the shared device
    /// initializes successfully; the OpenGL / MewVG backend already returns FBO-backed
    /// targets from <see cref="CreateBitmapRenderTarget"/>, so no override is needed.
    /// </remarks>
    IBitmapRenderTarget CreateOffscreenRenderTarget(int pixelWidth, int pixelHeight, double dpiScale = 1.0, bool hasAlpha = true)
        => CreateBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale, hasAlpha);

    /// <summary>
    /// Creates an executor for evaluating <see cref="ImageFilter"/> graphs. The default
    /// returns a CPU reference implementation; backends override to return GPU-accelerated
    /// executors that internally chain CPU fallback for unsupported nodes.
    /// </summary>
    IImageFilterExecutor CreateImageFilterExecutor() => new CpuImageFilterExecutor();

    /// <summary>
    /// Serializes worker-thread <c>SvgFilter</c> offline render units against UI
    /// window frames. The MewVG OpenGL backend overrides this to acquire the same
    /// mutex its UI window <c>BeginFrame</c> / <c>EndFrame</c> path holds, so the
    /// filter offline render and UI render don't overlap on share-listed GL
    /// contexts (which races on Intel iGPU producing intermittent black filter
    /// regions). Backends with thread-free APIs (D2D MULTI_THREADED, Metal, GDI)
    /// return a no-op disposable.
    /// </summary>
    IDisposable AcquireConcurrentRenderUnit() => NoOpScope.Instance;

    /// <summary>
    /// Reserves any per-thread state needed to perform rendering on the calling thread.
    /// Required for backends with thread-affine state (OpenGL contexts must be made
    /// current per thread). Backends with thread-free APIs (D2D MULTI_THREADED, Metal,
    /// GDI memory DCs) return a no-op disposable.
    /// <para/>
    /// Use this from worker threads that call <see cref="CreateContext"/> /
    /// <see cref="CreateOffscreenRenderTarget"/>:
    /// <code>
    /// await Task.Run(() =&gt; {
    ///     using var _ = factory.AcquireBackgroundRenderScope();
    ///     var target = factory.CreateOffscreenRenderTarget(...);
    ///     using var ctx = factory.CreateContext(target);
    ///     // draw...
    /// });
    /// </code>
    /// The MewVG (OpenGL) backend overrides this to activate a hidden-window worker
    /// HGLRC whose textures are share-listed with all window contexts; the resulting
    /// FBO texture is sample-able by the UI thread without readback.
    /// </summary>
    IDisposable AcquireBackgroundRenderScope() => NoOpScope.Instance;

    private sealed class NoOpScope : IDisposable
    {
        public static readonly NoOpScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>
/// Optional capability for factories that must release per-window resources when a window is destroyed.
/// </summary>
public interface IWindowResourceReleaser
{
    void ReleaseWindowResources(nint hwnd);
}
