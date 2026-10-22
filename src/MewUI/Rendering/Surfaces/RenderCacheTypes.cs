namespace Aprillz.MewUI.Rendering;

/// <summary>What a <see cref="RenderCacheKey"/> identifies.</summary>
public enum RenderCacheEntryKind
{
    /// <summary>Not classified.</summary>
    Unknown = 0,
    /// <summary>A realized image source.</summary>
    ImageSource,
    /// <summary>The output of a filter graph.</summary>
    FilterResult,
    /// <summary>A tile rendered for a pattern brush.</summary>
    PatternTile,
    /// <summary>A snapshot of a viewport.</summary>
    ViewportSnapshot,
    /// <summary>Staging memory for an upload.</summary>
    UploadStaging,
}

/// <summary>Why a cache is asked to trim.</summary>
public enum RenderCacheTrimReason
{
    /// <summary>An explicit request.</summary>
    Manual = 0,
    /// <summary>The process is under memory pressure.</summary>
    MemoryPressure,
    /// <summary>The backend device was lost, so its resources are invalid.</summary>
    DeviceLost,
    /// <summary>The DPI changed, so DPI-dependent entries are stale.</summary>
    DpiChanged,
    /// <summary>A source the entries derive from changed.</summary>
    SourceInvalidated,
    /// <summary>The cache exceeded its budget.</summary>
    CapacityExceeded,
}

/// <summary>When a cache is asked to maintain itself.</summary>
public enum RenderCacheMaintenanceMode
{
    /// <summary>After a lease was returned.</summary>
    Return = 0,
    /// <summary>Once per frame.</summary>
    Frame,
    /// <summary>While the application is idle.</summary>
    Idle,
    /// <summary>Under memory pressure.</summary>
    MemoryPressure,
    /// <summary>After a window closed.</summary>
    WindowClosed,
    /// <summary>After the backend device was lost.</summary>
    DeviceLost,
    /// <summary>While the device shuts down.</summary>
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

/// <summary>
/// Identifies a resident resource in an <see cref="IRenderResourceCache"/>; keys with the same values name
/// the same content on the same device generation.
/// </summary>
/// <param name="Kind">What the entry holds.</param>
/// <param name="PixelWidth">Width in device pixels.</param>
/// <param name="PixelHeight">Height in device pixels.</param>
/// <param name="DpiScale">DPI scale the content was rendered at.</param>
/// <param name="Format">Pixel format.</param>
/// <param name="SourceVersion">Version of the source the content was derived from.</param>
/// <param name="DeviceId">Device the resource belongs to.</param>
/// <param name="Scope">Optional caller-defined scope that separates entries of the same kind.</param>
/// <param name="DeviceGeneration">Device generation the resource was created in.</param>
/// <param name="ContextId">Context within the device, when the device has several.</param>
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
    /// <summary>Gets the key of the entry.</summary>
    RenderCacheKey Key { get; }

    /// <summary>Gets the resident surface.</summary>
    IRenderSurface Surface { get; }

    /// <summary>Gets the image view over the surface.</summary>
    IImage Image { get; }

    /// <summary>
    /// Gets the operation that must complete before the resource may be disposed, or
    /// <see langword="null"/> when it can be disposed at once.
    /// </summary>
    IRenderOperation? SafeToDisposeAfter { get; }
}

/// <summary>Holds a device's resident resources and its scratch surface pool within a byte and count budget.</summary>
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

    /// <summary>Disposes <paramref name="resource"/> once <paramref name="safeAfter"/> has completed.</summary>
    void ReleaseLater(IDisposable resource, IRenderOperation safeAfter);

    /// <summary>Evicts entries for <paramref name="reason"/>; active leases keep their resources alive.</summary>
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
