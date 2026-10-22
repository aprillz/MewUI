namespace Aprillz.MewUI.Rendering;

/// <summary>Describes the surface an <see cref="IRenderDevice"/> is asked to create.</summary>
/// <param name="PixelWidth">Width in device pixels.</param>
/// <param name="PixelHeight">Height in device pixels.</param>
/// <param name="DpiScale">DPI scale the surface reports as a render target.</param>
/// <param name="Format">Pixel format.</param>
/// <param name="Usage">Usages the surface is created for.</param>
/// <param name="RequiredCapabilities">Capabilities the surface must offer.</param>
/// <param name="LifetimeHint">How long the caller expects to keep the surface; the default is <see cref="SurfaceLifetimeHint.Frame"/>.</param>
/// <param name="DebugName">Optional name for diagnostics.</param>
public readonly record struct RenderSurfaceDescriptor(
    int PixelWidth,
    int PixelHeight,
    double DpiScale,
    RenderPixelFormat Format,
    SurfaceUsage Usage,
    SurfaceCapabilities RequiredCapabilities,
    SurfaceLifetimeHint LifetimeHint = SurfaceLifetimeHint.Frame,
    string? DebugName = null)
{
    /// <summary>Describes a CPU-readable and CPU-writable surface that can also be drawn into and kept as an image source.</summary>
    public static RenderSurfaceDescriptor CpuPixels(
        int pixelWidth,
        int pixelHeight,
        double dpiScale = 1.0,
        bool premultiplied = false,
        string? debugName = null)
        => new(
            pixelWidth,
            pixelHeight,
            dpiScale,
            premultiplied ? RenderPixelFormat.Bgra8888Premultiplied : RenderPixelFormat.Bgra8888,
            SurfaceUsage.Offscreen | SurfaceUsage.ImageSource | SurfaceUsage.ReadbackSource,
            SurfaceCapabilities.Renderable |
            SurfaceCapabilities.CpuReadable |
            SurfaceCapabilities.CpuWritable |
            SurfaceCapabilities.CacheableImageSource |
            SurfaceCapabilities.Alpha |
            (premultiplied ? SurfaceCapabilities.Premultiplied : SurfaceCapabilities.None),
            SurfaceLifetimeHint.Cached,
            debugName);

    /// <summary>Describes a GPU-sampleable offscreen surface whose content is then drawn as an image.</summary>
    public static RenderSurfaceDescriptor Offscreen(
        int pixelWidth,
        int pixelHeight,
        double dpiScale = 1.0,
        bool hasAlpha = true,
        string? debugName = null)
        => new(
            pixelWidth,
            pixelHeight,
            dpiScale,
            RenderPixelFormat.Bgra8888Premultiplied,
            SurfaceUsage.Offscreen | SurfaceUsage.ImageSource,
            SurfaceCapabilities.Renderable |
            SurfaceCapabilities.GpuSampleable |
            SurfaceCapabilities.CacheableImageSource |
            (hasAlpha ? SurfaceCapabilities.Alpha | SurfaceCapabilities.Premultiplied : SurfaceCapabilities.None),
            SurfaceLifetimeHint.Transient,
            debugName);

    /// <summary>
    /// Describes an exact-size intermediate surface for filter graphs; <paramref name="requireCpuFallback"/>
    /// adds CPU readability for nodes the CPU executor evaluates.
    /// </summary>
    public static RenderSurfaceDescriptor FilterIntermediate(
        int pixelWidth,
        int pixelHeight,
        double dpiScale = 1.0,
        bool requireCpuFallback = false,
        string? debugName = null)
        => new(
            pixelWidth,
            pixelHeight,
            dpiScale,
            RenderPixelFormat.Bgra8888Premultiplied,
            SurfaceUsage.Offscreen |
            SurfaceUsage.ImageSource |
            SurfaceUsage.FilterSource |
            SurfaceUsage.FilterIntermediate,
            SurfaceCapabilities.Renderable |
            SurfaceCapabilities.GpuSampleable |
            SurfaceCapabilities.FilterIntermediate |
            SurfaceCapabilities.Alpha |
            SurfaceCapabilities.Premultiplied |
            (requireCpuFallback ? SurfaceCapabilities.CpuReadable | SurfaceCapabilities.DeferredReadback : SurfaceCapabilities.None),
            SurfaceLifetimeHint.Pooled,
            debugName);

    /// <summary>Describes a surface kept across frames as a cached image source.</summary>
    public static RenderSurfaceDescriptor CachedImage(
        int pixelWidth,
        int pixelHeight,
        double dpiScale = 1.0,
        string? debugName = null,
        bool hasAlpha = true)
        => new(
            pixelWidth,
            pixelHeight,
            dpiScale,
            hasAlpha ? RenderPixelFormat.Bgra8888Premultiplied : RenderPixelFormat.Bgra8888,
            SurfaceUsage.Offscreen | SurfaceUsage.ImageSource | SurfaceUsage.CachedImageSource,
            SurfaceCapabilities.Renderable |
            SurfaceCapabilities.GpuSampleable |
            SurfaceCapabilities.CacheableImageSource |
            (hasAlpha ? SurfaceCapabilities.Alpha | SurfaceCapabilities.Premultiplied : SurfaceCapabilities.None),
            SurfaceLifetimeHint.Cached,
            debugName);

    /// <summary>Describes a surface that external code writes through <see cref="IExternalWritableGpuSurface"/> and the backend samples.</summary>
    public static RenderSurfaceDescriptor ExternalGpuWritable(
        int pixelWidth,
        int pixelHeight,
        double dpiScale = 1.0,
        bool hasAlpha = true,
        string? debugName = null)
        => new(
            pixelWidth,
            pixelHeight,
            dpiScale,
            RenderPixelFormat.Bgra8888Premultiplied,
            SurfaceUsage.Offscreen | SurfaceUsage.ImageSource,
            SurfaceCapabilities.GpuSampleable |
            SurfaceCapabilities.CacheableImageSource |
            SurfaceCapabilities.ExternalGpuWritable |
            (hasAlpha ? SurfaceCapabilities.Alpha | SurfaceCapabilities.Premultiplied : SurfaceCapabilities.None),
            SurfaceLifetimeHint.Cached,
            debugName);

    /// <summary>Describes a frame-lifetime staging surface whose pixels the presenter reads back.</summary>
    public static RenderSurfaceDescriptor PresenterIntermediate(
        int pixelWidth,
        int pixelHeight,
        double dpiScale = 1.0,
        bool premultiplied = true,
        string? debugName = null)
        => new(
            pixelWidth,
            pixelHeight,
            dpiScale,
            premultiplied ? RenderPixelFormat.Bgra8888Premultiplied : RenderPixelFormat.Bgra8888,
            SurfaceUsage.Offscreen |
            SurfaceUsage.PresenterIntermediate |
            SurfaceUsage.ReadbackSource,
            SurfaceCapabilities.Renderable |
            SurfaceCapabilities.CpuReadable |
            SurfaceCapabilities.Alpha |
            (premultiplied ? SurfaceCapabilities.Premultiplied : SurfaceCapabilities.None),
            SurfaceLifetimeHint.Frame,
            debugName);
}
