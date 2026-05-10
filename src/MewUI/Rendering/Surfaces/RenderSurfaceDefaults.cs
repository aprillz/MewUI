namespace Aprillz.MewUI.Rendering;

internal static class RenderSurfaceDefaults
{
    public static RenderPixelFormat GetBgraFormat(bool premultiplied)
        => premultiplied
            ? RenderPixelFormat.Bgra8888Premultiplied
            : RenderPixelFormat.Bgra8888;

    public static SurfaceUsage PixelSurfaceUsage =>
        SurfaceUsage.Offscreen | SurfaceUsage.ImageSource | SurfaceUsage.ReadbackSource;

    public static SurfaceCapabilities GetPixelSurfaceCapabilities(
        bool premultiplied,
        bool deferredReadback,
        bool gpuSampleable)
    {
        var capabilities =
            SurfaceCapabilities.Renderable |
            SurfaceCapabilities.CpuReadable |
            SurfaceCapabilities.CpuWritable |
            SurfaceCapabilities.Alpha;

        if (premultiplied)
        {
            capabilities |= SurfaceCapabilities.Premultiplied;
        }

        if (deferredReadback)
        {
            capabilities |= SurfaceCapabilities.DeferredReadback;
        }

        if (gpuSampleable)
        {
            capabilities |= SurfaceCapabilities.GpuSampleable;
        }

        return capabilities;
    }

    public static IRenderOperation RequestReadback(bool deferredReadback, Func<byte[]> copyPixels)
    {
        if (deferredReadback)
        {
            _ = copyPixels();
        }

        return RenderOperation.Completed;
    }

    public static bool TryFlushReadback(bool deferredReadback, Func<byte[]> copyPixels)
    {
        if (deferredReadback)
        {
            _ = copyPixels();
        }

        return true;
    }
}
