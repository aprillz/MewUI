using Aprillz.MewUI;

namespace MewUI.Test.Core;

/// <summary>
/// A coordinate assembled from terms that went through 1/dpiScale reaches a half-pixel boundary a
/// couple of ulps short of it. Both rounding helpers have to answer the pixel it sits on.
/// </summary>
[TestClass]
public sealed class LayoutRoundingMidpointTests
{
    [TestMethod]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(1.75)]
    [DataRow(2.0)]
    [DataRow(3.0)]
    public void AHalfPixelEdgeRoundsUpEvenWhenItsArithmeticFallsShort(double dpiScale)
    {
        for (int pixel = 8; pixel <= 40; pixel++)
        {
            // How a stroke's outer edge is built: a whole pixel pushed in by half a hairline, both
            // expressed in DIP.
            double edge = (pixel / dpiScale) - ((1 / dpiScale) / 2);

            Assert.AreEqual(pixel, LayoutRounding.RoundToPixelInt(edge, dpiScale),
                $"RoundToPixelInt lost the boundary at {pixel}px.");
            Assert.AreEqual(pixel / dpiScale, LayoutRounding.RoundToPixel(edge, dpiScale), 1e-9,
                $"RoundToPixel lost the boundary at {pixel}px.");
        }
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    public void BothHelpersAgreeOnEveryPixelFraction(double dpiScale)
    {
        for (int step = 0; step <= 400; step++)
        {
            double value = step / 8.0;
            Assert.AreEqual(
                LayoutRounding.RoundToPixelInt(value, dpiScale) / dpiScale,
                LayoutRounding.RoundToPixel(value, dpiScale),
                1e-9,
                $"The two helpers picked different pixels for {value} DIP.");
        }
    }

    [TestMethod]
    public void AValueBelowTheMidpointStillRoundsDown()
    {
        // 0.4999px below a boundary is a real position, not arithmetic noise, and must not be lifted.
        Assert.AreEqual(15.0, LayoutRounding.RoundToPixel(15.4999 / 1.5, 1.5) * 1.5, 1e-6);
        Assert.AreEqual(16.0, LayoutRounding.RoundToPixel(15.5001 / 1.5, 1.5) * 1.5, 1e-6);
    }

    [TestMethod]
    public void InfinityAndNaNPassThrough()
    {
        Assert.AreEqual(double.PositiveInfinity, LayoutRounding.RoundToPixel(double.PositiveInfinity, 1.5));
        Assert.IsTrue(double.IsNaN(LayoutRounding.RoundToPixel(double.NaN, 1.5)));
    }
}
