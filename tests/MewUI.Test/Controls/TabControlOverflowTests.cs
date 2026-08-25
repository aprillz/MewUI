using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class TabControlOverflowTests
{
    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    private static (Window window, TabControl tabs, DropDownButton overflow) Host(double width, int tabCount)
    {
        var window = HeadlessWindow.Create();
        var tabs = new TabControl { Width = width, Height = 120 };
        for (int i = 0; i < tabCount; i++)
        {
            tabs.AddTab(new TabItem
            {
                Header = new TextBlock { Text = $"Tab number {i}" },
                HeaderText = $"Tab number {i}",
                Content = new Border(),
            });
        }

        window.Content = tabs;
        window.PerformLayout();

        var overflow = (DropDownButton)VisualTree.Find(tabs, e => e is DropDownButton)!;
        return (window, tabs, overflow);
    }

    private static List<MenuItem> MenuItems(DropDownButton overflow)
        => overflow.DropDownMenu!.Items.OfType<MenuItem>().ToList();

    [TestMethod]
    public void OverflowButton_KeepsItsHeightAndCentresInTheStrip()
    {
        if (SkipOnNonWindows()) return;

        var (_, tabs, overflow) = Host(width: 200, tabCount: 8);
        var header = VisualTree.Find(tabs, e => e is TabHeaderButton && e.Bounds.Height > 0)!;

        Assert.IsTrue(overflow.Bounds.Height > 0, "the overflow button was not laid out");
        Assert.AreEqual(18, overflow.Bounds.Height,
            $"the chevron stretched to the strip instead of keeping its own height (overflow={overflow.Bounds})");
        Assert.AreEqual(
            header.Bounds.Y + ((header.Bounds.Height - overflow.Bounds.Height) / 2),
            overflow.Bounds.Y,
            $"the chevron is not centred in the strip (overflow={overflow.Bounds}, header={header.Bounds})");
    }

    [TestMethod]
    public void OverflowMenu_ListsOnlyHiddenTabs()
    {
        if (SkipOnNonWindows()) return;

        var (window, tabs, overflow) = Host(width: 160, tabCount: 6);

        overflow.IsDropDownOpen = true;

        var items = MenuItems(overflow);
        Assert.IsTrue(items.Count > 0, "a narrow strip hides tabs");
        Assert.IsTrue(items.Count < 6, "the visible tabs are not listed");
        Assert.IsFalse(items.Any(item => item.ToString() == "Tab number 0"),
            "the leading tab stays visible, so it is not in the menu");
    }

    [TestMethod]
    public async Task OverflowMenuItem_CommitsSelection()
    {
        if (SkipOnNonWindows()) return;

        var (window, tabs, overflow) = Host(width: 160, tabCount: 6);

        overflow.IsDropDownOpen = true;
        var last = MenuItems(overflow)[^1];

        bool executed = await window.CommandRouter.ExecuteAsync(last.Command!, CommandTarget.From(overflow));

        Assert.IsTrue(executed);
        Assert.AreEqual(5, tabs.SelectedIndex, "the menu item selects its tab");
    }

    [TestMethod]
    public void DisabledTab_IsDisabledInOverflowMenu()
    {
        if (SkipOnNonWindows()) return;

        var (window, tabs, overflow) = Host(width: 160, tabCount: 6);
        tabs.Tabs[5].IsEnabled = false;
        window.PerformLayout();

        overflow.IsDropDownOpen = true;
        var last = MenuItems(overflow)[^1];

        Assert.IsFalse(window.CommandRouter.CanExecute(last.Command!, CommandTarget.From(overflow)),
            "a disabled tab cannot be selected from the menu");
    }

    [TestMethod]
    public void Reopening_DoesNotAccumulateItems()
    {
        if (SkipOnNonWindows()) return;

        var (window, tabs, overflow) = Host(width: 160, tabCount: 6);

        overflow.IsDropDownOpen = true;
        int first = MenuItems(overflow).Count;
        overflow.IsDropDownOpen = false;

        overflow.IsDropDownOpen = true;
        Assert.AreEqual(first, MenuItems(overflow).Count, "each open rebuilds the menu from scratch");
    }

    [TestMethod]
    public void WideningStrip_ClosesOpenOverflowMenu()
    {
        if (SkipOnNonWindows()) return;

        var (window, tabs, overflow) = Host(width: 160, tabCount: 6);

        overflow.IsDropDownOpen = true;
        Assert.IsTrue(overflow.IsDropDownOpen);

        tabs.Width = 1200;
        window.PerformLayout();

        Assert.IsFalse(overflow.IsDropDownOpen, "the menu closes once nothing is hidden any more");
    }
}
