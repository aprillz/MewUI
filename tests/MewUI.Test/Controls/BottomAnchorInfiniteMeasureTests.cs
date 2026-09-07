using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// A bottom-anchored list follows its end while the user rests there. An unconstrained measure (a
/// SplitPanel sizing its panes, a popup owner asking for the natural size) is hypothetical, and with
/// an infinite viewport every offset looks like the end; that must not re-anchor a list the user
/// has scrolled away from.
/// </summary>
[TestClass]
public sealed class BottomAnchorInfiniteMeasureTests
{
    private const double VIEWPORT_WIDTH = 200;
    private const double VIEWPORT_HEIGHT = 240;
    private const double ITEM_HEIGHT = 26;

    [TestMethod]
    public void InfiniteViewport_DoesNotReadAsPinnedToEnd()
    {
        var presenter = CreatePresenter(40);
        Layout(presenter);
        presenter.SetOffset(new Point(0, 150));
        Layout(presenter);
        int corrections = CorrectionCount(presenter);

        presenter.SetViewport(new Size(VIEWPORT_WIDTH, double.PositiveInfinity));
        ((FrameworkElement)presenter).Measure(Size.Infinity);
        Layout(presenter);

        Assert.AreEqual(150, OffsetOf(presenter), 0.001, "the user's offset survives a hypothetical measure");
        Assert.AreEqual(corrections, CorrectionCount(presenter), "no scroll-into-view correction is raised");
    }

    [TestMethod]
    public void ItemsControl_UnconstrainedMeasure_KeepsTheUserOffset()
    {
        var items = new ObservableCollection<string>(Enumerable.Range(0, 40).Select(i => $"Item {i}"));
        var control = new ItemsControl().FixedHeightPresenter(ITEM_HEIGHT, ItemsAnchor.Bottom);
        control.ItemsSource = new ItemsView<string>(items);
        var scrollViewer = (ScrollViewer)typeof(ScrollableItemsBase)
            .GetField("_scrollViewer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(control)!;

        LayoutControl(control);
        LayoutControl(control);
        double end = scrollViewer.VerticalOffset;
        Assert.IsGreaterThan(0.0, end, "precondition: a bottom-anchored list starts at its end");

        // The user wheels away from the end; the wheel invalidates arrange only.
        scrollViewer.ScrollBy(-2);
        control.Arrange(new Rect(0, 0, VIEWPORT_WIDTH, VIEWPORT_HEIGHT));
        double scrolled = scrollViewer.VerticalOffset;
        Assert.IsLessThan(end, scrolled, "precondition: the wheel moved the list off the end");

        // The parent asks for the natural size, then lays out at the real size, as SplitPanel does.
        control.Measure(Size.Infinity);
        LayoutControl(control);
        LayoutControl(control);

        Assert.AreEqual(scrolled, scrollViewer.VerticalOffset, 0.001, "the list stays where the user left it");
    }

    private static void LayoutControl(ItemsControl control)
    {
        control.Measure(new Size(VIEWPORT_WIDTH, VIEWPORT_HEIGHT));
        control.Arrange(new Rect(0, 0, VIEWPORT_WIDTH, VIEWPORT_HEIGHT));
    }

    private static readonly Dictionary<IItemsPresenter, List<Point>> _corrections = new();

    private static FixedHeightItemsPresenter CreatePresenter(int count)
    {
        var items = new ObservableCollection<string>(Enumerable.Range(0, count).Select(i => $"Item {i}"));
        var presenter = new FixedHeightItemsPresenter
        {
            ItemHeight = ITEM_HEIGHT,
            Anchor = ItemsAnchor.Bottom,
            ItemsSource = new ItemsView<string>(items),
        };

        var log = new List<Point>();
        _corrections[presenter] = log;
        presenter.OffsetCorrectionRequested += log.Add;
        return presenter;
    }

    private static int CorrectionCount(IItemsPresenter presenter) => _corrections[presenter].Count;

    private static double OffsetOf(IItemsPresenter presenter)
    {
        double top = double.NaN;
        int topIndex = -1;
        presenter.VisitRealized((index, element) =>
        {
            if (topIndex < 0 || index < topIndex)
            {
                topIndex = index;
                top = element.Bounds.Y;
            }
        });

        return topIndex < 0 ? double.NaN : topIndex * ITEM_HEIGHT - top;
    }

    private static void Layout(IItemsPresenter presenter)
    {
        var element = (FrameworkElement)presenter;
        presenter.SetViewport(new Size(VIEWPORT_WIDTH, VIEWPORT_HEIGHT));
        element.Measure(new Size(VIEWPORT_WIDTH, VIEWPORT_HEIGHT));
        element.Arrange(new Rect(0, 0, VIEWPORT_WIDTH, VIEWPORT_HEIGHT));
    }
}
