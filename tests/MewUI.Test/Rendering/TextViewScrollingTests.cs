using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// MakeVisible moves the smallest amount that reveals a rectangle, except for one larger than the
/// viewport: pinning that to an edge would hide its middle, so it centres instead.
/// </summary>
[TestClass]
public sealed class TextViewScrollingTests
{
    private const double VIEWPORT = 100;

    [TestMethod]
    public void ContentAlreadyInsideDoesNotScroll()
        => Assert.AreEqual(50, TextViewScrolling.ResolveOffset(50, VIEWPORT, 60, 20), 0.01);

    [TestMethod]
    public void ContentAboveScrollsToItsStart()
        => Assert.AreEqual(30, TextViewScrolling.ResolveOffset(50, VIEWPORT, 30, 20), 0.01);

    [TestMethod]
    public void ContentBelowScrollsItsEndToTheEdge()
        => Assert.AreEqual(80, TextViewScrolling.ResolveOffset(50, VIEWPORT, 150, 30), 0.01);

    [TestMethod]
    public void ContentLargerThanTheViewportCentres()
    {
        // Starts above and ends below, so neither edge can show all of it.
        double offset = TextViewScrolling.ResolveOffset(50, VIEWPORT, 20, 300);
        Assert.AreEqual(170, offset, 0.01);
    }

    [TestMethod]
    public void AnEmptyViewportKeepsTheOffset()
        => Assert.AreEqual(50, TextViewScrolling.ResolveOffset(50, 0, 10, 10), 0.01);
}
