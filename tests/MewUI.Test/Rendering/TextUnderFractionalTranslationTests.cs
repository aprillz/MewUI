using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A popup window draws a subtree of its owner under a translation that takes the owner's coordinates
/// to its own. At 150% a whole device pixel is two thirds of a layout unit, so that translation and
/// the text under it are both fractions in layout units while their difference is a whole pixel.
/// Text drawn that way has to come out as sharp as text drawn with whole layout units.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextUnderFractionalTranslationTests
{
    private const double SCALE = 1.5;
    private const int PIXEL_WIDTH = 300;
    private const int PIXEL_HEIGHT = 90;

    [TestMethod]
    [DataRow("Direct2D")]
    [DataRow("Gdi")]
    public void TextOnAWholePixel_IsTheSameUnderAWholeAndAFractionalTranslation(string backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("These backends are Windows-only.");
        }

        IGraphicsFactory factory = backend == "Gdi" ? new GdiGraphicsFactory() : new Direct2DGraphicsFactory();
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;

        // 60 device pixels is 40 layout units; 61 and 62 are 40 2/3 and 41 1/3. The text lands on device row 10 every time.
        byte[] whole = Draw(factory, translationPixels: 60);
        foreach (int translation in new[] { 0, 3, 61, 62, 63, 66 })
        {
            Assert.AreEqual(0, Count(whole, Draw(factory, translation)), $"{backend}: text drawn under a translation of {translation} device pixels ({translation / SCALE:0.00} layout units) differs from text under 60");
        }
    }

    private static int Count(byte[] first, byte[] second)
    {
        int differing = 0;
        for (int offset = 0; offset + 3 < first.Length; offset += 4)
        {
            if (first[offset] != second[offset] || first[offset + 1] != second[offset + 1] || first[offset + 2] != second[offset + 2])
            {
                differing++;
            }
        }

        return differing;
    }

    private static byte[] Draw(IGraphicsFactory factory, int translationPixels)
    {
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(PIXEL_WIDTH, PIXEL_HEIGHT, SCALE, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            double translation = translationPixels / SCALE;
            context.Translate(0, -translation);

            var style = new Aprillz.MewUI.Text.TextRunStyle("Segoe UI", 12);
            uint dpi = (uint)Math.Round(96 * SCALE);
            var layout = Aprillz.MewUI.Text.TextLayoutOperations.GetOrCreate(factory, "Alpha Gamma Epsilon Theta", dpi, in style, double.PositiveInfinity, double.PositiveInfinity);
            double y = (translationPixels + 10) / SCALE;
            context.Text.Draw(layout, new Point(8 / SCALE, y), new Aprillz.MewUI.Text.TextDrawOptions(Color.FromArgb(255, 30, 30, 30)));
            context.EndFrame();
        }

        var pixels = new byte[PIXEL_WIDTH * PIXEL_HEIGHT * 4];
        Assert.IsTrue(((IRenderDevice)factory).TryReadPixels(surface, pixels, PIXEL_WIDTH * 4));
        return pixels;
    }
}
