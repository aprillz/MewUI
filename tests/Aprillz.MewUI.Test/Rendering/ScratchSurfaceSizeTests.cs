using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Rendering.Gdi;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class ScratchSurfaceSizeTests
{
    [DataRow(1, 16)]
    [DataRow(16, 16)]
    [DataRow(17, 32)]
    [DataRow(256, 256)]
    [DataRow(257, 288)]
    [DataRow(480, 480)]
    [DataRow(567, 576)]
    [DataRow(1025, 1088)]
    [DataRow(1654, 1664)]
    [DataRow(4097, 4224)]
    [TestMethod]
    public void Approximate_UsesTightReusableSizeClasses(int requested, int expected)
    {
        Assert.AreEqual(expected, ScratchSurfaceSize.Approximate(requested));
    }

    [TestMethod]
    public void Approximate_NeverReturnsLessThanRequested()
    {
        for (int requested = 1; requested <= 8192; requested++)
        {
            Assert.IsGreaterThanOrEqualTo(requested, ScratchSurfaceSize.Approximate(requested));
        }
    }

    [TestMethod]
    public void FilterPool_ReportsActivePooledAndDisposedLifecycle()
    {
        using var factory = new GdiGraphicsFactory();
        using var pool = new ScratchSurfacePool(factory, 1);
        var before = RenderMemoryLedger.Snapshot();
        long bytes = RenderMemoryLedger.ScratchBytes(32, 16);

        var first = pool.RentLease(32, 16);
        var active = RenderMemoryLedger.Snapshot();
        Assert.AreEqual(before.ScratchCreated + 1, active.ScratchCreated);
        Assert.AreEqual(before.ScratchActiveCount + 1, active.ScratchActiveCount);
        Assert.AreEqual(before.ScratchActiveBytes + bytes, active.ScratchActiveBytes);

        pool.Return(first);
        var pooled = RenderMemoryLedger.Snapshot();
        Assert.AreEqual(before.ScratchActiveCount, pooled.ScratchActiveCount);
        Assert.AreEqual(before.ScratchPooledCount + 1, pooled.ScratchPooledCount);
        Assert.AreEqual(before.ScratchPooledBytes + bytes, pooled.ScratchPooledBytes);

        var reused = pool.RentLease(32, 16);
        Assert.AreSame(first, reused);
        var activeAgain = RenderMemoryLedger.Snapshot();
        Assert.AreEqual(before.ScratchCreated + 1, activeAgain.ScratchCreated);
        Assert.AreEqual(before.ScratchActiveCount + 1, activeAgain.ScratchActiveCount);
        Assert.AreEqual(before.ScratchPooledCount, activeAgain.ScratchPooledCount);

        reused.Dispose();
        var disposed = RenderMemoryLedger.Snapshot();
        Assert.AreEqual(before.ScratchActiveCount, disposed.ScratchActiveCount);
        Assert.AreEqual(before.ScratchPooledCount, disposed.ScratchPooledCount);
        Assert.AreEqual(before.ScratchDisposed + 1, disposed.ScratchDisposed);
        Assert.IsTrue(disposed.IsBalanced);
    }
}
