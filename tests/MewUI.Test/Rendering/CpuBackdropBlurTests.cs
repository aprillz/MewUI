using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Rendering.Gdi;

namespace MewUI.Test.Rendering;

/// <summary>
/// The CPU blur of a backdrop runs at a reduced resolution, since a backdrop is drawn straight back stretched and
/// never composited by pixel index; stretched back, it matches the full-resolution blur closely. Other filter
/// graphs keep their resolution.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CpuBackdropBlurTests
{
    private const int WIDTH = 256;
    private const int HEIGHT = 128;
    private const int STRIPE = 16;
    // Radius 24 DIPs is a pixel sigma of 8 at scale 1.
    private const double RADIUS = 24;

    [TestMethod]
    public void ABackdropBlur_RunsAtAQuarterResolution()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (backdrop, _) = Blur(extendsSourceEdges: true);
        var (plain, _) = Blur(extendsSourceEdges: false);

        Assert.AreEqual(WIDTH / 4, backdrop.Width, "the backdrop blur kept its resolution");
        Assert.AreEqual(WIDTH, plain.Width, "a blur that is not a backdrop lost resolution");
    }

    [TestMethod]
    public void ABackdropBlur_StretchedBack_MatchesTheFullResolutionBlur()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (backdrop, backdropRow) = Blur(extendsSourceEdges: true);
        var (plain, plainRow) = Blur(extendsSourceEdges: false);

        double total = 0;
        for (int x = 0; x < plain.Width; x++)
        {
            // The reduced pixel whose area covers this one.
            int reduced = Math.Min(backdrop.Width - 1, x * backdrop.Width / plain.Width);
            total += Math.Abs(plainRow[x * 4 + 2] - backdropRow[reduced * 4 + 2]);
        }

        double mean = total / plain.Width;
        Assert.IsLessThan(12.0, mean, $"stretched back, the backdrop blur differs from the full blur by {mean:F1} levels on average");
    }

    /// <summary>Blurs red and black vertical stripes; returns the result's size and its middle row.</summary>
    private static ((int Width, int Height), byte[] Row) Blur(bool extendsSourceEdges)
    {
        using var factory = new GdiGraphicsFactory();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, dpiScale: 1.0, debugName: "Stripes"));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.Black);
            for (int left = 0; left < WIDTH; left += STRIPE * 2)
            {
                context.FillRectangle(new Rect(left, 0, STRIPE, HEIGHT), Color.FromRgb(240, 0, 0));
            }

            context.EndFrame();
        }

        using var image = factory.CreateImageView(surface);
        using var filterContext = new DefaultFilterContext(surface, image, new Rect(0, 0, WIDTH, HEIGHT), factory)
        {
            ExtendsSourceEdges = extendsSourceEdges,
            ReducesBlur = extendsSourceEdges,
        };
        using var result = ((IGraphicsFactory)factory).CreateImageFilterExecutor().Execute(new BlurFilter(RADIUS), filterContext);
        var pixels = result.ReadPixels(out int stride);
        var row = pixels.Slice(result.PixelHeight / 2 * stride, result.PixelWidth * 4).ToArray();
        return ((result.PixelWidth, result.PixelHeight), row);
    }
}
