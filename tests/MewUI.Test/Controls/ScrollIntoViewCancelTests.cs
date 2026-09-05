using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// A scroll-into-view request waits for the next arrange. If the user scrolls in between, honouring
/// it would snap the list away from them, so the request is dropped instead. A ComboBox popup hits
/// this on its first open: the list is asked to bring the selected item into view before it has a
/// viewport, and the deferred request was still pending when the wheel arrived.
/// </summary>
[TestClass]
public sealed class ScrollIntoViewCancelTests
{
    private const double VIEWPORT_WIDTH = 200;
    private const double VIEWPORT_HEIGHT = 240;
    private const double ITEM_HEIGHT = 26;

    [TestMethod]
    public void UserScroll_AfterScrollIntoViewRequest_KeepsTheUserOffset()
    {
        var presenter = CreatePresenter(40);
        Layout(presenter);

        // The popup asks for the selected item (the first) while the offset is still at the top.
        presenter.RequestScrollIntoView(0);

        // The user turns the wheel before the request is arranged.
        presenter.SetOffset(new Point(0, 150));
        Layout(presenter);

        Assert.AreEqual(150, OffsetOf(presenter), 0.001,
            "the wheel wins over a scroll-into-view request that has not been arranged yet");
        Assert.AreEqual(0, CorrectionCount(presenter),
            "no offset correction is requested once the user has scrolled");
    }

    [TestMethod]
    public void UserScroll_DoesNotStopALaterScrollIntoView()
    {
        var presenter = CreatePresenter(40);
        Layout(presenter);

        presenter.RequestScrollIntoView(0);
        presenter.SetOffset(new Point(0, 150));
        Layout(presenter);

        // A request made after the scroll is honoured normally.
        int corrections = CorrectionCount(presenter);
        presenter.RequestScrollIntoView(39);
        Layout(presenter);

        Assert.IsGreaterThan(corrections, CorrectionCount(presenter),
            "a request made after the user scrolled still brings the item into view");
    }

    [TestMethod]
    public void ScrollIntoViewRequest_WithoutUserScroll_IsHonoured()
    {
        var presenter = CreatePresenter(40);
        Layout(presenter);

        presenter.RequestScrollIntoView(30);
        Layout(presenter);

        Assert.AreEqual(1, CorrectionCount(presenter),
            "an untouched request still asks the scroll owner to move");
        Assert.IsGreaterThan(0.0, LastCorrection(presenter),
            "the requested offset brings the item into view");
    }

    [TestMethod]
    public void CorrectionEcho_DoesNotCancelTheRequest()
    {
        var presenter = CreatePresenter(40);
        Layout(presenter);

        presenter.RequestScrollIntoView(30);
        Layout(presenter);
        double requested = LastCorrection(presenter);

        // The scroll owner applies the correction and reports it back, as ScrollViewer does on its
        // next arrange. That echo must not read as a user scroll.
        presenter.SetOffset(new Point(0, requested));
        Layout(presenter);

        Assert.AreEqual(requested, OffsetOf(presenter), 0.001,
            "the list stays where the request put it");
    }

    private static readonly Dictionary<IItemsPresenter, List<Point>> _corrections = new();

    private static FixedHeightItemsPresenter CreatePresenter(int count)
    {
        var items = new ObservableCollection<string>(Enumerable.Range(0, count).Select(i => $"Item {i}"));
        var presenter = new FixedHeightItemsPresenter
        {
            ItemHeight = ITEM_HEIGHT,
            ItemsSource = new ItemsView<string>(items),
        };

        var log = new List<Point>();
        _corrections[presenter] = log;
        presenter.OffsetCorrectionRequested += log.Add;
        return presenter;
    }

    private static int CorrectionCount(IItemsPresenter presenter) => _corrections[presenter].Count;

    private static double LastCorrection(IItemsPresenter presenter) => _corrections[presenter][^1].Y;

    private static double OffsetOf(IItemsPresenter presenter)
    {
        // The presenter exposes no offset getter; the first realized row's position reports where it
        // actually put the content.
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
