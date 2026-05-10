namespace Aprillz.MewUI.Rendering;

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

    public static RenderSurfaceDescriptor CachedImage(
        int pixelWidth,
        int pixelHeight,
        double dpiScale = 1.0,
        string? debugName = null)
        => new(
            pixelWidth,
            pixelHeight,
            dpiScale,
            RenderPixelFormat.Bgra8888Premultiplied,
            SurfaceUsage.Offscreen | SurfaceUsage.ImageSource | SurfaceUsage.CachedImageSource,
            SurfaceCapabilities.Renderable |
            SurfaceCapabilities.GpuSampleable |
            SurfaceCapabilities.CacheableImageSource |
            SurfaceCapabilities.Alpha |
            SurfaceCapabilities.Premultiplied,
            SurfaceLifetimeHint.Cached,
            debugName);

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
