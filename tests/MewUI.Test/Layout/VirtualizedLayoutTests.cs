using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Layout;

/// <summary>Virtualizing item hosts settle every scroll step with wrapped items inside their boxes and edges on device pixels.</summary>
[TestClass]
[DoNotParallelize]
public sealed class VirtualizedLayoutTests
{
    private const int ITEM_COUNT = 120;
    private const int SCROLL_STEPS = 40;
    private const double SCROLL_STEP = 37.3;

    [TestMethod]
    [LayoutDpis]
    public void VariableHeightItems(uint dpi) => Scroll(dpi, items => items.VariableHeightPresenter(), wrapped: true);

    /// <summary>Rows of a fixed height clip what does not fit by design, so they hold single line labels.</summary>
    [TestMethod]
    [LayoutDpis]
    public void FixedHeightItems(uint dpi) => Scroll(dpi, items => items.FixedHeightPresenter(), wrapped: false);

    [TestMethod]
    [LayoutDpis]
    public void StackedItems(uint dpi) => Scroll(dpi, items => items.StackPresenter(), wrapped: true);

    private static void Scroll(uint dpi, Func<ItemsControl, ItemsControl> presenter, bool wrapped)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI text measurement is Windows-only.");
            return;
        }

        var items = presenter(new ItemsControl
        {
            Width = 301.7,
            Height = 300.3,
            ItemsSource = new ItemsView<int>(Enumerable.Range(0, ITEM_COUNT).ToArray()),
            ItemTemplate = new DelegateTemplate<int>(
                build: _ => wrapped ? LayoutTexts.Wrapped(0) : LayoutTexts.Label(""),
                bind: static (view, item, _, _) => ((TextBlock)view).Text = LayoutTexts.Wrapped(item).Text),
        });
        using var window = LayoutSweep.Host(items, dpi);
        var layoutCase = new LayoutCase { Build = () => items };
        double scale = dpi / 96.0;

        var violations = new List<string>();
        void Report(string message) => violations.Add(message);

        window.PerformLayout();
        LayoutSweep.Check(window, items, layoutCase, scale, "first layout", Report);
        for (int step = 1; step <= SCROLL_STEPS * 2; step++)
        {
            double delta = step <= SCROLL_STEPS ? SCROLL_STEP : -SCROLL_STEP;
            items.SetScrollOffsets(0, Math.Max(0, items.VerticalOffset + delta));
            window.PerformLayout();
            LayoutSweep.Check(window, items, layoutCase, scale, $"step {step} offset {items.VerticalOffset:0.###}", Report);
        }

        if (violations.Count > 0)
        {
            Assert.Fail($"{violations.Count} layout violations at {dpi} DPI, first:\n" + string.Join("\n", violations.Take(12)));
        }
    }
}
