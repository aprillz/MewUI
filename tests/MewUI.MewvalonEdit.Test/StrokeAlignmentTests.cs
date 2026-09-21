using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit.Rendering;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// The rendering tests all run at 96 DPI, where a DIP is a device pixel and every alignment bug
/// hides. These check the arithmetic itself at the scales that expose it.
/// </summary>
[TestClass]
public sealed class StrokeAlignmentTests
{
    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    [DataRow(2.0)]
    public void ASnappedStrokeCoversWholeDevicePixels(double scale)
    {
        var pen = new ColorPen(Color.FromRgb(1, 2, 3)).SnapThickness(scale);

        Assert.IsTrue(IsWholePixels(pen.Thickness, scale),
            $"Thickness {pen.Thickness} is not a whole number of pixels at {scale}.");

        foreach (double edge in new[] { 0.0, 10.3, 41.7, 100.5 })
        {
            double center = pen.SnapStrokeCenter(edge, scale);

            // A stroke is centred on its coordinate, so the covered band starts half a thickness
            // before it. That start is what has to land on a pixel boundary.
            Assert.IsTrue(IsWholePixels(center - pen.Thickness / 2, scale),
                $"Stroke at edge {edge} starts mid-pixel at {scale}.");
        }
    }

    [TestMethod]
    public void AThicknessBelowOnePixelIsRaisedToOne()
    {
        // Halving a hairline until it disappears is worse than drawing the thinnest visible line.
        var pen = new ColorPen(Color.FromRgb(1, 2, 3), 0.1).SnapThickness(2.0);

        Assert.AreEqual(0.5, pen.Thickness, 1e-9);
    }

    private static bool IsWholePixels(double dip, double scale)
        => Math.Abs(dip * scale - Math.Round(dip * scale)) < 1e-6;
}
