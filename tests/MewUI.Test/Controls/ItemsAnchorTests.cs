using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ItemsAnchorTests
{
    private const double VIEWPORT_HEIGHT = 320;
    private const double VIEWPORT_WIDTH = 200;
    private const double ITEM_HEIGHT = 40;

    [TestMethod]
    public void BottomAnchor_ShortContent_RestsAgainstViewportBottom()
    {
        var items = new ObservableCollection<double>(Enumerable.Repeat(ITEM_HEIGHT, 3));
        var presenter = CreateFixed(items, ItemsAnchor.Bottom);

        Layout(presenter);

        Assert.AreEqual(VIEWPORT_HEIGHT, BoundsOf(presenter, items.Count - 1).Bottom, 0.001,
            "The last item must sit against the bottom edge while the content is shorter than the viewport.");
        Assert.AreEqual(VIEWPORT_HEIGHT - items.Count * ITEM_HEIGHT, BoundsOf(presenter, 0).Top, 0.001);
    }

    [TestMethod]
    public void TopAnchor_ShortContent_StartsAtViewportTop()
    {
        var items = new ObservableCollection<double>(Enumerable.Repeat(ITEM_HEIGHT, 3));
        var presenter = CreateFixed(items, ItemsAnchor.Top);

        Layout(presenter);

        Assert.AreEqual(0, BoundsOf(presenter, 0).Top, 0.001);
    }

    [TestMethod]
    public void BottomAnchor_ContentFillsViewport_ShiftCollapsesToZero()
    {
        // Exactly filling the viewport is the boundary where the shift must reach zero, so the
        // handover to normal scrolling has no jump.
        var items = new ObservableCollection<double>(
            Enumerable.Repeat(ITEM_HEIGHT, (int)(VIEWPORT_HEIGHT / ITEM_HEIGHT)));
        var presenter = CreateFixed(items, ItemsAnchor.Bottom);

        Layout(presenter);

        Assert.AreEqual(0, BoundsOf(presenter, 0).Top, 0.001);
        Assert.AreEqual(VIEWPORT_HEIGHT, BoundsOf(presenter, items.Count - 1).Bottom, 0.001);
    }

    [TestMethod]
    public void BottomAnchor_ShortContent_HitTestMatchesRenderedPosition()
    {
        var items = new ObservableCollection<double>(Enumerable.Repeat(ITEM_HEIGHT, 3));
        var presenter = CreateFixed(items, ItemsAnchor.Bottom);

        Layout(presenter);

        double shift = VIEWPORT_HEIGHT - items.Count * ITEM_HEIGHT;
        Assert.IsFalse(presenter.TryGetItemIndexAtY(shift - 1, out _),
            "The empty strip above the anchored content must not map to an item.");

        Assert.IsTrue(presenter.TryGetItemIndexAtY(shift + ITEM_HEIGHT * 1.5, out int middle));
        Assert.AreEqual(1, middle);

        Assert.IsTrue(presenter.TryGetItemYRange(0, out double top, out _));
        Assert.AreEqual(shift, top, 0.001, "The reported range must match where the item is drawn.");
    }

    [TestMethod]
    public void BottomAnchor_AppendWhileAtEnd_FollowsTheEnd()
    {
        var items = new ObservableCollection<double>(Enumerable.Repeat(ITEM_HEIGHT, 10));
        var presenter = CreateFixed(items, ItemsAnchor.Bottom);
        double offsetY = 0;
        presenter.OffsetCorrectionRequested += offset =>
        {
            offsetY = offset.Y;
            presenter.SetOffset(offset);
        };

        Layout(presenter);
        presenter.SetOffset(new Point(0, items.Count * ITEM_HEIGHT - VIEWPORT_HEIGHT));
        Layout(presenter);

        items.Add(ITEM_HEIGHT);
        Layout(presenter);

        Assert.AreEqual(items.Count * ITEM_HEIGHT - VIEWPORT_HEIGHT, offsetY, 0.001,
            "Appending while the view rests at the end must follow the new end.");
    }

    [TestMethod]
    public void BottomAnchor_AppendWhileScrolledUp_KeepsOffset()
    {
        var items = new ObservableCollection<double>(Enumerable.Repeat(ITEM_HEIGHT, 10));
        var presenter = CreateFixed(items, ItemsAnchor.Bottom);
        presenter.OffsetCorrectionRequested += presenter.SetOffset;
        Layout(presenter);

        // Scroll up to read history, once the initial move to the end has settled.
        presenter.SetOffset(new Point(0, 20));
        Layout(presenter);

        bool corrected = false;
        presenter.OffsetCorrectionRequested += _ => corrected = true;

        items.Add(ITEM_HEIGHT);
        Layout(presenter);

        Assert.IsFalse(corrected,
            "Reading history must not be interrupted by items arriving at the end.");
    }

    [TestMethod]
    public void BottomAnchor_ExplicitScrollIntoView_Wins()
    {
        var items = new ObservableCollection<double>(Enumerable.Repeat(ITEM_HEIGHT, 20));
        var presenter = CreateFixed(items, ItemsAnchor.Bottom);
        double offsetY = -1;
        presenter.OffsetCorrectionRequested += offset =>
        {
            offsetY = offset.Y;
            presenter.SetOffset(offset);
        };

        Layout(presenter);
        presenter.SetOffset(new Point(0, items.Count * ITEM_HEIGHT - VIEWPORT_HEIGHT));
        Layout(presenter);

        presenter.RequestScrollIntoView(0);
        Layout(presenter);

        Assert.AreEqual(0, offsetY, 0.001, "An explicit scroll request must not be overridden by the anchor.");
        Assert.AreEqual(0, BoundsOf(presenter, 0).Top, 0.001);
    }

    [TestMethod]
    public void SwitchingAnchor_WhileScrollable_DoesNotMoveContent()
    {
        // The anchor only decides where content shorter than the viewport rests, so switching it
        // on a scrollable list must leave every item exactly where it was.
        var items = new ObservableCollection<double>(Enumerable.Repeat(ITEM_HEIGHT, 30));
        var presenter = CreateFixed(items, ItemsAnchor.Top);
        Layout(presenter);
        presenter.SetOffset(new Point(0, 200));
        Layout(presenter);
        var before = BoundsOf(presenter, 5);

        presenter.Anchor = ItemsAnchor.Bottom;
        Layout(presenter);

        Assert.AreEqual(before.Top, BoundsOf(presenter, 5).Top, 0.001);
    }

    [TestMethod]
    public void ReselectingTheSamePresenterKind_KeepsTheInstance()
    {
        // Switching the anchor must not build a fresh presenter: that loses the measured heights,
        // so the restored offset lands on different content and the view jumps.
        var list = new ItemsControl().VariableHeightPresenter(ItemsAnchor.Top);
        var before = FieldPresenter(list);

        list.VariableHeightPresenter(ItemsAnchor.Bottom);

        Assert.AreSame(before, FieldPresenter(list));
    }

    [TestMethod]
    public void SelectingADifferentPresenterKind_ReplacesTheInstance()
    {
        var list = new ItemsControl().VariableHeightPresenter();
        var before = FieldPresenter(list);

        list.FixedHeightPresenter();

        Assert.AreNotSame(before, FieldPresenter(list));
    }

    private static object FieldPresenter(ItemsControl list)
        => typeof(ItemsControl)
            .GetField("_presenter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(list)!;

    [TestMethod]
    public void BottomAnchor_VariableHeight_MeasuredHeightsReachBottomEdge()
    {
        // The variable presenter lays out with estimated heights first; once the real heights land
        // the shift changes, and the content must still end up flush with the bottom edge.
        var heights = new ObservableCollection<double>([70, 90, 50]);
        var presenter = new VariableHeightItemsPresenter
        {
            Anchor = ItemsAnchor.Bottom,
            ItemsSource = new ItemsView<double>(heights),
            ItemTemplate = NewHeightTemplate(),
        };
        presenter.OffsetCorrectionRequested += presenter.SetOffset;

        Layout(presenter);
        Layout(presenter);

        Assert.AreEqual(VIEWPORT_HEIGHT, BoundsOf(presenter, heights.Count - 1).Bottom, 0.001);
        Assert.AreEqual(VIEWPORT_HEIGHT - heights.Sum(), BoundsOf(presenter, 0).Top, 0.001);
    }

    [TestMethod]
    public void BottomAnchor_VariableHeight_FillingAnEmptyList_ReachesTheEnd()
    {
        // Items taller than the running estimate: the end computed from estimates is short of the
        // real one, so following the end has to keep up as the measured heights land.
        var heights = new ObservableCollection<double>();
        var presenter = new VariableHeightItemsPresenter
        {
            Anchor = ItemsAnchor.Bottom,
            ItemsSource = new ItemsView<double>(heights),
            ItemTemplate = NewHeightTemplate(),
        };
        presenter.OffsetCorrectionRequested += presenter.SetOffset;
        Layout(presenter);

        for (int i = 0; i < 20; i++)
        {
            heights.Add(60);
        }

        for (int pass = 0; pass < 4; pass++)
        {
            Layout(presenter);
        }

        Assert.AreEqual(VIEWPORT_HEIGHT, BoundsOf(presenter, heights.Count - 1).Bottom, 0.001,
            "Filling an empty bottom-anchored list must leave the newest item at the bottom edge.");
    }

    [TestMethod]
    public void BottomAnchor_StackPresenter_ShortContent_RestsAgainstViewportBottom()
    {
        var heights = new ObservableCollection<double>([40, 60]);
        var presenter = new StackItemsPresenter
        {
            Anchor = ItemsAnchor.Bottom,
            ItemsSource = new ItemsView<double>(heights),
            ItemTemplate = NewHeightTemplate(),
        };

        Layout(presenter);

        Assert.AreEqual(VIEWPORT_HEIGHT, BoundsOf(presenter, heights.Count - 1).Bottom, 0.001);
    }

    private static FixedHeightItemsPresenter CreateFixed(ObservableCollection<double> items, ItemsAnchor anchor)
        => new()
        {
            Anchor = anchor,
            ItemHeight = ITEM_HEIGHT,
            ItemsSource = new ItemsView<double>(items),
            ItemTemplate = NewHeightTemplate(),
        };

    private static DelegateTemplate<double> NewHeightTemplate()
        => new(
            build: _ => new HeightElement(),
            bind: static (view, height, _, _) => ((HeightElement)view).ItemHeight = height);

    private static void Layout(IItemsPresenter presenter)
    {
        var element = (FrameworkElement)presenter;
        presenter.SetViewport(new Size(VIEWPORT_WIDTH, VIEWPORT_HEIGHT));
        element.Measure(new Size(VIEWPORT_WIDTH, VIEWPORT_HEIGHT));
        element.Arrange(new Rect(0, 0, VIEWPORT_WIDTH, VIEWPORT_HEIGHT));
    }

    private static Rect BoundsOf(IItemsPresenter presenter, int index)
    {
        Rect bounds = default;
        bool found = false;
        presenter.VisitRealized((i, element) =>
        {
            if (i == index)
            {
                bounds = element.Bounds;
                found = true;
            }
        });

        Assert.IsTrue(found, $"Item {index} was expected to be realized.");
        return bounds;
    }

    private sealed class HeightElement : FrameworkElement
    {
        public double ItemHeight { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(availableSize.Width, ItemHeight);
    }
}
