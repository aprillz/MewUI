using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TextLayoutLifetimeTests
{
    [TestMethod]
    public void ReplacingAndReleasingBackendHandleIsIdempotent()
    {
        var released = new List<nint>();
        var layout = CreateLayout();
        layout.AttachBackendHandle(1, released.Add);
        layout.AttachBackendHandle(2, released.Add);

        CollectionAssert.AreEqual(new nint[] { 1 }, released);
        layout.ReleaseBackendHandle();
        layout.ReleaseBackendHandle();

        CollectionAssert.AreEqual(new nint[] { 1, 2 }, released);
        Assert.AreEqual(0, layout.BackendHandle);
    }

    [TestMethod]
    public void FinalizerReleasesUnownedBackendHandle()
    {
        int releaseCount = 0;
        var weak = CreateUnownedLayout(() => Interlocked.Increment(ref releaseCount));

        for (int attempt = 0; attempt < 5 && weak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.IsFalse(weak.IsAlive);
        Assert.AreEqual(1, releaseCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateUnownedLayout(Action release)
    {
        var layout = CreateLayout();
        layout.AttachBackendHandle(42, _ => release());
        return new WeakReference(layout);
    }

    private static BackendTextLayout CreateLayout()
        => new()
        {
            MeasuredSize = new Size(10, 10),
            EffectiveBounds = new Rect(0, 0, 10, 10),
            EffectiveMaxWidth = 10,
            ContentHeight = 10
        };
}
