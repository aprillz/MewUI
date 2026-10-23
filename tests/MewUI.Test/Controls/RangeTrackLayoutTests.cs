using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class RangeTrackLayoutTests
{
    [TestMethod]
    public void ScrollBarMode_SizesTheThumbFromTheViewportShare()
    {
        var layout = RangeTrackLayout.Compute(
            new Rect(0, 0, 100, 4), Orientation.Horizontal, isDirectionReversed: false,
            minimum: 0, maximum: 100, value: 50, viewportSize: 100, thumbLength: double.NaN, minThumbLength: 8);

        Assert.AreEqual(50, layout.ThumbLength, 0.001, "the viewport is half of range plus viewport");
        Assert.AreEqual(new Rect(25, 0, 50, 4), layout.ThumbRect);
        Assert.AreEqual(new Rect(0, 0, 25, 4), layout.DecreaseRect);
        Assert.AreEqual(new Rect(75, 0, 25, 4), layout.IncreaseRect);
        Assert.AreEqual(50, layout.ValueFromPoint(new Point(50, 2)), 0.001, "the track centre is the middle value");
        Assert.AreEqual(50, layout.ValueFromDistance(25), 0.001, "half the usable length is half the range");
    }

    [TestMethod]
    public void ScrollBarMode_TooShortATrack_HasNoThumb()
    {
        var layout = RangeTrackLayout.Compute(
            new Rect(0, 0, 4, 4), Orientation.Horizontal, isDirectionReversed: false,
            minimum: 0, maximum: 100, value: 50, viewportSize: 100, thumbLength: double.NaN, minThumbLength: 8);

        Assert.IsTrue(layout.ThumbRect.IsEmpty);
    }

    [TestMethod]
    public void SliderMode_KeepsTheGivenThumbLength()
    {
        var layout = RangeTrackLayout.Compute(
            new Rect(0, 0, 114, 14), Orientation.Horizontal, isDirectionReversed: false,
            minimum: 0, maximum: 100, value: 100, viewportSize: double.NaN, thumbLength: 14, minThumbLength: 8);

        Assert.AreEqual(new Rect(100, 0, 14, 14), layout.ThumbRect, "the maximum puts the thumb at the end");
        Assert.AreEqual(0, layout.ValueFromPoint(new Point(7, 7)), 0.001, "a point under the first thumb centre is the minimum");
        Assert.AreEqual(114, layout.IncreaseRect.X, 0.001, "nothing lies past a thumb at the maximum");
        Assert.AreEqual(0, layout.IncreaseRect.Width, 0.001);
    }

    [TestMethod]
    public void ReversedVertical_PutsTheMinimumAtTheBottom()
    {
        var layout = RangeTrackLayout.Compute(
            new Rect(0, 0, 14, 114), Orientation.Vertical, isDirectionReversed: true,
            minimum: 0, maximum: 100, value: 0, viewportSize: double.NaN, thumbLength: 14, minThumbLength: 8);

        Assert.AreEqual(new Rect(0, 100, 14, 14), layout.ThumbRect, "the minimum sits at the bottom");
        Assert.AreEqual(0, layout.DecreaseRect.Height, 0.001, "nothing lies toward the minimum");
        Assert.AreEqual(100, layout.IncreaseRect.Height, 0.001, "everything above the thumb lies toward the maximum");
        Assert.AreEqual(-10, layout.ValueFromDistance(10), 0.001, "dragging down lowers the value");
    }
}
