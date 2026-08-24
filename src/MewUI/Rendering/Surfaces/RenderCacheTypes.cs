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

public readonly record struct RenderCacheKey(
    RenderCacheEntryKind Kind,
    int PixelWidth,
    int PixelHeight,
    double DpiScale,
    RenderPixelFormat Format,
    ulong SourceVersion,
    ulong DeviceId,
    string? Scope = null);

public interface IRenderCacheEntry : IDisposable
{
    RenderCacheKey Key { get; }

    IRenderSurface Surface { get; }

    IImage Image { get; }

    IRenderOperation? SafeToDisposeAfter { get; }
}

public interface IRenderResourceCache
{
    bool TryGet(RenderCacheKey key, out IRenderCacheEntry entry);

    IRenderCacheEntry Add(
        RenderCacheKey key,
        IRenderSurface surface,
        IImage image,
        IRenderOperation? safeToDisposeAfter = null);

    void Release(RenderCacheKey key);

    void ReleaseLater(IDisposable resource, IRenderOperation safeAfter);

    void Trim(RenderCacheTrimReason reason);

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
