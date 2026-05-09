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
}
