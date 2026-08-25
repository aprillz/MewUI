using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class TextServicesLifetimeTests
{
    [TestMethod]
    public void ConcurrentEngineLookup_MaterializesOneOwnedInstance()
    {
        using var factory = new GdiGraphicsFactory();
        var engines = new ITextEngine[64];

        Parallel.For(0, engines.Length, i => engines[i] = TextServices.GetEngine(factory));

        Assert.IsTrue(engines.All(engine => ReferenceEquals(engine, engines[0])));
    }

    [TestMethod]
    public void ReleaseWithoutLookup_DoesNotMaterializeEngine()
    {
        using var factory = new GdiGraphicsFactory();

        TextServices.ReleaseIfCreated(factory);

        Assert.ThrowsExactly<ObjectDisposedException>(() => TextServices.GetEngine(factory));
    }

    [TestMethod]
    public void TrimWithoutLookup_DoesNotMaterializeOrDisposeEngine()
    {
        using var factory = new GdiGraphicsFactory();

        TextServices.TrimIfCreated(factory);

        Assert.IsNotNull(TextServices.GetEngine(factory));
    }
}
