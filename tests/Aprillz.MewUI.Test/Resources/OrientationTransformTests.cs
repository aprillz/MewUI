using Aprillz.MewUI;
using Aprillz.MewUI.Resources;

namespace MewUI.Test.Resources;

[TestClass]
public sealed class OrientationTransformTests
{
    private const double RawWidth = 4;
    private const double RawHeight = 2;
    private const double Tol = 1e-9;

    [DataTestMethod]
    [DataRow(ImageOrientation.Normal, 4.0, 2.0)]
    [DataRow(ImageOrientation.MirrorHorizontal, 4.0, 2.0)]
    [DataRow(ImageOrientation.Rotate180, 4.0, 2.0)]
    [DataRow(ImageOrientation.MirrorVertical, 4.0, 2.0)]
    [DataRow(ImageOrientation.Transpose, 2.0, 4.0)]
    [DataRow(ImageOrientation.Rotate90, 2.0, 4.0)]
    [DataRow(ImageOrientation.Transverse, 2.0, 4.0)]
    [DataRow(ImageOrientation.Rotate270, 2.0, 4.0)]
    public void GetOrientedSize_SwapsForQuarterTurns(ImageOrientation orientation, double expectedW, double expectedH)
    {
        var size = OrientationTransform.GetOrientedSize(orientation, RawWidth, RawHeight);

        Assert.AreEqual(expectedW, size.Width, Tol);
        Assert.AreEqual(expectedH, size.Height, Tol);
    }

    // Raw corners are TL(0,0), TR(4,0), BL(0,2), BR(4,2). Each row lists the expected oriented position
    // of those four corners for one orientation.
    [DataTestMethod]
    [DataRow(ImageOrientation.Normal, /*TL*/0.0, 0.0, /*TR*/4.0, 0.0, /*BL*/0.0, 2.0, /*BR*/4.0, 2.0)]
    [DataRow(ImageOrientation.MirrorHorizontal, 4.0, 0.0, 0.0, 0.0, 4.0, 2.0, 0.0, 2.0)]
    [DataRow(ImageOrientation.Rotate180, 4.0, 2.0, 0.0, 2.0, 4.0, 0.0, 0.0, 0.0)]
    [DataRow(ImageOrientation.MirrorVertical, 0.0, 2.0, 4.0, 2.0, 0.0, 0.0, 4.0, 0.0)]
    [DataRow(ImageOrientation.Transpose, 0.0, 0.0, 0.0, 4.0, 2.0, 0.0, 2.0, 4.0)]
    [DataRow(ImageOrientation.Rotate90, 2.0, 0.0, 2.0, 4.0, 0.0, 0.0, 0.0, 4.0)]
    [DataRow(ImageOrientation.Transverse, 2.0, 4.0, 2.0, 0.0, 0.0, 4.0, 0.0, 0.0)]
    [DataRow(ImageOrientation.Rotate270, 0.0, 4.0, 0.0, 0.0, 2.0, 4.0, 2.0, 0.0)]
    public void RawToOriented_MapsCorners(
        ImageOrientation orientation,
        double tlx, double tly, double trx, double tryy,
        double blx, double bly, double brx, double bry)
    {
        AssertMaps(orientation, new Point(0, 0), new Point(tlx, tly));
        AssertMaps(orientation, new Point(RawWidth, 0), new Point(trx, tryy));
        AssertMaps(orientation, new Point(0, RawHeight), new Point(blx, bly));
        AssertMaps(orientation, new Point(RawWidth, RawHeight), new Point(brx, bry));
    }

    [TestMethod]
    public void RawToOriented_ThenOrientedToRaw_RoundTrips()
    {
        Point[] points = [new(0, 0), new(4, 2), new(1.5, 0.25), new(3, 2), new(0, 1)];

        foreach (ImageOrientation orientation in Enum.GetValues<ImageOrientation>())
        {
            foreach (var raw in points)
            {
                var oriented = OrientationTransform.RawToOriented(orientation, RawWidth, RawHeight, raw);
                var back = OrientationTransform.OrientedToRaw(orientation, RawWidth, RawHeight, oriented);

                Assert.AreEqual(raw.X, back.X, Tol, $"{orientation} X");
                Assert.AreEqual(raw.Y, back.Y, Tol, $"{orientation} Y");
            }
        }
    }

    [TestMethod]
    public void RawToOriented_Rect_Rotate90_StaysAxisAligned()
    {
        // A vertical strip on the raw left edge becomes a horizontal strip on the oriented top edge.
        var raw = new Rect(1, 0, 1, 2);

        var oriented = OrientationTransform.RawToOriented(ImageOrientation.Rotate90, RawWidth, RawHeight, raw);

        AssertRect(new Rect(0, 1, 2, 1), oriented);
    }

    [TestMethod]
    public void OrientedToRaw_Rect_InvertsRawToOriented()
    {
        var raw = new Rect(0.5, 1, 2, 0.5);

        foreach (ImageOrientation orientation in Enum.GetValues<ImageOrientation>())
        {
            var oriented = OrientationTransform.RawToOriented(orientation, RawWidth, RawHeight, raw);
            var back = OrientationTransform.OrientedToRaw(orientation, RawWidth, RawHeight, oriented);

            AssertRect(raw, back);
        }
    }

    [TestMethod]
    public void Normalize_InvalidValue_BecomesNormal()
    {
        Assert.AreEqual(ImageOrientation.Normal, OrientationTransform.Normalize((ImageOrientation)0));
        Assert.AreEqual(ImageOrientation.Normal, OrientationTransform.Normalize((ImageOrientation)99));
        Assert.AreEqual(ImageOrientation.Rotate90, OrientationTransform.Normalize(ImageOrientation.Rotate90));
    }

    private static void AssertMaps(ImageOrientation orientation, Point raw, Point expectedOriented)
    {
        var actual = OrientationTransform.RawToOriented(orientation, RawWidth, RawHeight, raw);
        Assert.AreEqual(expectedOriented.X, actual.X, Tol, $"{orientation} {raw} -> X");
        Assert.AreEqual(expectedOriented.Y, actual.Y, Tol, $"{orientation} {raw} -> Y");
    }

    private static void AssertRect(Rect expected, Rect actual)
    {
        Assert.AreEqual(expected.X, actual.X, Tol, "Rect.X");
        Assert.AreEqual(expected.Y, actual.Y, Tol, "Rect.Y");
        Assert.AreEqual(expected.Width, actual.Width, Tol, "Rect.Width");
        Assert.AreEqual(expected.Height, actual.Height, Tol, "Rect.Height");
    }
}
