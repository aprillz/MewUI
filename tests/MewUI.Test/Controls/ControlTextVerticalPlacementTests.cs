using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class ControlTextVerticalPlacementTests
{
    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    [DataRow(192u)]
    public void DefaultButtonListBoxAndTreeView_CenterTextInTheirRows(uint dpi)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The headless text factory is Windows-only."); return; }

        AssertButtonTextIsCentered(dpi);
        AssertListBoxTextIsCentered(dpi);
        AssertTreeViewTextIsCentered(dpi);
    }

    private static void AssertButtonTextIsCentered(uint dpi)
    {
        var text = new AccessText
        {
            RawText = "Default",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
        };
        var button = new Button
        {
            Content = text,
            Width = 160,
        };

        Layout(button, dpi);

        AssertCentersMatch(button.Bounds, text.Bounds, dpi, "Button");
        Assert.AreEqual(TextAlignment.Center, text.VerticalTextAlignment);
    }

    private static void AssertListBoxTextIsCentered(uint dpi)
    {
        var list = new ListBox
        {
            ItemsSource = ItemsView.Create(new[] { "List item" }),
            Width = 160,
            Height = 80,
        };

        Layout(list, dpi);

        var container = GetFirstContainer(list);
        var text = (TextBlock)container.Content!;
        var row = container.Bounds.Inflate(container.RowPadding);
        AssertCentersMatch(row, text.Bounds, dpi, "ListBox");
        Assert.AreEqual(TextAlignment.Center, text.VerticalTextAlignment);
    }

    private static void AssertTreeViewTextIsCentered(uint dpi)
    {
        var tree = new TreeView
        {
            Width = 160,
            Height = 80,
        };
        tree.ItemsSource([new TreeViewNode("Tree item")]);

        Layout(tree, dpi);

        var container = GetFirstContainer(tree);
        var text = (TextBlock)container.Content!;
        var row = container.Bounds.Inflate(container.RowPadding);
        AssertCentersMatch(row, text.Bounds, dpi, "TreeView");
        Assert.AreEqual(TextAlignment.Center, text.VerticalTextAlignment);
    }

    private static void Layout(Control control, uint dpi)
    {
        var window = HeadlessWindow.Create();
        window.SetDpi(dpi);
        window.Content = control;
        window.PerformLayout();
    }

    private static ItemContainer GetFirstContainer(ListBox list)
    {
        ItemContainer? result = null;
        list.VisitRealizedContainers((index, element) =>
        {
            if (index == 0)
            {
                result = (ItemContainer)element;
            }
        });
        Assert.IsNotNull(result, "ListBox did not realize its first row.");
        return result;
    }

    private static ItemContainer GetFirstContainer(TreeView tree)
    {
        ItemContainer? result = null;
        tree.VisitRealizedContainers((index, element) =>
        {
            if (index == 0)
            {
                result = (ItemContainer)element;
            }
        });
        Assert.IsNotNull(result, "TreeView did not realize its first row.");
        return result;
    }

    private static void AssertCentersMatch(Rect row, Rect text, uint dpi, string controlName)
    {
        double rowCenter = row.Y + row.Height / 2;
        double textCenter = text.Y + text.Height / 2;
        double onePixelDip = 96.0 / dpi;
        Assert.AreEqual(rowCenter, textCenter, onePixelDip / 2 + 0.001,
            $"{controlName} text box is not vertically centered at {dpi} DPI.");
    }
}
