extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// Measures the persistent-frame capability on real offscreen surfaces: with preservation on, a
/// second frame that draws one small rectangle must leave every other pixel of the first frame
/// untouched, and the dirty-erase entry points must overwrite exactly the rectangle they are
/// given. Without preservation the backend clears at BeginFrame and partial repaint is impossible.
/// </summary>
[TestClass]
public sealed class PersistentFrameSurfaceTests
{
    private const int WIDTH = 32;
    private const int HEIGHT = 16;
    private const int DIRTY_X = 8;
    private const int DIRTY_Y = 4;
    private const int DIRTY_WIDTH = 8;
    private const int DIRTY_HEIGHT = 6;

    private static readonly Color _firstFrameColor = Color.FromArgb(255, 200, 30, 30);
    private static readonly Color _secondFrameColor = Color.FromArgb(255, 20, 40, 220);
    private static readonly Color _eraseColor = Color.FromArgb(255, 10, 180, 60);

    [TestMethod]
    public void GdiSurface_PreservesTheFirstFrameOutsideTheRedrawnRectangle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertPreservesUntouchedPixels(factory);
    }

    [TestMethod]
    public void Direct2DSurface_PreservesTheFirstFrameOutsideTheRedrawnRectangle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Direct2D backend is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        AssertPreservesUntouchedPixels(factory);
    }

    [TestMethod]
    public void GdiContext_ErasesOnlyTheDirtyRectangle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertDirtyErase(factory);
    }

    [TestMethod]
    public void Direct2DContext_ErasesOnlyTheDirtyRectangle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Direct2D backend is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        AssertDirtyErase(factory);
    }

    [TestMethod]
    [DoNotParallelize]
    public void MewVGWin32Surface_PreservesTheFirstFrameOutsideTheRedrawnRectangle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The MewVG Win32 backend is Windows-only.");
            return;
        }

        using var factory = new MewVGWin32GraphicsFactory();
        using var backgroundScope = factory.AcquireBackgroundRenderScope();
        AssertPreservesUntouchedPixels(factory);
    }

    [TestMethod]
    [DoNotParallelize]
    public void MewVGWin32Context_ErasesOnlyTheDirtyRectangle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The MewVG Win32 backend is Windows-only.");
            return;
        }

        using var factory = new MewVGWin32GraphicsFactory();
        using var backgroundScope = factory.AcquireBackgroundRenderScope();
        AssertDirtyErase(factory);
    }

    private static void AssertPreservesUntouchedPixels(IGraphicsFactory factory)
    {
        var persistentFactory = factory as IPersistentFrameGraphicsFactory;
        Assert.IsNotNull(persistentFactory, $"{factory.Backend} does not declare persistent-frame support.");
        Assert.IsTrue(
            persistentFactory.IsPersistentFrameRenderingVerified,
            $"{factory.Backend} declares persistent-frame support but has not verified it.");

        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1.0, "persistent-frame"));
        var persistentSurface = surface as IPersistentFrameSurface;
        Assert.IsNotNull(persistentSurface, $"{factory.Backend} offscreen surfaces cannot preserve contents.");

        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.FillRectangle(new Rect(0, 0, WIDTH, HEIGHT), _firstFrameColor);
            context.EndFrame();
        }

        persistentSurface.PreserveContentsOnBeginFrame = true;
        using (persistentFactory.AcquirePersistentFrameRenderScope())
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.FillRectangle(
                new Rect(DIRTY_X, DIRTY_Y, DIRTY_WIDTH, DIRTY_HEIGHT),
                _secondFrameColor);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;

        AssertPixel(pixels, stride, DIRTY_X + 2, DIRTY_Y + 2, _secondFrameColor, "the redrawn rectangle");
        AssertPixel(pixels, stride, 1, 1, _firstFrameColor, "the top-left corner of frame one");
        AssertPixel(pixels, stride, WIDTH - 2, HEIGHT - 2, _firstFrameColor, "the bottom-right corner of frame one");
        AssertPixel(pixels, stride, DIRTY_X - 2, DIRTY_Y + 2, _firstFrameColor, "the pixels left of the redraw");
        AssertPixel(
            pixels,
            stride,
            DIRTY_X + DIRTY_WIDTH + 1,
            DIRTY_Y + 2,
            _firstFrameColor,
            "the pixels right of the redraw");
    }

    private static void AssertDirtyErase(IGraphicsFactory factory)
    {
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1.0, "dirty-erase"));
        var persistentSurface = surface as IPersistentFrameSurface;
        Assert.IsNotNull(persistentSurface, $"{factory.Backend} offscreen surfaces cannot preserve contents.");

        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.FillRectangle(new Rect(0, 0, WIDTH, HEIGHT), _firstFrameColor);
            context.EndFrame();
        }

        persistentSurface.PreserveContentsOnBeginFrame = true;
        var dirtyRect = new Rect(DIRTY_X, DIRTY_Y, DIRTY_WIDTH, DIRTY_HEIGHT);
        using (var context = factory.CreateContext(surface))
        {
            var opaqueDirty = context as IOpaqueDirtyRectContext;
            Assert.IsNotNull(opaqueDirty, $"{factory.Backend} contexts cannot erase an opaque rectangle.");
            var transparentDirty = context as ITransparentDirtyRectContext;
            Assert.IsNotNull(transparentDirty, $"{factory.Backend} contexts cannot erase to transparent.");

            context.BeginFrame(surface);
            opaqueDirty.ClearRectangle(dirtyRect, _eraseColor);
            transparentDirty.ClearRectangleToTransparent(new Rect(0, 0, 4, 4));
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;

        AssertPixel(pixels, stride, DIRTY_X + 2, DIRTY_Y + 2, _eraseColor, "the opaque erase");
        AssertPixel(pixels, stride, DIRTY_X - 2, DIRTY_Y + 2, _firstFrameColor, "the pixels left of the erase");
        AssertPixel(pixels, stride, WIDTH - 2, HEIGHT - 2, _firstFrameColor, "the pixels outside both erases");

        int transparentOffset = 1 * stride + 1 * 4;
        Assert.AreEqual(
            0,
            (int)pixels[transparentOffset + 3],
            $"{factory.Backend} left alpha behind inside the transparent erase.");
    }

    private static void AssertPixel(
        ReadOnlySpan<byte> pixels,
        int stride,
        int column,
        int row,
        Color expected,
        string what)
    {
        int offset = row * stride + column * 4;
        int blue = pixels[offset + 0];
        int green = pixels[offset + 1];
        int red = pixels[offset + 2];
        int alpha = pixels[offset + 3];
        Assert.IsTrue(
            Math.Abs(blue - expected.B) <= 2 &&
            Math.Abs(green - expected.G) <= 2 &&
            Math.Abs(red - expected.R) <= 2 &&
            Math.Abs(alpha - expected.A) <= 2,
            $"Expected {what} at ({column},{row}) to be " +
            $"B={expected.B} G={expected.G} R={expected.R} A={expected.A}, " +
            $"but read B={blue} G={green} R={red} A={alpha}.");
    }
}
