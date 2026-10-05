using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class ProgressRingTests
{
    private const int SIDE = 96;

    /// <summary>
    /// The indeterminate arc grows from a dot to half the ring over a turn and a quarter, then shrinks back
    /// to a dot over a turn and three quarters, ending where the next cycle starts.
    /// </summary>
    [TestMethod]
    public void IndeterminateArc_GrowsToHalfTheRingThenShrinksBackToADot()
    {
        var first = ProgressRing.GetIndeterminateArc(0);
        var quarter = ProgressRing.GetIndeterminateArc(0.25);
        var half = ProgressRing.GetIndeterminateArc(0.5);
        var last = ProgressRing.GetIndeterminateArc(1);

        Assert.AreEqual(0, first.StartDegrees, 0.001);
        Assert.IsLessThan(1.0, first.SweepDegrees);
        Assert.IsGreaterThan(0.0, first.SweepDegrees);
        Assert.AreEqual(225, quarter.StartDegrees, 0.001);
        Assert.AreEqual(90, quarter.SweepDegrees, 0.001);
        Assert.AreEqual(450, half.StartDegrees, 0.001);
        Assert.AreEqual(180, half.SweepDegrees, 0.001);
        Assert.AreEqual(1080, last.StartDegrees, 0.001);
        Assert.IsLessThan(1.0, last.SweepDegrees);
    }

    /// <summary>
    /// A quarter of the range fills the ring clockwise from the top, leaves the rest to the track, and its
    /// rounded start reaches past the top by the stroke's half width.
    /// </summary>
    [TestMethod]
    public void QuarterValue_FillsClockwiseFromTheTopWithARoundedStart()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Renders through a Win32 backend.");
            return;
        }

        var factory = new Direct2DGraphicsFactory();
        var previousFactory = Application.DefaultGraphicsFactory;
        Application.DefaultGraphicsFactory = factory;
        try
        {
            using var scope = factory.AcquireBackgroundRenderScope();
            var ring = new ProgressRing { Value = 25 };
            var window = HeadlessWindow.Create(SIDE, SIDE);
            window.Padding = new Thickness(0);
            window.Content = ring;
            window.PerformLayout();

            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SIDE, SIDE, 1.0, hasAlpha: false));
            window.RenderFrameToSurface(surface);
            var pixels = new byte[SIDE * SIDE * 4];
            Assert.IsTrue(factory.TryReadPixels(surface, pixels, SIDE * 4), "the surface could not be read back");

            // 96 wide: stroke 12, centre line radius 42.
            const double RADIUS = 42;
            var filled = Sample(pixels, RADIUS, -45);
            var track = Sample(pixels, RADIUS, 135);
            var cap = Sample(pixels, RADIUS, -90 - 5);
            var pastCap = Sample(pixels, RADIUS, -90 - 20);

            Assert.AreNotEqual(track, filled, "the filled quarter and the track are the same colour");
            Assert.AreEqual(filled, cap, "the start of the arc is not rounded past the top");
            Assert.AreEqual(track, pastCap, "the arc reaches further past the top than its rounded end");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
            factory.Dispose();
        }
    }

    private static (byte B, byte G, byte R) Sample(byte[] pixels, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        int x = (int)Math.Round(SIDE / 2.0 + radius * Math.Cos(radians));
        int y = (int)Math.Round(SIDE / 2.0 + radius * Math.Sin(radians));
        int offset = (y * SIDE + x) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
    }
}
