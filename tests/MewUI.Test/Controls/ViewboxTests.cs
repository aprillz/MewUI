using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// A viewbox scales its naturally sized child by <see cref="Stretch"/> and <see cref="StretchDirection"/> the way
/// WPF does, centers the result, and hit tests the child where it is drawn.
/// </summary>
[TestClass]
public sealed class ViewboxTests
{
    private const double TOLERANCE = 1e-3;

    // A 40x20 child offered 100x100 would scale 2.5 horizontally and 5 vertically.
    [TestMethod]
    [DataRow(Stretch.None, StretchDirection.Both, 40.0, 20.0)]
    [DataRow(Stretch.None, StretchDirection.UpOnly, 40.0, 20.0)]
    [DataRow(Stretch.None, StretchDirection.DownOnly, 40.0, 20.0)]
    [DataRow(Stretch.Fill, StretchDirection.Both, 100.0, 100.0)]
    [DataRow(Stretch.Fill, StretchDirection.UpOnly, 100.0, 100.0)]
    [DataRow(Stretch.Fill, StretchDirection.DownOnly, 40.0, 20.0)]
    [DataRow(Stretch.Uniform, StretchDirection.Both, 100.0, 50.0)]
    [DataRow(Stretch.Uniform, StretchDirection.UpOnly, 100.0, 50.0)]
    [DataRow(Stretch.Uniform, StretchDirection.DownOnly, 40.0, 20.0)]
    [DataRow(Stretch.UniformToFill, StretchDirection.Both, 200.0, 100.0)]
    [DataRow(Stretch.UniformToFill, StretchDirection.UpOnly, 200.0, 100.0)]
    [DataRow(Stretch.UniformToFill, StretchDirection.DownOnly, 40.0, 20.0)]
    public void Measure_Enlarging(Stretch stretch, StretchDirection direction, double expectedWidth, double expectedHeight)
        => AssertDesired(stretch, direction, new Size(100, 100), expectedWidth, expectedHeight);

    // A 40x20 child offered 20x20 would scale 0.5 horizontally and 1 vertically.
    [TestMethod]
    [DataRow(Stretch.None, StretchDirection.Both, 40.0, 20.0)]
    [DataRow(Stretch.None, StretchDirection.UpOnly, 40.0, 20.0)]
    [DataRow(Stretch.None, StretchDirection.DownOnly, 40.0, 20.0)]
    [DataRow(Stretch.Fill, StretchDirection.Both, 20.0, 20.0)]
    [DataRow(Stretch.Fill, StretchDirection.UpOnly, 40.0, 20.0)]
    [DataRow(Stretch.Fill, StretchDirection.DownOnly, 20.0, 20.0)]
    [DataRow(Stretch.Uniform, StretchDirection.Both, 20.0, 10.0)]
    [DataRow(Stretch.Uniform, StretchDirection.UpOnly, 40.0, 20.0)]
    [DataRow(Stretch.Uniform, StretchDirection.DownOnly, 20.0, 10.0)]
    [DataRow(Stretch.UniformToFill, StretchDirection.Both, 40.0, 20.0)]
    [DataRow(Stretch.UniformToFill, StretchDirection.UpOnly, 40.0, 20.0)]
    [DataRow(Stretch.UniformToFill, StretchDirection.DownOnly, 40.0, 20.0)]
    public void Measure_Shrinking(Stretch stretch, StretchDirection direction, double expectedWidth, double expectedHeight)
        => AssertDesired(stretch, direction, new Size(20, 20), expectedWidth, expectedHeight);

    [TestMethod]
    public void Measure_OneUnconstrainedSide_ScalesUniformlyByTheOther()
    {
        var viewbox = new Viewbox { Child = new Border { Width = 40, Height = 20 }, Stretch = Stretch.Fill };

        viewbox.Measure(new Size(double.PositiveInfinity, 40));

        Assert.AreEqual(new Size(80, 40), viewbox.DesiredSize);
    }

    [TestMethod]
    public void Arrange_LaysTheChildOutAtItsNaturalSizeAndDrawsItScaledAndCentered()
    {
        var child = new Border { Width = 40, Height = 20 };
        var viewbox = new Viewbox { Child = child };

        viewbox.Measure(new Size(100, 100));
        viewbox.Arrange(new Rect(0, 0, 100, 100));

        Assert.AreEqual(new Rect(0, 0, 40, 20), child.Bounds);
        var toViewbox = child.TransformToAncestor(viewbox);
        var topLeft = toViewbox.Transform(new Point(0, 0));
        var bottomRight = toViewbox.Transform(new Point(40, 20));
        Assert.AreEqual(0, topLeft.X, TOLERANCE);
        Assert.AreEqual(25, topLeft.Y, TOLERANCE);
        Assert.AreEqual(100, bottomRight.X, TOLERANCE);
        Assert.AreEqual(75, bottomRight.Y, TOLERANCE);
    }

    [TestMethod]
    public void HitTest_FindsTheChildWhereItIsDrawn()
    {
        var child = new Border { Width = 40, Height = 20 };
        var viewbox = new Viewbox { Child = child };
        viewbox.Measure(new Size(100, 100));
        viewbox.Arrange(new Rect(0, 0, 100, 100));

        Assert.AreSame(child, viewbox.HitTest(new Point(90, 70)), "inside the scaled child, far outside its layout box");
        Assert.AreSame(viewbox, viewbox.HitTest(new Point(50, 10)), "the letterbox band above the child");
    }

    [TestMethod]
    public void UnitScale_LeavesTheChildUntransformed()
    {
        var child = new Border { Width = 40, Height = 20 };
        var viewbox = new Viewbox { Child = child };
        viewbox.Measure(new Size(40, 20));
        viewbox.Arrange(new Rect(10, 10, 40, 20));

        Assert.AreEqual(new Rect(child.Bounds.Size), child.TransformToAncestor(viewbox).TransformBounds(new Rect(child.Bounds.Size)));
        Assert.AreSame(child, viewbox.HitTest(new Point(12, 12)));
    }

    private static void AssertDesired(Stretch stretch, StretchDirection direction, Size available, double expectedWidth, double expectedHeight)
    {
        var child = new Border { Width = 40, Height = 20 };
        var viewbox = new Viewbox
        {
            Child = child,
            Stretch = stretch,
            StretchDirection = direction,
        };

        viewbox.Measure(available);
        viewbox.Arrange(new Rect(available));

        // The desired size is clipped to the available size like any element's; the drawn child is not.
        Assert.AreEqual(Math.Min(expectedWidth, available.Width), viewbox.DesiredSize.Width, TOLERANCE);
        Assert.AreEqual(Math.Min(expectedHeight, available.Height), viewbox.DesiredSize.Height, TOLERANCE);
        var drawn = child.TransformToAncestor(viewbox).TransformBounds(new Rect(0, 0, 40, 20));
        Assert.AreEqual(expectedWidth, drawn.Width, TOLERANCE);
        Assert.AreEqual(expectedHeight, drawn.Height, TOLERANCE);
    }
}
