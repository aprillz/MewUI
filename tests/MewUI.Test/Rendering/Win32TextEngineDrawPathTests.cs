extern alias gdibackend;

using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

using GdiDirectWriteFonts = gdibackend::Aprillz.MewUI.Rendering.DirectWrite.DirectWriteFontFactory;
using GdiFaceCoverageCache = gdibackend::Aprillz.MewUI.Rendering.Gdi.Core.FaceCoverageCache;

namespace Aprillz.MewUI.Test.Rendering;

/// <summary>
/// Drives the GDI backend's draw path with a DirectWrite font, the pairing the
/// <c>MewUIWin32TextEngine</c> switch creates. A rasterizer test alone cannot catch a draw path that
/// turns the font away before it ever rasterizes, which is what a plain type check does.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class Win32TextEngineDrawPathTests
{
    private const int WIDTH_PX = 220;
    private const int HEIGHT_PX = 40;
    private const string TEXT = "Hamburgefonstiv";

    [TestMethod]
    public void GdiBackend_DrawsWithADirectWriteFont()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 text engines are Windows-only.");
            return;
        }

        using var gdi = new GdiGraphicsFactory();
        // The GDI backend compiles its own copy of the DirectWrite sources, so the font has to come
        // from that copy: a face from another backend implements that backend's IWin32TextFace.
        using var fonts = new GdiDirectWriteFonts();
        using var font = fonts.CreateFont("Segoe UI", 16, FontWeight.Normal, false, false, false, 96);

        Assert.IsGreaterThan(0, InkedColumns(gdi, font),
            "The GDI backend drew nothing for a DirectWrite font.");
    }

    [TestMethod]
    public void GdiBackend_DrawsWithItsOwnFont()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 text engines are Windows-only.");
            return;
        }

        using var gdi = new GdiGraphicsFactory();
        using var font = gdi.CreateFont("Segoe UI", 16);

        Assert.IsGreaterThan(0, InkedColumns(gdi, font));
    }

    /// <summary>
    /// Subpixel antialiasing spreads one glyph edge across the three channels, so an edge pixel
    /// comes back with unequal R, G and B. Averaging the channels into one coverage value, which a
    /// single-alpha bitmap forces, leaves every pixel grey.
    /// </summary>
    [TestMethod]
    public void GdiBackend_KeepsSubpixelAntialiasingOnAnOpaqueSurface()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 text engines are Windows-only.");
            return;
        }

        using var gdi = new GdiGraphicsFactory();
        using var fonts = new GdiDirectWriteFonts();
        using var font = fonts.CreateFont("Segoe UI", 16, FontWeight.Normal, false, false, false, 96);

        Assert.IsGreaterThan(0, ColourFringedPixels(gdi, font),
            "The DirectWrite path dropped subpixel coverage on a surface whose pixels are known.");
    }

    /// <summary>
    /// The GDI backend has no texture to keep a rasterized run in, so a face that rasterizes itself
    /// would lay the run out and render its glyphs on every draw. The coverage of a run is kept
    /// from the second time it is drawn: after that, drawing it again, in any colour, only blends it.
    /// </summary>
    [TestMethod]
    public void GdiBackend_KeepsTheCoverageOfARunDrawnTwice_AndBlendsItAfterwards()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 text engines are Windows-only.");
            return;
        }

        using var gdi = new GdiGraphicsFactory();
        using var fonts = new GdiDirectWriteFonts();
        using var font = fonts.CreateFont("Segoe UI", 17, FontWeight.Normal, false, false, false, 96);

        GdiFaceCoverageCache.Clear();
        int missesBefore = GdiFaceCoverageCache.Misses;
        int hitsBefore = GdiFaceCoverageCache.Hits;

        byte[] first = Render(gdi, font, static (pixels, _) => pixels.ToArray(), Color.FromArgb(255, 0, 0, 0));
        Render(gdi, font, static (pixels, _) => 0, Color.FromArgb(255, 0, 0, 0));
        byte[] third = Render(gdi, font, static (pixels, _) => pixels.ToArray(), Color.FromArgb(255, 0, 0, 0));
        byte[] recoloured = Render(gdi, font, static (pixels, _) => pixels.ToArray(), Color.FromArgb(255, 200, 30, 30));

        Assert.AreEqual(2, GdiFaceCoverageCache.Misses - missesBefore, "a run drawn for the second time was not kept");
        Assert.AreEqual(2, GdiFaceCoverageCache.Hits - hitsBefore, "drawing the run again did not find what was kept");
        CollectionAssert.AreEqual(first, third, "a run blended from kept coverage differs from the run that was rasterized");
        CollectionAssert.AreNotEqual(first, recoloured, "the kept coverage carried the first colour with it");
    }

    private static int ColourFringedPixels(IGraphicsFactory factory, IFont font)
        => Render(factory, font, static (pixels, stride) =>
        {
            int fringed = 0;
            for (int y = 0; y < HEIGHT_PX; y++)
            {
                for (int x = 0; x < WIDTH_PX; x++)
                {
                    int i = (y * stride) + (x * 4);
                    int blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
                    if (Math.Max(Math.Abs(red - blue), Math.Max(Math.Abs(red - green), Math.Abs(green - blue))) > 12)
                    {
                        fringed++;
                    }
                }
            }

            return fringed;
        });

    private static int InkedColumns(IGraphicsFactory factory, IFont font)
        => Render(factory, font, static (pixels, stride) =>
        {
            int columns = 0;
            for (int x = 0; x < WIDTH_PX; x++)
            {
                for (int y = 0; y < HEIGHT_PX; y++)
                {
                    if (pixels[(y * stride) + (x * 4) + 1] < 160)
                    {
                        columns++;
                        break;
                    }
                }
            }

            return columns;
        });

    private delegate TResult PixelReader<TResult>(ReadOnlySpan<byte> pixels, int stride);

    private static int Render(IGraphicsFactory factory, IFont font, PixelReader<int> read)
        => Render(factory, font, read, Color.FromArgb(255, 0, 0, 0));

    private static TResult Render<TResult>(IGraphicsFactory factory, IFont font, PixelReader<TResult> read, Color color)
    {
        // No alpha: the pixels under the run are known, which is what subpixel blending needs.
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(WIDTH_PX, HEIGHT_PX, 1.0, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.FromArgb(255, 255, 255, 255));

            var format = new BackendTextFormat
            {
                Font = font,
                HorizontalAlignment = TextAlignment.Left,
                VerticalAlignment = TextAlignment.Top,
                Wrapping = TextWrapping.NoWrap,
                Trimming = TextTrimming.None,
            };
            var constraints = new BackendTextLayoutConstraints(new Rect(2, 2, WIDTH_PX - 4, HEIGHT_PX - 4));
            var contextBase = (GraphicsContextBase)context;
            var layout = contextBase.CreateBackendTextLayout(TEXT, format, in constraints);
            Assert.IsNotNull(layout, "The backend produced no text layout.");
            contextBase.DrawBackendTextLayout(TEXT, format, layout, color);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        return read(cpu.GetReadOnlyPixelSpan(), cpu.StrideBytes);
    }
}
