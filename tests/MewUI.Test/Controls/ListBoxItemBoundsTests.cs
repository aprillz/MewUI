using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// <see cref="ListBox.TryGetItemBounds"/> is the reverse of <see cref="ListBox.TryGetItemIndexAt"/>,
/// so the two have to agree: the rectangle an index reports must hit-test back to that index.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ListBoxItemBoundsTests
{
    private const double ITEM_HEIGHT = 20;

    [TestMethod]
    public void RowsFollowTheirIndex()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, list) = CreateList(10);

        Assert.IsTrue(list.TryGetItemBounds(0, out var first));
        Assert.IsTrue(list.TryGetItemBounds(3, out var fourth));

        Assert.AreEqual(ITEM_HEIGHT, first.Height, 0.5);
        Assert.AreEqual(first.Y + 3 * ITEM_HEIGHT, fourth.Y, 0.5, "rows are uniform, so the fourth sits three rows down");
        Assert.AreEqual(first.X, fourth.X);
        Assert.IsGreaterThan(0, first.Width, "the rectangle should span the item's width");
        window.Close();
    }

    [TestMethod]
    public void AnIndexOutsideTheItemsHasNoBounds()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, list) = CreateList(3);

        Assert.IsFalse(list.TryGetItemBounds(-1, out _));
        Assert.IsFalse(list.TryGetItemBounds(3, out _));
        window.Close();
    }

    [TestMethod]
    public void TheReportedRectangleHitTestsBackToItsIndex()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, list) = CreateList(10);

        for (int index = 0; index < 4; index++)
        {
            Assert.IsTrue(list.TryGetItemBounds(index, out var bounds), $"item {index} reported no bounds");
            var middle = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            Assert.IsTrue(list.TryGetItemIndexAt(middle, out int hit), $"item {index} did not hit-test at {middle}");
            Assert.AreEqual(index, hit, "the rectangle belongs to a different item than the one asked for");
        }
        window.Close();
    }

    [TestMethod]
    public void ScrollingMovesTheRows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, list) = CreateList(200);
        Assert.IsTrue(list.TryGetItemBounds(100, out var before));

        list.ScrollIntoView(100);
        window.PerformLayout();

        Assert.IsTrue(list.TryGetItemBounds(100, out var after));
        Assert.IsLessThan(before.Y, after.Y, "the row did not move when the list scrolled to it");
        Assert.IsLessThanOrEqualTo(list.Bounds.Bottom, after.Y, "the row scrolled into view should sit inside the list");
        window.Close();
    }

    private static (Window window, ListBox list) CreateList(int count)
    {
        var window = HeadlessWindow.Create(400, 200);
        var list = new ListBox { ItemHeight = ITEM_HEIGHT, Height = 120, VerticalAlignment = VerticalAlignment.Top };
        list.ItemsSource = ItemsView.Create(
            Enumerable.Range(0, count).Select(index => $"Item {index}").ToList());
        window.Content = list;
        window.PerformLayout();
        return (window, list);
    }
}
