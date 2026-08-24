using Aprillz.MewUI.Resources;

namespace MewUI.Test.Resources;

[TestClass]
public sealed class DecodedPixelCacheTests
{
    [TestMethod]
    public void Maintain_EvictsLeastRecentlyUsedRehydratableVariant()
    {
        var cache = new DecodedPixelCache(12, scheduleMaintenance: false);
        var firstOwner = CreateOwner(8);
        var secondOwner = CreateOwner(8);
        var first = new FakeSource();
        var second = new FakeSource();

        cache.Register(first, firstOwner);
        cache.Register(second, secondOwner);
        cache.Maintain();

        Assert.AreEqual(1, first.EvictionCount);
        Assert.AreEqual(0, second.EvictionCount);
        Assert.AreEqual((1, 8L), cache.GetStatistics());

        firstOwner.Release();
        secondOwner.Release();
    }

    [TestMethod]
    public void Maintain_SkipsPinnedVariantAndEvictsNextCandidate()
    {
        var cache = new DecodedPixelCache(12, scheduleMaintenance: false);
        var pinnedOwner = CreateOwner(8);
        var victimOwner = CreateOwner(8);
        var pinned = new FakeSource { IsPinned = true };
        var victim = new FakeSource();

        cache.Register(pinned, pinnedOwner);
        cache.Register(victim, victimOwner);
        cache.Maintain();

        Assert.AreEqual(0, pinned.EvictionCount);
        Assert.AreEqual(1, victim.EvictionCount);
        Assert.AreEqual((1, 8L), cache.GetStatistics());

        pinnedOwner.Release();
        victimOwner.Release();
    }

    private static DecodedPixelOwner CreateOwner(int bytes) =>
        new(new Bgra32PixelBuffer(bytes / 4, 1, new byte[bytes], true));

    private sealed class FakeSource : IDecodedPixelCacheOwner
    {
        public bool IsPinned { get; init; }
        public int EvictionCount { get; private set; }

        public bool TryEvictDecodedPixels(DecodedPixelOwner owner)
        {
            if (IsPinned)
            {
                return false;
            }
            EvictionCount++;
            return true;
        }
    }
}
