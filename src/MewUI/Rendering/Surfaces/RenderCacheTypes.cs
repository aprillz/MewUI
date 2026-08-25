namespace Aprillz.MewUI.Rendering;

public enum RenderCacheEntryKind
{
    Unknown = 0,
    ImageSource,
    FilterResult,
    PatternTile,
    ViewportSnapshot,
    UploadStaging,
}

public enum RenderCacheTrimReason
{
    Manual = 0,
    MemoryPressure,
    DeviceLost,
    DpiChanged,
    SourceInvalidated,
    CapacityExceeded,
}

public enum RenderCacheMaintenanceMode
{
    Return = 0,
    Frame,
    Idle,
    MemoryPressure,
    WindowClosed,
    DeviceLost,
    Shutdown,
}

/// <summary>
/// Backend-owned cache maintenance that cannot be expressed through
/// <see cref="IRenderResourceCache"/> (for example, rasterized text or tessellation caches).
/// Kept internal so consumers never observe backend allocation sizes or native handles.
/// </summary>
internal interface IBackendRenderCacheMaintenance
{
    void TrimBackendCaches(RenderCacheTrimReason reason);

    void MaintainBackendCaches(RenderCacheMaintenanceMode mode);
}

public readonly record struct RenderCacheKey(
    RenderCacheEntryKind Kind,
    int PixelWidth,
    int PixelHeight,
    double DpiScale,
    RenderPixelFormat Format,
    ulong SourceVersion,
    ulong DeviceId,
    string? Scope = null,
    uint DeviceGeneration = 0,
    ulong ContextId = 0)
{
    /// <summary>Returns this content key partitioned for the supplied render device generation.</summary>
    public RenderCacheKey ForDevice(IRenderDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var identity = device.RenderIdentity;
        return this with
        {
            DeviceId = identity.DeviceId,
            DeviceGeneration = identity.Generation,
            ContextId = identity.ContextId,
        };
    }
}

/// <summary>
/// An active view of a cache entry. Disposing the view releases its pin; it does not dispose
/// the underlying resource while that resource remains resident or another view is active.
/// </summary>
public interface IRenderCacheEntry : IDisposable
{
    RenderCacheKey Key { get; }

    IRenderSurface Surface { get; }

    IImage Image { get; }

    IRenderOperation? SafeToDisposeAfter { get; }
}

public interface IRenderResourceCache
{
    /// <summary>Acquires a lease for a completed resident entry.</summary>
    bool TryGet(RenderCacheKey key, out IRenderCacheEntry entry);

    /// <summary>Adds a resident resource and returns its first active lease.</summary>
    IRenderCacheEntry Add(
        RenderCacheKey key,
        IRenderSurface surface,
        IImage image,
        IRenderOperation? safeToDisposeAfter = null);

    /// <summary>
    /// Attempts to admit a completed resource without exceeding the device byte or count budget.
    /// On failure the caller retains ownership of <paramref name="surface"/> and
    /// <paramref name="image"/>.
    /// </summary>
    bool TryAdd(
        RenderCacheKey key,
        IRenderSurface surface,
        IImage image,
        out IRenderCacheEntry entry,
        IRenderOperation? safeToDisposeAfter = null);

    /// <summary>Removes cache residency; active leases keep the resource alive until returned.</summary>
    void Release(RenderCacheKey key);

    void ReleaseLater(IDisposable resource, IRenderOperation safeAfter);

    void Trim(RenderCacheTrimReason reason);

    /// <summary>Drains completed retirements and performs bounded cache maintenance.</summary>
    void Maintain(RenderCacheMaintenanceMode mode = RenderCacheMaintenanceMode.Frame)
        => Trim(mode switch
        {
            RenderCacheMaintenanceMode.MemoryPressure => RenderCacheTrimReason.MemoryPressure,
            RenderCacheMaintenanceMode.DeviceLost => RenderCacheTrimReason.DeviceLost,
            RenderCacheMaintenanceMode.Shutdown => RenderCacheTrimReason.Manual,
            _ => RenderCacheTrimReason.CapacityExceeded,
        });

    /// <summary>
    /// Takes an exact or bounded larger scratch surface compatible with <paramref name="key"/>,
    /// or <see langword="null"/> when the pool holds none. The caller owns the allocation until it
    /// is handed back via <see cref="ReturnScratchSurface"/>; the previous content is undefined.
    /// </summary>
    IRenderSurface? RentScratchSurface(ScratchSurfaceKey key);

    /// <summary>Rents a scratch surface, optionally rejecting larger compatible allocations.</summary>
    IRenderSurface? RentScratchSurface(ScratchSurfaceKey key, bool exactSizeOnly)
        => RentScratchSurface(key);

    /// <summary>
    /// Hands a surface (back) to the scratch pool for later reuse under <paramref name="key"/>.
    /// The pool takes ownership: it disposes the surface when the budget forces eviction or the
    /// cache is disposed. The key must describe the surface's actual allocation.
    /// </summary>
    void ReturnScratchSurface(ScratchSurfaceKey key, IRenderSurface surface);
}
