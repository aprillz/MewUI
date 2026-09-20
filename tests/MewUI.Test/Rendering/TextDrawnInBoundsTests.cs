using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// A control that draws its own caption centres it in a box, and the centre of a box is seldom a
/// whole device pixel. Text starting between two pixels comes out softer than the same text in a
/// TextBlock, which layout rounding puts on a pixel, so the two would look different side by side:
/// a menu item next to a list row. Text placed in a box therefore starts on a whole device pixel.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextDrawnInBoundsTests
{
    private const int PIXEL_WIDTH = 360;
    private const int PIXEL_HEIGHT = 90;

    [TestMethod]
    [DataRow("Direct2D", 1.5)]
    [DataRow("Gdi", 1.5)]
    [DataRow("Direct2D", 1.25)]
    [DataRow("Gdi", 1.25)]
    public void TextCentredInABox_StartsOnAWholeDevicePixel(string backend, double scale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("These backends are Windows-only.");
        }

        IGraphicsFactory factory = backend == "Gdi" ? new GdiGraphicsFactory() : new Direct2DGraphicsFactory();
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;

        var style = new TextRunStyle("Segoe UI", 12);
        uint dpi = (uint)Math.Round(96 * scale);
        var layout = TextLayoutOperations.GetOrCreate(factory, "Alpha Gamma Epsilon Theta", dpi, in style, double.PositiveInfinity, double.PositiveInfinity);

        // A row a menu would make: an odd height and a padded left edge, neither on a device pixel.
        var box = new Rect(8.3, 10.2, 220, 27.7);
        double centredY = box.Y + ((box.Height - layout.ContentHeight) * 0.5);
        var snappedOrigin = new Point(LayoutRounding.RoundToPixel(box.X, scale), LayoutRounding.RoundToPixel(centredY, scale));

        byte[] inBox = Draw(factory, scale, context => TextLayoutOperations.DrawInBounds(context, layout, box, Color.FromArgb(255, 30, 30, 30), TextAlignment.Center));
        byte[] onThePixel = Draw(factory, scale, context => context.Text.Draw(layout, snappedOrigin, new TextDrawOptions(Color.FromArgb(255, 30, 30, 30))));

        int differing = 0;
        int largest = 0;
        for (int offset = 0; offset + 3 < inBox.Length; offset += 4)
        {
            int delta = Math.Max(Math.Abs(inBox[offset] - onThePixel[offset]), Math.Max(Math.Abs(inBox[offset + 1] - onThePixel[offset + 1]), Math.Abs(inBox[offset + 2] - onThePixel[offset + 2])));
            if (delta > 0)
            {
                differing++;
                largest = Math.Max(largest, delta);
            }
        }

        Assert.AreEqual(0, differing, $"{backend} at {scale}x: {differing} pixels differ by up to {largest} between text centred in a box and the same text drawn from the nearest whole pixel");
    }

    private static byte[] Draw(IGraphicsFactory factory, double scale, Action<IGraphicsContext> draw)
    {
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(PIXEL_WIDTH, PIXEL_HEIGHT, scale, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            draw(context);
            context.EndFrame();
        }

        var pixels = new byte[PIXEL_WIDTH * PIXEL_HEIGHT * 4];
        Assert.IsTrue(((IRenderDevice)factory).TryReadPixels(surface, pixels, PIXEL_WIDTH * 4));
        return pixels;
    }
}
