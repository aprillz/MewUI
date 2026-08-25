using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Rendering.Gdi;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class ScratchSurfaceSizeTests
{
    [TestMethod]
    public void FilterPool_ReportsActivePooledAndDisposedLifecycle()
    {
        using var factory = new GdiGraphicsFactory();
        using var pool = new ScratchSurfacePool(factory, 1);
        var before = RenderResourceMetrics.Snapshot();
        long bytes = RenderResourceMetrics.ScratchBytes(32, 16);

        var first = pool.RentLease(32, 16);
        var active = RenderResourceMetrics.Snapshot();
        Assert.AreEqual(before.ScratchCreated + 1, active.ScratchCreated);
        Assert.AreEqual(before.ScratchActiveCount + 1, active.ScratchActiveCount);
        Assert.AreEqual(before.ScratchActiveBytes + bytes, active.ScratchActiveBytes);

        pool.Return(first);
        var pooled = RenderResourceMetrics.Snapshot();
        Assert.AreEqual(before.ScratchActiveCount, pooled.ScratchActiveCount);
        Assert.AreEqual(before.ScratchPooledCount + 1, pooled.ScratchPooledCount);
        Assert.AreEqual(before.ScratchPooledBytes + bytes, pooled.ScratchPooledBytes);

        var reused = pool.RentLease(32, 16);
        Assert.AreSame(first, reused);
        var activeAgain = RenderResourceMetrics.Snapshot();
        Assert.AreEqual(before.ScratchCreated + 1, activeAgain.ScratchCreated);
        Assert.AreEqual(before.ScratchActiveCount + 1, activeAgain.ScratchActiveCount);
        Assert.AreEqual(before.ScratchPooledCount, activeAgain.ScratchPooledCount);

        reused.Dispose();
        var disposed = RenderResourceMetrics.Snapshot();
        Assert.AreEqual(before.ScratchActiveCount, disposed.ScratchActiveCount);
        Assert.AreEqual(before.ScratchPooledCount, disposed.ScratchPooledCount);
        Assert.AreEqual(before.ScratchDisposed + 1, disposed.ScratchDisposed);
        Assert.IsTrue(disposed.IsBalanced);
    }

    [TestMethod]
    public void DevicePool_ReusesOversizeWithoutExposingAllocationSize()
    {
        using var factory = new GdiGraphicsFactory();

        var first = factory.AcquireScratchSurface(200, 100);
        var allocation = ((IBackendSurfaceProvider)first).BackendSurface;
        factory.ReleaseScratchSurface(first);

        var reused = factory.AcquireScratchSurface(150, 80);
        using var image = factory.CreateImageView(reused);

        Assert.AreSame(allocation, ((IBackendSurfaceProvider)reused).BackendSurface);
        Assert.AreEqual(150, reused.PixelWidth);
        Assert.AreEqual(80, reused.PixelHeight);
        Assert.AreEqual(150, image.PixelWidth);
        Assert.AreEqual(80, image.PixelHeight);
        factory.ReleaseScratchSurface(reused);
    }

    [TestMethod]
    public void DevicePool_PrefersExactSizeOverNewerOversize()
    {
        using var factory = new GdiGraphicsFactory();

        var exact = factory.AcquireScratchSurface(150, 100);
        var exactAllocation = ((IBackendSurfaceProvider)exact).BackendSurface;
        factory.ReleaseScratchSurface(exact);
        var larger = factory.AcquireScratchSurface(200, 100);
        factory.ReleaseScratchSurface(larger);

        var selected = factory.AcquireScratchSurface(150, 100);

        Assert.AreSame(exactAllocation, ((IBackendSurfaceProvider)selected).BackendSurface);
        factory.ReleaseScratchSurface(selected);
    }

    [TestMethod]
    public void DevicePool_SelectsMostRecentlyReturnedCompatibleOversize()
    {
        using var factory = new GdiGraphicsFactory();

        var older = factory.AcquireScratchSurface(180, 100);
        var olderAllocation = ((IBackendSurfaceProvider)older).BackendSurface;
        factory.ReleaseScratchSurface(older);
        var newer = factory.AcquireScratchSurface(190, 100);
        var newerAllocation = ((IBackendSurfaceProvider)newer).BackendSurface;
        factory.ReleaseScratchSurface(newer);

        var selected = factory.AcquireScratchSurface(150, 100);

        Assert.AreNotSame(olderAllocation, newerAllocation);
        Assert.AreSame(newerAllocation, ((IBackendSurfaceProvider)selected).BackendSurface);
        factory.ReleaseScratchSurface(selected);
    }

    [TestMethod]
    public void DevicePool_RejectsOversizeBeyondAreaLimit()
    {
        using var factory = new GdiGraphicsFactory();

        var larger = factory.AcquireScratchSurface(301, 100);
        var largerAllocation = ((IBackendSurfaceProvider)larger).BackendSurface;
        factory.ReleaseScratchSurface(larger);

        var selected = factory.AcquireScratchSurface(150, 100);

        Assert.AreNotSame(largerAllocation, ((IBackendSurfaceProvider)selected).BackendSurface);
        factory.ReleaseScratchSurface(selected);
    }

    [TestMethod]
    public void DevicePool_ExactOnlyRejectsCompatibleOversize()
    {
        using var factory = new GdiGraphicsFactory();
        var larger = factory.AcquireScratchSurface(200, 100);
        factory.ReleaseScratchSurface(larger);

        var selected = factory.ResourceCache!.RentScratchSurface(
            new ScratchSurfaceKey(150, 100, 1, HasAlpha: true),
            exactSizeOnly: true);

        Assert.IsNull(selected);
    }

    [TestMethod]
    public void DevicePool_ReturnIsExactOnceAndBackendUseAfterReturnFails()
    {
        using var factory = new GdiGraphicsFactory();
        var before = RenderResourceMetrics.Snapshot();
        var surface = factory.AcquireScratchSurface(32, 16);

        factory.ReleaseScratchSurface(surface);
        var returned = RenderResourceMetrics.Snapshot();
        factory.ReleaseScratchSurface(surface);
        var returnedAgain = RenderResourceMetrics.Snapshot();

        Assert.IsTrue(surface.IsDisposed);
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => RenderSurfaceResource.ResolveBackendSurface(surface));
        Assert.AreEqual(returned.ScratchActiveCount, returnedAgain.ScratchActiveCount);
        Assert.AreEqual(returned.ScratchPooledCount, returnedAgain.ScratchPooledCount);
        Assert.AreEqual(before.ScratchActiveCount, returnedAgain.ScratchActiveCount);
    }

    [TestMethod]
    public void DevicePool_RejectsSurfaceThatWasNotAcquiredFromPool()
    {
        using var factory = new GdiGraphicsFactory();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(32, 16, 1));

        Assert.ThrowsExactly<ArgumentException>(() => factory.ReleaseScratchSurface(surface));
        Assert.IsFalse(surface.IsDisposed);
    }

    [TestMethod]
    public void FilterPools_ShareExactSurfacesThroughDeviceCache()
    {
        using var factory = new GdiGraphicsFactory();
        using var firstPool = new ScratchSurfacePool(factory, 1);
        using var secondPool = new ScratchSurfacePool(factory, 1);

        var first = firstPool.RentLease(64, 32);
        var allocation = first.Surface;
        firstPool.Return(first);
        var second = secondPool.RentLease(64, 32);

        Assert.AreSame(allocation, second.Surface);
        second.Dispose();
    }
}
