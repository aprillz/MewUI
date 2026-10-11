using Aprillz.MewUI;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class TestBackendSessionTests
{
    [TestMethod]
    public void NestedSessions_RestoreTheOuterAndOriginalFactory()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        var original = Application.DefaultGraphicsFactory;
        using (var outer = TestBackendSession.Open(TestBackend.Gdi))
        {
            Assert.AreSame(outer.Factory, Application.DefaultGraphicsFactory);
            using (var inner = TestBackendSession.Open(TestBackend.Gdi))
            {
                Assert.AreSame(inner.Factory, Application.DefaultGraphicsFactory);
                Assert.AreNotSame(outer.Factory, inner.Factory);
            }
            Assert.AreSame(outer.Factory, Application.DefaultGraphicsFactory);
        }
        Assert.AreSame(original, Application.DefaultGraphicsFactory);
    }

    [TestMethod]
    public void DisposingAgain_DoesNotReplaceAnActiveSessionFactory()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var first = TestBackendSession.Open(TestBackend.Gdi);
        first.Dispose();
        using var active = TestBackendSession.Open(TestBackend.Gdi);
        first.Dispose();
        Assert.AreSame(active.Factory, Application.DefaultGraphicsFactory);
    }
}
