using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A backdrop inside a scroll viewer, whose content clip has rounded corners, blurs what the content drew
/// under it on every backend, including one that holds drawing under such a clip back from its target.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class BackdropUnderRoundedClipTests
{
    private const int WIDTH = 400;
    private const int HEIGHT = 300;
    private const int STRIPE = 6;
    private const byte GLASS_WHITE_ALPHA = 40;

    // Black and white stripes blurred to their middle grey, with the glass's translucent white over it.
    private const double BLURRED_LEVEL = 127.5 + (255 - 127.5) * GLASS_WHITE_ALPHA / 255.0;
    private const double LEVEL_TOLERANCE = 14;
    private const int MAX_RIPPLE = 40;

    private sealed class Stripes : Control
    {
        protected override Size MeasureContent(Size availableSize) => new(WIDTH, HEIGHT);

        protected override void OnRender(IGraphicsContext context)
        {
            for (double x = Bounds.X; x < Bounds.Right; x += STRIPE * 2)
            {
                context.FillRectangle(new Rect(x, Bounds.Y, STRIPE, Bounds.Height), Color.Black);
                context.FillRectangle(new Rect(x + STRIPE, Bounds.Y, STRIPE, Bounds.Height), Color.White);
            }
        }
    }

    [TestMethod]
    [DataRow(TestBackend.Gdi)]
    [DataRow(TestBackend.Direct2D)]
    public void ABackdropInsideAScrollViewer_BlursTheContentUnderIt(TestBackend backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The backends under test are Windows-only.");
            return;
        }

        using var session = TestBackendSession.Open(backend);
        var glass = new Border
        {
            Margin = new Thickness(80),
            CornerRadius = 12,
            Background = Color.FromArgb(GLASS_WHITE_ALPHA, 255, 255, 255),
            BackdropFilter = new BlurFilter(12, 12),
        };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new ScrollViewer { CornerRadius = 8, Content = new Grid().Children(new Stripes(), glass) };
        window.PerformLayout();

        using var surface = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderFrameToSurface(surface);
        string label = session.Factory.Backend;
        Assert.AreEqual(0, window.RetainedStatistics!.BackdropFallbackCount, $"{label}: the backdrop fell back");
        Assert.AreEqual(1, window.RetainedStatistics!.BackdropPassCount, $"{label}: backdrops drawn");

        var pixels = ReadPixels(session.Factory, surface);
        int row = (int)(glass.Bounds.Y + glass.Bounds.Height / 2);
        int middle = (int)(glass.Bounds.X + glass.Bounds.Width / 2);
        int lowest = 255;
        int highest = 0;
        double sum = 0;
        for (int column = middle - STRIPE * 2; column < middle + STRIPE * 2; column++)
        {
            int red = pixels[(row * WIDTH + column) * 4 + 2];
            lowest = Math.Min(lowest, red);
            highest = Math.Max(highest, red);
            sum += red;
        }

        double mean = sum / (STRIPE * 4);
        Assert.IsLessThan(MAX_RIPPLE, highest - lowest, $"{label}: the stripes under the glass were not blurred ({lowest} to {highest})");
        Assert.IsLessThan(LEVEL_TOLERANCE, Math.Abs(mean - BLURRED_LEVEL), $"{label}: the glass shows {mean:0.0}, not the blurred stripes");

        using var reference = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        var expected = ReadPixels(session.Factory, reference);
        int differing = 0;
        for (int offset = 0; offset < expected.Length; offset++)
        {
            if (Math.Abs(expected[offset] - pixels[offset]) > session.ChannelTolerance)
            {
                differing++;
            }
        }

        Assert.AreEqual(0, differing, $"{label}: channels that differ from a frame drawn straight from the visuals");
    }

    private static byte[] ReadPixels(IGraphicsFactory factory, IRenderSurface surface)
    {
        var pixels = new byte[WIDTH * HEIGHT * 4];
        if (!((IRenderDevice)factory).TryReadPixels(surface, pixels, WIDTH * 4))
        {
            Assert.Inconclusive($"{factory.Backend} cannot read an offscreen surface back.");
        }

        return pixels;
    }
}
