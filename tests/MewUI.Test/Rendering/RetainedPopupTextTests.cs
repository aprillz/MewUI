using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A popup window carries per-pixel alpha, and the list inside it paints an opaque background of its
/// own. Text over that background has real pixels under it, so it must come out exactly as it does
/// in the main window: the same subpixel antialiasing, whether the frame is drawn whole, replayed
/// from the scene or repainted in part.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedPopupTextTests
{
    private const int WIDTH = 260;
    private const int HEIGHT = 200;
    private static double _scale = 1.0;

    private static int PixelWidth => (int)Math.Round(WIDTH * _scale);

    private static int PixelHeight => (int)Math.Round(HEIGHT * _scale);

    [TestMethod]
    [DataRow("Direct2D", 1.0)]
    [DataRow("Gdi", 1.0)]
    [DataRow("Direct2D", 1.25)]
    [DataRow("Gdi", 1.25)]
    [DataRow("Direct2D", 1.5)]
    [DataRow("Gdi", 1.5)]
    public void TextOverAnOpaqueBackground_LooksTheSameOnASurfaceWithAlpha(string backend, double scale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("These backends are Windows-only.");
        }

        IGraphicsFactory factory = backend == "Gdi" ? new Aprillz.MewUI.Rendering.Gdi.GdiGraphicsFactory() : new Direct2DGraphicsFactory();
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;

        _scale = scale;
        var opaque = Render(factory, transparentWindow: false, out var opaqueList);
        var alpha = Render(factory, transparentWindow: true, out var alphaList);

        Compare(opaque.Reference, alpha.Reference, opaqueList, "drawn straight from the visuals");
        Compare(opaque.Reference, alpha.Whole, opaqueList, "replayed whole from the scene");
        Compare(opaque.Reference, alpha.Partial, opaqueList, "after a partial repaint");
        Assert.AreEqual(opaqueList, alphaList, "the two lists were laid out differently");
    }

    private static (byte[] Reference, byte[] Whole, byte[] Partial) Render(IGraphicsFactory factory, bool transparentWindow, out Rect listBounds)
    {
        var list = new ListBox { Width = 200, Height = 150, Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        list.Items(Enumerable.Range(0, 6).Select(index => $"Popup list item number {index}").ToArray());

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        if (transparentWindow)
        {
            window.AllowsTransparency = true;
            window.Background = Color.Transparent;
        }

        window.SetDpi((uint)Math.Round(96 * _scale));
        window.Content = list;
        window.PerformLayout();
        listBounds = list.Bounds;

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(PixelWidth, PixelHeight, _scale, hasAlpha: transparentWindow));
        window.RenderReferenceFrameToSurface(reference);
        byte[] referencePixels = Read(factory, reference);

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(PixelWidth, PixelHeight, _scale, hasAlpha: transparentWindow));
        window.RenderFrameToSurface(surface);
        window.RenderFrameToSurface(surface);
        byte[] wholePixels = Read(factory, surface);

        // Selecting and unselecting a row repaints that row in part and ends at the same picture.
        list.SelectedIndex = 2;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        list.SelectedIndex = -1;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        byte[] partialPixels = Read(factory, surface);

        return (referencePixels, wholePixels, partialPixels);
    }

    private static byte[] Read(IGraphicsFactory factory, IRenderSurface surface)
    {
        var pixels = new byte[PixelWidth * PixelHeight * 4];
        Assert.IsTrue(((IRenderDevice)factory).TryReadPixels(surface, pixels, PixelWidth * 4));
        return pixels;
    }

    private static void Compare(byte[] expected, byte[] actual, Rect listBounds, string label)
    {
        // Inside the list, away from its rounded border, every pixel sits on the list's own background.
        int left = (int)(listBounds.X * _scale) + 8;
        int top = (int)(listBounds.Y * _scale) + 8;
        int right = (int)(listBounds.Right * _scale) - 8;
        int bottom = (int)(listBounds.Bottom * _scale) - 8;
        int differing = 0;
        int largest = 0;
        int colored = 0;
        int expectedColored = 0;
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int offset = ((y * PixelWidth) + x) * 4;
                int delta = Math.Max(Math.Abs(expected[offset] - actual[offset]), Math.Max(Math.Abs(expected[offset + 1] - actual[offset + 1]), Math.Abs(expected[offset + 2] - actual[offset + 2])));
                if (delta > 0)
                {
                    differing++;
                    largest = Math.Max(largest, delta);
                }

                // A pixel whose channels are spread differently from the background carries subpixel colour.
                int backgroundOffset = ((top * PixelWidth) + left) * 4;
                int spread = (actual[offset + 2] - actual[offset]) - (actual[backgroundOffset + 2] - actual[backgroundOffset]);
                int expectedSpread = (expected[offset + 2] - expected[offset]) - (expected[backgroundOffset + 2] - expected[backgroundOffset]);
                if (Math.Abs(spread) > 8)
                {
                    colored++;
                }

                if (Math.Abs(expectedSpread) > 8)
                {
                    expectedColored++;
                }
            }
        }

        Assert.AreEqual(0, differing, $"{label}: {differing} pixels of the list differ from the main-window rendering by up to {largest}; subpixel-coloured pixels: {colored} here, {expectedColored} in the main window");
    }
}
