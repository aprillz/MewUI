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
        var font = factory.CreateFont("Segoe UI", 16);
        var path = new PathGeometry();

        Assert.IsTrue(((IGlyphOutlineFont)font).TryAppendGlyphOutline(path, 'A', new Point(0, 20), out _));
        Assert.AreNotEqual(0, GetCachedFace(font));

        font.Dispose();

        Assert.AreEqual(0, GetCachedFace(font));
    }

    // The DirectWrite sources are linked into every Win32 backend, so naming the type here would be
    // ambiguous between the assemblies the test references. The font comes from the Direct2D factory,
    // so reading the field off its own type reaches the right copy.
    private static nint GetCachedFace(IFont font)
        => (nint)(font.GetType()
            .GetField("_cachedFontFace", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(font) ?? 0);
}
