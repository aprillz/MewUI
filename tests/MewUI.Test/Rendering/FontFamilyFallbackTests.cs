using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class FontFamilyFallbackTests
{
    private const string MISSING = "Definitely Missing Font 123";

    [TestMethod]
    public void Gdi_MissingFirstCandidate_FallsToTheInstalledFamily()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        using var fallback = factory.CreateFont($"{MISSING}, Consolas", 16);
        using var direct = factory.CreateFont("Consolas", 16);

        Assert.AreEqual("Consolas", fallback.Family);
        Assert.AreEqual(direct.Ascent, fallback.Ascent, 0.01);
    }

    [TestMethod]
    public void Gdi_InstalledFirstCandidate_Wins()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        using var font = factory.CreateFont("Consolas, Segoe UI", 16);

        Assert.AreEqual("Consolas", font.Family);
    }

    [TestMethod]
    public void Direct2D_MissingFirstCandidate_FallsToTheInstalledFamily()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        using var fallback = factory.CreateFont($"{MISSING}, Consolas", 16);
        using var direct = factory.CreateFont("Consolas", 16);

        Assert.AreEqual("Consolas", fallback.Family);
        Assert.AreEqual(direct.Ascent, fallback.Ascent, 0.01);
    }

    [TestMethod]
    public void Direct2D_NoCandidateInstalled_KeepsTheFirstName()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        using var font = factory.CreateFont($"{MISSING}, {MISSING} Two", 16);

        Assert.AreEqual(MISSING, font.Family);
    }
}
