namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Identifies interchangeable offscreen render surfaces in the scratch pool. Two surfaces with the
/// same key can be swapped for one another because the renter repaints the content it needs.
/// </summary>
/// <param name="PixelWidth">Allocated surface width in pixels.</param>
/// <param name="PixelHeight">Allocated surface height in pixels.</param>
/// <param name="DpiScale">DPI scale the surface was created with.</param>
/// <param name="HasAlpha">Whether the surface carries a per-pixel alpha channel.</param>
/// <param name="ResourceClass">Opaque compatibility class used to prevent cross-purpose reuse.</param>
public readonly record struct ScratchSurfaceKey(
    int PixelWidth,
    int PixelHeight,
    double DpiScale,
    bool HasAlpha,
    ScratchResourceClass ResourceClass = ScratchResourceClass.General);

public enum ScratchResourceClass
{
    General = 0,
    FilterIntermediate,
    AtlasPage,
}

/// <summary>
/// Scratch-surface acquisition for offscreen caches. Callers ask for the size they actually paint;
/// the pool behind <see cref="IRenderDevice.ResourceCache"/> may reuse a larger allocation. The returned
/// logical view always reports the requested dimensions; the allocation size remains internal.
/// </summary>
public static class ScratchSurfaceExtensions
{
    /// <summary>
    /// Returns a surface at least <paramref name="pixelWidth"/> x <paramref name="pixelHeight"/>,
    /// reusing a pooled one when available. Content is undefined. Hand it back via
    /// <see cref="ReleaseScratchSurface"/> instead of disposing.
    /// </summary>
    public static IRenderSurface AcquireScratchSurface(
        this IRenderDevice device,
        int pixelWidth,
        int pixelHeight,
        double dpiScale = 1.0,
        bool hasAlpha = true,
        string? debugName = null)
        => AcquireScratchSurfaceCore(
            device,
            pixelWidth,
            pixelHeight,
            dpiScale,
            hasAlpha,
            debugName,
            ScratchResourceClass.General,
            exactSizeOnly: false);

    internal static IRenderSurface AcquireScratchSurfaceCore(
        IRenderDevice device,
        int pixelWidth,
        int pixelHeight,
        double dpiScale,
        bool hasAlpha,
        string? debugName,
        ScratchResourceClass resourceClass,
        bool exactSizeOnly)
    {
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        var key = new ScratchSurfaceKey(pixelWidth, pixelHeight, dpiScale, hasAlpha, resourceClass);

        IRenderSurface allocation;
        long bytes;
        var pooled = device.ResourceCache?.RentScratchSurface(key, exactSizeOnly);
        if (pooled != null)
        {
            allocation = pooled;
            bytes = RenderResourceMetrics.ScratchBytes(allocation.PixelWidth, allocation.PixelHeight);
            RenderResourceMetrics.ScratchAcquired(bytes, created: false);
        }
        else
        {
            allocation = device.CreateSurface(
                RenderSurfaceDescriptor.CachedImage(pixelWidth, pixelHeight, dpiScale, debugName, hasAlpha));
            bytes = RenderResourceMetrics.ScratchBytes(allocation.PixelWidth, allocation.PixelHeight);
            RenderResourceMetrics.ScratchAcquired(bytes, created: true);
        }
        return new LeasedRenderSurfaceView(device, allocation, pixelWidth, pixelHeight, resourceClass);
    }

    /// <summary>
    /// Hands a surface obtained from <see cref="AcquireScratchSurface"/> back for reuse.
    /// Disposes it when the device has no pool or the pool is over budget.
    /// </summary>
    public static void ReleaseScratchSurface(this IRenderDevice device, IRenderSurface surface)
    {
        if (surface is LeasedRenderSurfaceView lease)
        {
            lease.Dispose();
            return;
        }

        var cache = device.ResourceCache;
        if (cache == null || surface.IsDisposed)
        {
            surface.Dispose();
            return;
        }

        var key = new ScratchSurfaceKey(
            surface.PixelWidth,
            surface.PixelHeight,
            surface.DpiScale,
            surface is Aprillz.MewUI.Resources.IPixelBufferSource pixels
                ? pixels.HasAlpha
                : surface.Capabilities.HasFlag(SurfaceCapabilities.Alpha));
        cache.ReturnScratchSurface(key, surface);
    }
}
