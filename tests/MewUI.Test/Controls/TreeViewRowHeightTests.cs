using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A tree and a list are shown side by side, so their rows have the same default height. A tree's own
/// ItemHeight still wins over the default.
/// Not parallelizable: lays out through the process-wide application defaults that other tests assign.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TreeViewRowHeightTests
{
    [TestMethod]
    public void DefaultRowPitch_IsThatOfAListBox()
    {
        double list = RowPitch(new ListBox().Items(new[] { "a", "b", "c" }));

        var tree = new TreeView();
        tree.ItemsSource([new TreeViewNode("a"), new TreeViewNode("b"), new TreeViewNode("c")]);

        Assert.AreEqual(list, RowPitch(tree), 0.01);
    }

    [TestMethod]
    public void OwnItemHeight_WinsOverTheDefault()
    {
        var tree = new TreeView { ItemHeight = 34 };
        tree.ItemsSource([new TreeViewNode("a"), new TreeViewNode("b")]);

        Assert.AreEqual(34, RowPitch(tree), 0.01);
    }

    /// <summary>Distance between the tops of the first two rows the control realized.</summary>
    private static double RowPitch(FrameworkElement control)
    {
        control.Width = 240;
        control.Height = 200;
        var window = HeadlessWindow.Create(300, 260);
        window.Content = control;
        window.PerformLayout();

        var tops = new List<double>();
        VisualTree.Visit(control, element =>
        {
            if (element is ItemContainer container)
            {
                tops.Add(container.Bounds.Y);
            }
        });

        tops.Sort();
        Assert.IsGreaterThanOrEqualTo(2, tops.Count, $"{control.GetType().Name} realized {tops.Count} rows");
        return tops[1] - tops[0];
    }
}
