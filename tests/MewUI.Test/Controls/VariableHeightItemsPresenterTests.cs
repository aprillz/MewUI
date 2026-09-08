using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class VariableHeightItemsPresenterTests
{
    [TestMethod]
    public void AppendThenScrollIntoView_ArrangesShortLastItemAtViewportBottomImmediately()
    {
        var heights = new ObservableCollection<double>(Enumerable.Repeat(40d, 10));
        var presenter = new VariableHeightItemsPresenter
        {
            ItemsSource = new ItemsView<double>(heights),
            ItemTemplate = new DelegateTemplate<double>(
                build: _ => new HeightElement(),
                bind: static (view, height, _, _) => ((HeightElement)view).ItemHeight = height),
        };

        presenter.OffsetCorrectionRequested += presenter.SetOffset;
        presenter.SetViewport(new Size(300, 100));
        presenter.Measure(new Size(300, 100));
        presenter.Arrange(new Rect(0, 0, 300, 100));

        presenter.RequestScrollIntoView(heights.Count - 1);
        presenter.Arrange(new Rect(0, 0, 300, 100));

        // The running estimate is now 40. Append an item that is 20 DIPs shorter,
        // matching the gallery chat case where a short message follows taller rows.
        heights.Add(20);
        presenter.RequestScrollIntoView(heights.Count - 1);
        presenter.Arrange(new Rect(0, 0, 300, 100));

        FrameworkElement? last = null;
        presenter.VisitRealized((index, element) =>
        {
            if (index == heights.Count - 1)
            {
                last = element;
            }
        });

        Assert.IsNotNull(last);
        Assert.AreEqual(100, last.Bounds.Bottom, 0.001,
            "The appended item must use its measured height before the bottom offset is calculated.");
    }

    [TestMethod]
    public void AnchorCorrectionRepositionsContainersInTheSamePass()
    {
        // Short and tall rows alternate so the running estimate is far off and every jump into
        // unmeasured territory triggers an anchor correction.
        var heights = new ObservableCollection<double>(Enumerable.Range(0, 4000).Select(index => index % 4 == 0 ? 160d : 24d));
        var presenter = new VariableHeightItemsPresenter
        {
            ItemsSource = new ItemsView<double>(heights),
            ItemTemplate = new DelegateTemplate<double>(
                build: _ => new HeightElement(),
                bind: static (view, height, _, _) => ((HeightElement)view).ItemHeight = height),
        };
        presenter.OffsetCorrectionRequested += presenter.SetOffset;
        var viewport = new Size(300, 482);
        presenter.SetViewport(viewport);
        presenter.Measure(viewport);
        presenter.Arrange(new Rect(0, 0, viewport.Width, viewport.Height));

        presenter.SetOffset(new Point(0, 100000));
        presenter.Arrange(new Rect(0, 0, viewport.Width, viewport.Height));

        for (int jump = 0; jump < 12; jump++)
        {
            double offsetBefore = ReadOffset(presenter);
            presenter.SetOffset(new Point(0, Math.Max(0, offsetBefore - 3000)));
            presenter.Arrange(new Rect(0, 0, viewport.Width, viewport.Height));

            double coveredTop = double.PositiveInfinity, coveredBottom = double.NegativeInfinity;
            presenter.VisitRealized((Element element) =>
            {
                coveredTop = Math.Min(coveredTop, element.Bounds.Y);
                coveredBottom = Math.Max(coveredBottom, element.Bounds.Bottom);
            });
            Assert.IsTrue(coveredTop <= 1 && coveredBottom >= viewport.Height - 1,
                $"jump {jump}: after the arrange pass the containers cover [{coveredTop:F1}, {coveredBottom:F1}] instead of the viewport [0, {viewport.Height}]");
        }
    }

    private static double ReadOffset(VariableHeightItemsPresenter presenter)
    {
        // The presenter mirrors the owner's offset; the last corrected value is what the owner holds.
        double offset = 0;
        presenter.VisitRealized((int index, FrameworkElement element) =>
        {
            presenter.TryGetItemYRange(index, out double top, out _);
            offset = top - element.Bounds.Y;
        });
        return offset;
    }

    private sealed class HeightElement : FrameworkElement
    {
        public double ItemHeight { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(availableSize.Width, ItemHeight);
    }
}
