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

    private sealed class HeightElement : FrameworkElement
    {
        public double ItemHeight { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(availableSize.Width, ItemHeight);
    }
}
