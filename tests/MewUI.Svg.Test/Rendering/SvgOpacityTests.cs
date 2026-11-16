using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;

using SvgImageSource = Aprillz.MewUI.Svg.SvgImageSource;

namespace MewUI.Svg.Test.Rendering;

/// <summary>
/// The <c>opacity</c> of a shape fades the shape as a whole: its stroke covers its fill before the fade, so
/// the half of a stroke that lies over the fill shows the same colour as the half over the background.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SvgOpacityTests
{
    private const int RENDER_SIZE = 80;
    private const int TOLERANCE = 3;

    // A 40-unit square at (20, 20) with a 10-unit stroke: the stroke spans 15 to 25 across the left edge.
    private const string FADED_SHAPE = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 80 80">
          <rect x="20" y="20" width="40" height="40" style="opacity:0.5;fill:red;stroke:black;stroke-width:10"/>
        </svg>
        """;

    [TestMethod]
    public void AStrokeOverTheFillFadesWithIt()
    {
        var factory = (GdiGraphicsFactory)Application.DefaultGraphicsFactory!;
        var source = SvgImageSource.FromString(FADED_SHAPE);
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(RENDER_SIZE, RENDER_SIZE, 1));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            try
            {
                context.Clear(Color.White);
                source.Render(context, new Rect(0, 0, RENDER_SIZE, RENDER_SIZE));
            }
            finally
            {
                context.EndFrame();
            }
        }

        var pixels = ((ICpuPixelSurface)surface).CopyPixels();
        var overBackground = PixelAt(pixels, 17, 40);
        var overFill = PixelAt(pixels, 23, 40);
        Assert.IsTrue(
            Math.Abs(overBackground.R - overFill.R) <= TOLERANCE &&
            Math.Abs(overBackground.G - overFill.G) <= TOLERANCE &&
            Math.Abs(overBackground.B - overFill.B) <= TOLERANCE,
            $"the stroke over the background is {overBackground}, over the fill {overFill}: the fill shows through the faded stroke");
    }

    private static Color PixelAt(byte[] pixels, int x, int y)
    {
        int offset = (y * RENDER_SIZE + x) * 4;
        return Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }
}
