using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;

namespace MewUI.Test.Rendering;

/// <summary>
/// A pooled surface can be larger than the content drawn into it. Drawing its logical image
/// without an explicit source rect used to sample the whole allocation, squashing the content
/// into the destination - the shape reported as a cached NavigationView collapsing vertically
/// after the window shrank.
/// </summary>
[TestClass]
public sealed class PooledSurfaceBlitTests
{
    [TestMethod]
    public void LogicalImageOverOversizedSurface_BlitsOnlyTheContent()
    {
        const int allocation = 64;
        const int content = 32;
        var factory = new GdiGraphicsFactory();

        var backing = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(allocation, allocation, 1.0, "oversized"));
        using (var ctx = factory.CreateContext(backing))
        {
            ctx.BeginFrame((IRenderTarget)backing);
            ctx.Clear(Color.FromArgb(255, 0, 0, 0));
            // Only the logical content region is painted; the rest stays black.
            ctx.FillRectangle(new Rect(0, 0, content, content), Color.FromArgb(255, 0, 0, 255));
            ctx.EndFrame();
        }

        // What the render cache hands out: the allocation hidden behind its logical size.
        using var logical = ImageResource.WrapLogical(factory.CreateImageView(backing), content, content);
        Assert.AreEqual(content, logical.PixelWidth);

        var dest = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(content, content, 1.0, "dest"));
        using (var ctx = factory.CreateContext(dest))
        {
            ctx.BeginFrame((IRenderTarget)dest);
            ctx.Clear(Color.FromArgb(255, 0, 255, 0));
            ctx.DrawImage(logical, new Rect(0, 0, content, content));
            ctx.EndFrame();
        }

        var pixels = ((ICpuPixelSurface)dest).GetReadOnlyPixelSpan();
        int corner = (content - 1) * content * 4 + (content - 1) * 4;
        Assert.IsGreaterThan(200, (int)pixels[corner],
            "the content should still cover the destination corner, not be squashed into a quarter of it");

        (dest as IDisposable)?.Dispose();
        (backing as IDisposable)?.Dispose();
        factory.Dispose();
    }
}
