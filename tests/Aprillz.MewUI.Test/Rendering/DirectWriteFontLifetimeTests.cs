using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class DirectWriteFontLifetimeTests
{
    [TestMethod]
    public void Dispose_ReleasesCachedFontFace()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DirectWrite is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        var font = (DirectWriteFont)factory.CreateFont("Segoe UI", 16);
        var path = new PathGeometry();

        Assert.IsTrue(font.TryAppendGlyphOutline(path, 'A', new Point(0, 20), out _));
        Assert.AreNotEqual(0, GetCachedFace(font));

        font.Dispose();

        Assert.AreEqual(0, GetCachedFace(font));
    }

    private static nint GetCachedFace(DirectWriteFont font)
        => (nint)(typeof(DirectWriteFont)
            .GetField("_cachedFontFace", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(font) ?? 0);
}
