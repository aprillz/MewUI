using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A drop-down short enough to show every item must not scroll. The list rounds its row height to device
/// pixels, so at a fractional scale the rows are taller than the nominal height the popup is sized from.
/// </summary>
[TestClass]
public sealed class ComboBoxDropDownHeightTests
{
    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    [DataRow(168u)]
    [DataRow(192u)]
    public void AShortDropDown_ShowsEveryItemWithoutAScrollBar(uint dpi)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create(400, 300);
        window.SetDpi(dpi);
        var combo = new ComboBox { Width = 160 }.Items(["Alpha", "Beta"]);
        window.Content = new StackPanel().Children(combo);
        window.PerformLayout();

        combo.IsDropDownOpen = true;
        window.PerformLayout();
        window.PerformLayout();

        var list = FindPopupList(window);
        Assert.IsNotNull(list, "the drop-down list was not found");

        ScrollBar? vertical = null;
        VisualTree.Visit(list, element =>
        {
            if (element is ScrollBar bar && bar.Orientation == Orientation.Vertical)
            {
                vertical = bar;
            }
        });

        Assert.IsNotNull(vertical, "the drop-down list has no vertical scroll bar element");
        Assert.IsFalse(vertical.IsVisible, $"dpi {dpi}: a two-item drop-down shows a scroll bar (list bounds {list.Bounds})");
        combo.IsDropDownOpen = false;
    }

    private static ListBox? FindPopupList(Window window)
    {
        for (Element? element = window.FocusManager.FocusedElement; element != null; element = element.Parent)
        {
            if (element is ListBox list)
            {
                return list;
            }
        }

        return null;
    }
}
