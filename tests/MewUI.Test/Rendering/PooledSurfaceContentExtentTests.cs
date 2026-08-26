extern alias MewVGWin32;

using OpenGLPixelRenderSurface = MewVGWin32::Aprillz.MewUI.Rendering.OpenGL.OpenGLPixelRenderSurface;

namespace MewUI.Test.Rendering;

/// <summary>
/// Pins the contract the logical-to-physical source mapping depends on: a pooled surface reports
/// the extent of the content last rendered into it, and every writer that covers the whole
/// allocation says so. FBO creation is lazy, so this needs no GL context.
/// </summary>
[TestClass]
public sealed class PooledSurfaceContentExtentTests
{
    [TestMethod]
    public void ContentExtent_DefaultsToTheWholeAllocation()
    {
        var surface = new OpenGLPixelRenderSurface(64, 64, 1.0);

        Assert.AreEqual(64, surface.ContentWidthPx);
        Assert.AreEqual(64, surface.ContentHeightPx);
    }

    [TestMethod]
    public void ContentExtent_TracksTheLastRenderedViewport()
    {
        var surface = new OpenGLPixelRenderSurface(64, 64, 1.0);

        surface.SetContentSize(40, 24);

        Assert.AreEqual(40, surface.ContentWidthPx);
        Assert.AreEqual(24, surface.ContentHeightPx);
    }

    [DataRow(0, 0)]
    [DataRow(-5, -5)]
    [DataRow(999, 999)]
    [TestMethod]
    public void ContentExtent_StaysInsideTheAllocation(int width, int height)
    {
        var surface = new OpenGLPixelRenderSurface(64, 64, 1.0);

        surface.SetContentSize(width, height);

        Assert.IsGreaterThan(0, surface.ContentWidthPx);
        Assert.IsLessThanOrEqualTo(64, surface.ContentWidthPx);
        Assert.IsGreaterThan(0, surface.ContentHeightPx);
        Assert.IsLessThanOrEqualTo(64, surface.ContentHeightPx);
    }

    [TestMethod]
    public void ContentExtent_IsRestoredWhenAWriterCoversTheWholeAllocation()
    {
        var surface = new OpenGLPixelRenderSurface(64, 64, 1.0);
        surface.SetContentSize(40, 24);

        // Mirrors what BeginExternalWrite and UploadToFbo record after writing every texel.
        surface.SetContentSize(surface.PixelWidth, surface.PixelHeight);

        Assert.AreEqual(64, surface.ContentWidthPx);
        Assert.AreEqual(64, surface.ContentHeightPx);
    }
}
