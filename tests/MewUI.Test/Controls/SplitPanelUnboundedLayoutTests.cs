using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A SplitPanel measured without a width bound (a horizontally scrolling ScrollViewer) arranges each
/// pane at least as wide as it measured, so a WrapPanel in a pane keeps the rows it measured instead of
/// wrapping into rows that spill over the element below (issue #298).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SplitPanelUnboundedLayoutTests
{
    private const int ITEM_COUNT = 32;
    private const double ITEM_WIDTH = 30;
    private const double ITEM_HEIGHT = 20;
    private const double SPACING = 8;
    private const double SPLITTER = 8;
    private const double WINDOW_WIDTH = 600;
    private const double ONE_ROW_WIDTH = ITEM_COUNT * ITEM_WIDTH + (ITEM_COUNT - 1) * SPACING;

    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    private sealed record Host(SplitPanel Split, Border First, WrapPanel Wrap, Border Last, Border Below);

    private static Host Build(ScrollMode horizontalScroll, GridLength firstLength)
    {
        var items = Enumerable.Range(0, ITEM_COUNT)
            .Select(_ => new Border { Width = ITEM_WIDTH, Height = ITEM_HEIGHT })
            .ToArray();
        var first = new Border();
        var wrap = new WrapPanel().Horizontal().Spacing(SPACING).DockTop().Children(items);
        var below = new Border();
        var split = new SplitPanel
        {
            First = first,
            Second = new DockPanel().Children(wrap, below),
            SplitterThickness = SPLITTER,
            FirstLength = firstLength,
        };

        var window = HeadlessWindow.Create(WINDOW_WIDTH, 400);
        window.Content = new ScrollViewer().HorizontalScroll(horizontalScroll).Content(split);
        window.PerformLayout();
        return new Host(split, first, wrap, items[^1], below);
    }

    private static void AssertRowsStayInside(Host host)
    {
        Assert.IsLessThanOrEqualTo(host.Wrap.Bounds.Bottom + 0.5, host.Last.Bounds.Bottom,
            $"the last item ({host.Last.Bounds}) spills below the WrapPanel ({host.Wrap.Bounds})");
        Assert.IsGreaterThanOrEqualTo(host.Last.Bounds.Bottom - 0.5, host.Below.Bounds.Top,
            $"the element below ({host.Below.Bounds}) overlaps the last item ({host.Last.Bounds})");
    }

    [TestMethod]
    public void HorizontalScroll_StarPanes_SecondPaneKeepsItsContentWidth()
    {
        if (SkipOnNonWindows()) return;

        var host = Build(ScrollMode.Auto, GridLength.Star);

        Assert.AreEqual(ONE_ROW_WIDTH, host.Wrap.Bounds.Width, 0.5, "the WrapPanel is arranged at its one-row width");
        Assert.AreEqual(ITEM_HEIGHT, host.Wrap.Bounds.Height, 0.5, "the items stay on one row");
        AssertRowsStayInside(host);
    }

    [TestMethod]
    public void HorizontalScroll_PixelFirstPane_ExtentIncludesTheFixedLength()
    {
        if (SkipOnNonWindows()) return;

        var host = Build(ScrollMode.Auto, GridLength.Pixels(170));

        Assert.AreEqual(170 + SPLITTER + ONE_ROW_WIDTH, host.Split.DesiredSize.Width, 0.5, "the desired width counts the fixed first pane");
        Assert.AreEqual(170, host.First.Bounds.Width, 0.5, "the first pane keeps its fixed length");
        Assert.AreEqual(ONE_ROW_WIDTH, host.Wrap.Bounds.Width, 0.5, "the WrapPanel is arranged at its one-row width");
        AssertRowsStayInside(host);
    }

    [TestMethod]
    public void DisabledScroll_StarPanes_WrapInsideTheViewport()
    {
        if (SkipOnNonWindows()) return;

        var host = Build(ScrollMode.Disabled, GridLength.Star);

        Assert.IsLessThanOrEqualTo(WINDOW_WIDTH + 0.5, host.Split.Bounds.Width, "the panel stays inside the viewport");
        Assert.AreEqual((host.Split.Bounds.Width - SPLITTER) / 2, host.Wrap.Bounds.Width, 1, "the panes split the viewport by weight");
        Assert.IsGreaterThan(ITEM_HEIGHT + 0.5, host.Wrap.Bounds.Height, "the items wrap into several rows");
        AssertRowsStayInside(host);
    }

    [TestMethod]
    public void DisabledScroll_PixelFirstPane_SecondPaneTakesTheRest()
    {
        if (SkipOnNonWindows()) return;

        var host = Build(ScrollMode.Disabled, GridLength.Pixels(170));

        Assert.IsLessThanOrEqualTo(WINDOW_WIDTH + 0.5, host.Split.Bounds.Width, "the panel stays inside the viewport");
        Assert.AreEqual(170, host.First.Bounds.Width, 0.5, "the first pane keeps its fixed length");
        Assert.AreEqual(host.Split.Bounds.Width - 170 - SPLITTER, host.Wrap.Bounds.Width, 0.5, "the second pane takes the rest of the viewport");
        Assert.IsGreaterThan(ITEM_HEIGHT + 0.5, host.Wrap.Bounds.Height, "the items wrap into several rows");
        AssertRowsStayInside(host);
    }
}
