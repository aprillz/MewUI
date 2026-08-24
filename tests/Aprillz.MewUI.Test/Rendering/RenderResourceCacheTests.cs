using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class RenderResourceCacheTests
{
    [TestMethod]
    public void PersistentCache_UsesLeaseSafeLruHysteresis()
    {
        using var cache = new RenderResourceCache(400 * 1024, 100);
        var a = Add(cache, Key(1), out var surfaceA);
        a.Dispose();
        Add(cache, Key(2), out var surfaceB).Dispose();
        Add(cache, Key(3), out var surfaceC).Dispose();
        Assert.IsTrue(cache.TryGet(Key(1), out var touchedA));
        touchedA.Dispose();

        Add(cache, Key(4), out var surfaceD).Dispose();

        Assert.IsFalse(surfaceA.IsDisposed);
        Assert.IsTrue(surfaceB.IsDisposed);
        Assert.IsTrue(surfaceC.IsDisposed);
        Assert.IsFalse(surfaceD.IsDisposed);
        Assert.IsTrue(cache.TryGet(Key(1), out var retainedA));
        retainedA.Dispose();
    }

    [TestMethod]
    public void PersistentCache_NeverDisposesActiveLeasesDuringPressure()
    {
        using var cache = new RenderResourceCache(1, 1);
        var first = Add(cache, Key(1), out var firstSurface);
        var second = Add(cache, Key(2), out var secondSurface);

        cache.Maintain(RenderCacheMaintenanceMode.MemoryPressure);

        Assert.IsFalse(firstSurface.IsDisposed);
        Assert.IsFalse(secondSurface.IsDisposed);
        first.Dispose();
        second.Dispose();
        Assert.IsFalse(firstSurface.IsDisposed);
        Assert.IsFalse(secondSurface.IsDisposed);
        cache.Maintain(RenderCacheMaintenanceMode.MemoryPressure);
        Assert.IsTrue(firstSurface.IsDisposed);
        Assert.IsTrue(secondSurface.IsDisposed);
    }

    [TestMethod]
    public void PersistentCache_PartitionsDeviceGenerationAndContext()
    {
        using var cache = new RenderResourceCache();
        var generationOne = Key(1) with { DeviceGeneration = 1, ContextId = 7 };
        var generationTwo = generationOne with { DeviceGeneration = 2 };
        Add(cache, generationOne, out _).Dispose();

        Assert.IsTrue(cache.TryGet(generationOne, out var matching));
        matching.Dispose();
        Assert.IsFalse(cache.TryGet(generationTwo, out _));
    }

    [TestMethod]
    public void PersistentCache_WaitsForLeaseAndBackendCompletionBeforeDisposal()
    {
        using var cache = new RenderResourceCache();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = RenderOperation.FromTask(completion.Task);
        var lease = Add(cache, Key(1), out var surface, operation);

        cache.Release(Key(1));
        lease.Dispose();
        cache.Maintain();
        Assert.IsFalse(surface.IsDisposed);

        completion.SetResult();
        cache.Maintain();
        Assert.IsTrue(surface.IsDisposed);
    }

    [TestMethod]
    public void FrameMaintenance_EvictsAtMostThirtyTwoResources()
    {
        using var cache = new RenderResourceCache(1, 100);
        var leases = new List<IRenderCacheEntry>();
        for (ulong i = 1; i <= 40; i++)
        {
            leases.Add(Add(cache, Key(i), out _));
        }
        foreach (var lease in leases)
        {
            lease.Dispose();
        }

        cache.Maintain(RenderCacheMaintenanceMode.Frame);

        Assert.AreEqual(8, cache.GetStatistics().PersistentCount);
        cache.Maintain(RenderCacheMaintenanceMode.Frame);
        Assert.AreEqual(0, cache.GetStatistics().PersistentCount);
    }

    private static IRenderCacheEntry Add(
        RenderResourceCache cache,
        RenderCacheKey key,
        out FakeSurface surface,
        IRenderOperation? operation = null)
    {
        surface = new FakeSurface(64, 64);
        return cache.Add(key, surface, new FakeImage(64, 64), operation);
    }

    private static RenderCacheKey Key(ulong version) => new(
        RenderCacheEntryKind.FilterResult,
        64,
        64,
        1,
        RenderPixelFormat.Bgra8888Premultiplied,
        version,
        DeviceId: 42);

    private sealed class FakeSurface(int width, int height) : IRenderSurface
    {
        public int PixelWidth => width;
        public int PixelHeight => height;
        public double DpiScale => 1;
        public RenderPixelFormat Format => RenderPixelFormat.Bgra8888Premultiplied;
        public SurfaceUsage Usage => SurfaceUsage.Offscreen;
        public SurfaceCapabilities Capabilities => SurfaceCapabilities.Renderable;
        public ulong Version => 0;
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class FakeImage(int width, int height) : IImage
    {
        public int PixelWidth => width;
        public int PixelHeight => height;
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}
