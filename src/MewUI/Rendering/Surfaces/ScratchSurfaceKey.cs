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
            bytes = RenderMemoryLedger.ScratchBytes(allocation.PixelWidth, allocation.PixelHeight);
            RenderMemoryLedger.ScratchAcquired(bytes, created: false);
        }
        else
        {
            allocation = device.CreateSurface(
                RenderSurfaceDescriptor.CachedImage(pixelWidth, pixelHeight, dpiScale, debugName, hasAlpha));
            bytes = RenderMemoryLedger.ScratchBytes(allocation.PixelWidth, allocation.PixelHeight);
            RenderMemoryLedger.ScratchAcquired(bytes, created: true);
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

/// <summary>
/// Rounds requested surface dimensions up so that a resize sweep asks for a handful of distinct
/// sizes instead of a new one every frame, which is what makes the scratch pool hit.
/// </summary>
public static class ScratchSurfaceSize
{
    // Below this, a surface is not worth sizing precisely.
    private const int MIN_EXTENT = 16;

    /// <summary>Rounds one axis up to the nearest allocation step.</summary>
    public static int Approximate(int extent)
    {
        if (extent <= MIN_EXTENT)
        {
            return MIN_EXTENT;
        }

        // Fixed quanta keep nearby resize requests reusable without the 40-100% area waste of
        // independently rounding both axes to powers of two. Larger surfaces use a wider quantum,
        // keeping the number of pool keys bounded while capping per-axis slack at roughly 6%.
        int quantum = extent switch
        {
            <= 256 => 16,
            <= 1024 => 32,
            <= 4096 => 64,
            _ => 128,
        };
        long rounded = ((long)extent + quantum - 1) / quantum * quantum;
        return rounded > int.MaxValue ? int.MaxValue : (int)rounded;
    }
}
