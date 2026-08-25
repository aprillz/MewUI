namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Internal bridge for plan 03. It owns page allocation lifetime and budget admission only;
/// slot packing, content keys, fragmentation, and batching stay in the atlas implementation.
/// </summary>
internal sealed class AtlasPageResourceAdapter
{
    internal const int DefaultPageExtent = 1024;

    private readonly IRenderDevice _device;
    private readonly RenderDeviceIdentity _identity;

    public AtlasPageResourceAdapter(IRenderDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _identity = device.RenderIdentity;
    }

    public bool TryAcquire(string contentClass, out AtlasPageLease? lease)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentClass);
        lease = null;
        if (_device.RenderIdentity != _identity || _device.ResourceCache is not RenderResourceCache cache)
        {
            return false;
        }

        long bytes = RenderResourceMetrics.ScratchBytes(DefaultPageExtent, DefaultPageExtent);
        if (!cache.TryReserveAtlasPage(bytes))
        {
            return false;
        }

        try
        {
            var surface = ScratchSurfaceExtensions.AcquireScratchSurfaceCore(
                _device,
                DefaultPageExtent,
                DefaultPageExtent,
                dpiScale: 1,
                hasAlpha: true,
                debugName: $"AtlasPage:{contentClass}",
                ScratchResourceClass.AtlasPage,
                exactSizeOnly: true);
            lease = new AtlasPageLease(_device, cache, surface, contentClass, bytes, _identity);
            RenderResourceMetrics.AtlasPageAdded(bytes);
            return true;
        }
        catch
        {
            cache.ReleaseAtlasPageReservation(bytes);
            throw;
        }
    }
}

internal sealed class AtlasPageLease : IDisposable
{
    private IRenderDevice? _device;
    private RenderResourceCache? _cache;
    private IRenderSurface? _surface;
    private readonly long _bytes;

    internal AtlasPageLease(
        IRenderDevice device,
        RenderResourceCache cache,
        IRenderSurface surface,
        string contentClass,
        long bytes,
        RenderDeviceIdentity identity)
    {
        _device = device;
        _cache = cache;
        _surface = surface;
        ContentClass = contentClass;
        _bytes = bytes;
        Identity = identity;
    }

    public string ContentClass { get; }
    public RenderDeviceIdentity Identity { get; }
    public IRenderSurface Surface => Volatile.Read(ref _surface)
        ?? throw new ObjectDisposedException(nameof(AtlasPageLease));

    public void Dispose()
    {
        var surface = Interlocked.Exchange(ref _surface, null);
        var device = Interlocked.Exchange(ref _device, null);
        var cache = Interlocked.Exchange(ref _cache, null);
        if (surface == null || device == null || cache == null)
        {
            return;
        }
        cache.ReleaseAtlasPageReservation(_bytes);
        RenderResourceMetrics.AtlasPageRemoved(_bytes);
        device.ReleaseScratchSurface(surface);
    }
}
