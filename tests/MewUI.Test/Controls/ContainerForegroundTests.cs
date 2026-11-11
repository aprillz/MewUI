using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A foreground set on a container reaches every text under it that has no state color of its own,
/// the default item labels of the built-in lists and the segment labels included.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ContainerForegroundTests
{
    private static readonly Color INK = Color.FromRgb(0x33, 0x99, 0xFF);

    [TestMethod]
    public void ListBoxItems_FollowTheContainer_ExceptTheSelectedOne()
    {
        var list = new ListBox().Items("Red", "Green", "Blue");
        list.SelectedIndex = 1;
        var window = Show(list);

        Assert.AreEqual(INK, FindText(list, "Red").Foreground);
        Assert.AreEqual(list.ThemeInternal.Palette.SelectionText, FindText(list, "Green").Foreground);

        list.SelectedIndex = 0;
        window.PerformLayout();
        Assert.AreEqual(INK, FindText(list, "Green").Foreground, "a deselected item keeps its selection color");
    }

    [TestMethod]
    public void TreeViewItemsAndExpander_FollowTheContainer_ExceptTheSelectedOne()
    {
        var leaf = new TreeViewNode("leaf");
        var root = new TreeViewNode("root", [new TreeViewNode("child")]);
        var tree = new TreeView().ItemsSource([root, leaf]);
        tree.SelectedItem = leaf;
        var window = Show(tree);

        Assert.AreEqual(INK, FindText(tree, "root").Foreground);
        Assert.AreEqual(tree.ThemeInternal.Palette.SelectionText, FindText(tree, "leaf").Foreground);

        TreeRowContainer? rootRow = null;
        tree.VisitRealizedContainers((index, element) =>
        {
            if (index == 0)
            {
                rootRow = element as TreeRowContainer;
            }
        });
        Assert.IsNotNull(rootRow);
        Assert.AreEqual(INK, rootRow.Foreground, "an unselected row draws its expander in its inherited foreground");

        tree.SelectedItem = root;
        window.PerformLayout();
        Assert.AreEqual(INK, FindText(tree, "leaf").Foreground, "a deselected item keeps its selection color");
        Assert.AreEqual(tree.ThemeInternal.Palette.SelectionText, FindText(tree, "root").Foreground);
        Assert.AreEqual(tree.ThemeInternal.Palette.SelectionText, rootRow.Foreground, "the expander follows the selection");
    }

    [TestMethod]
    public void DisabledList_DimsTheSelectedItemToo()
    {
        var list = new ListBox().Items("Red", "Green");
        list.SelectedIndex = 1;
        var window = Show(list);
        // Off screen, so the foreground transition of the list lands at once.
        list.Arrange(new Rect(1000, 1000, list.Bounds.Width, list.Bounds.Height));

        list.IsEnabled = false;
        window.UpdateVisualStates();

        Assert.AreEqual(list.ThemeInternal.Palette.DisabledText, FindText(list, "Red").Foreground);
        Assert.AreEqual(list.ThemeInternal.Palette.DisabledText, FindText(list, "Green").Foreground);
    }

    [TestMethod]
    public void GridViewCells_FollowTheContainer_ExceptTheSelectedRow()
    {
        var grid = new GridView { Width = 300, Height = 200 };
        grid.ItemsSource = ItemsView.Create(new[] { "Alpha", "Beta" });
        grid.SetColumns(
        [
            new GridViewColumn<string>
            {
                Header = "Name",
                Width = 200,
                CellTemplate = new DelegateTemplate<string>(
                    build: _ => new TextBlock(),
                    bind: (view, item, _, _) => ((TextBlock)view).Text = item),
            },
        ]);
        grid.SelectedIndex = 1;
        var window = Show(grid);

        Assert.AreEqual(INK, FindText(grid, "Alpha").Foreground);
        Assert.AreEqual(grid.ThemeInternal.Palette.SelectionText, FindText(grid, "Beta").Foreground);

        grid.SelectedIndex = 0;
        window.PerformLayout();
        Assert.AreEqual(INK, FindText(grid, "Beta").Foreground, "a deselected row keeps its selection color");
    }

    [TestMethod]
    public void SegmentLabels_FollowTheContainer()
    {
        var control = new SegmentedControl().Items("Day", "Week").SelectedIndex(0);
        Show(control);

        Assert.AreEqual(INK, FindText(control, "Day").Foreground);
        Assert.AreEqual(INK, FindText(control, "Week").Foreground);
    }

    private static Window Show(UIElement content)
    {
        var window = HeadlessWindow.Create(400, 300);
        window.Content = new Border().Child(content).Foreground(INK);
        window.PerformLayout();
        return window;
    }

    private static TextBlock FindText(Element root, string text)
    {
        var found = Find(root, text);
        Assert.IsNotNull(found, $"no label reads '{text}'");
        return found;
    }

    private static TextBlock? Find(Element root, string text)
    {
        if (root is TextBlock block && block.Text == text)
        {
            return block;
        }

        TextBlock? found = null;
        if (root is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                found = Find(child, text);
                return found == null;
            });
        }

        return found;
    }
}
