using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;

using SvgImageSource = Aprillz.MewUI.Svg.SvgImageSource;

namespace MewUI.Svg.Test.Rendering;

/// <summary>A shape filled with a <c>pattern</c> paints the pattern's tile, which the extension renders offscreen.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SvgPatternFillTests
{
    private const int RENDER_SIZE = 64;

    private const string PATTERN_FILL = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
          <defs>
            <pattern id="tile" width="8" height="8" patternUnits="userSpaceOnUse">
              <rect width="8" height="8" fill="#c8283c"/>
            </pattern>
          </defs>
          <rect width="64" height="64" fill="url(#tile)"/>
        </svg>
        """;

    // A tile a hair over 8 units, as an editor writes pattern sizes, which once took a 9-pixel bitmap with an empty last column.
    private const string FRACTIONAL_TILE_FILL = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
          <defs>
            <pattern id="tile" width="8.0002" height="8.0002" patternUnits="userSpaceOnUse">
              <rect width="8.0002" height="8.0002" fill="#c8283c"/>
            </pattern>
          </defs>
          <rect width="64" height="64" fill="url(#tile)"/>
        </svg>
        """;

    [TestMethod]
    public void APatternFillPaintsItsTile()
    {
        var pixels = Render(PATTERN_FILL);
        int offset = ((RENDER_SIZE / 2) * RENDER_SIZE + RENDER_SIZE / 2) * 4;
        Assert.AreEqual(Color.FromArgb(255, 0xc8, 0x28, 0x3c), Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]),
            "the pattern's tile was not painted");
    }

    [TestMethod]
    public void ATileOfAFractionalSizeRepeatsWithoutASeam()
    {
        var pixels = Render(FRACTIONAL_TILE_FILL);
        int row = RENDER_SIZE / 2;
        for (int column = 0; column < RENDER_SIZE; column++)
        {
            int offset = (row * RENDER_SIZE + column) * 4;
            Assert.AreEqual(255, pixels[offset + 3], $"column {column} of the fill is not opaque: a seam between two tiles");
        }
    }

    private static byte[] Render(string svg)
    {
        var factory = (GdiGraphicsFactory)Application.DefaultGraphicsFactory!;
        var source = SvgImageSource.FromString(svg);
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(RENDER_SIZE, RENDER_SIZE, 1));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            try
            {
                ((ICpuPixelSurface)surface).Clear(Color.Transparent);
                source.Render(context, new Rect(0, 0, RENDER_SIZE, RENDER_SIZE));
            }
            finally
            {
                context.EndFrame();
            }
        }

        return ((ICpuPixelSurface)surface).CopyPixels();
    }
}
