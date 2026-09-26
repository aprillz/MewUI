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

    [TestMethod]
    public void APatternFillPaintsItsTile()
    {
        var factory = (GdiGraphicsFactory)Application.DefaultGraphicsFactory!;
        var source = SvgImageSource.FromString(PATTERN_FILL);
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

        var pixels = ((ICpuPixelSurface)surface).CopyPixels();
        int offset = ((RENDER_SIZE / 2) * RENDER_SIZE + RENDER_SIZE / 2) * 4;
        Assert.AreEqual(Color.FromArgb(255, 0xc8, 0x28, 0x3c), Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]),
            "the pattern's tile was not painted");
    }
}
