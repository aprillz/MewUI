using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// The item corner radius is derived during arrange, before the presenter binds its containers, so
/// a container realized on the very first layout already carries the control's radius even though
/// nothing has rendered yet. Later changes to the control's own radius reach the containers that
/// are already realized.
/// </summary>
[TestClass]
public sealed class ItemContainerCornerRadiusTests
{
    private const double WIDTH = 400;
    private const double HEIGHT = 300;

    // Integral at every DPI scale in use (1.0, 1.25, 1.5, 2.0), so pixel rounding leaves them alone
    // and the expected radius does not depend on the DPI the detached control resolves.
    private const double CORNER_RADIUS = 8;
    private const double SMALLER_CORNER_RADIUS = 4;

    [TestMethod]
    public void ListBox_ContainersRealizedOnTheFirstLayout_CarryTheItemRadius()
    {
        var box = CreateListBox();

        Layout(box);

        AssertContainerRadius(box, CORNER_RADIUS, "the first layout");
    }

    [TestMethod]
    public void ListBox_CornerRadiusChange_ReachesTheRealizedContainers()
    {
        var box = CreateListBox();
        Layout(box);

        box.CornerRadius = SMALLER_CORNER_RADIUS;
        Layout(box);

        AssertContainerRadius(box, SMALLER_CORNER_RADIUS, "the radius change");
    }

    [TestMethod]
    public void NavigationList_ContainersRealizedOnTheFirstLayout_CarryTheItemRadius()
    {
        var list = new NavigationList
        {
            ItemsSource = ItemsView.Create(Items()),
            CornerRadius = CORNER_RADIUS,
            BorderThickness = 0,
            Width = WIDTH,
            Height = HEIGHT,
        };

        Layout(list);

        AssertContainerRadius(list, CORNER_RADIUS, "the first layout");
    }

    [TestMethod]
    public void TreeView_RowsRealizedOnTheFirstLayout_CarryTheItemRadius()
    {
        var tree = new TreeView
        {
            CornerRadius = CORNER_RADIUS,
            BorderThickness = 0,
            Width = WIDTH,
            Height = HEIGHT,
        };
        tree.ItemsSource([new TreeViewNode("root"), new TreeViewNode("leaf")]);

        // A tree row is wrapped in a container only while a container hook is registered.
        tree.PrepareContainer<TreeViewNode>((_, _, _, _) => { });

        Layout(tree);

        AssertContainerRadius(tree, CORNER_RADIUS, "the first layout");
    }

    private static ListBox CreateListBox()
        => new()
        {
            ItemsSource = ItemsView.Create(Items()),
            CornerRadius = CORNER_RADIUS,
            BorderThickness = 0,
            Width = WIDTH,
            Height = HEIGHT,
        };

    private static List<string> Items()
        => Enumerable.Range(0, 20).Select(index => "Item " + index).ToList();

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(WIDTH, HEIGHT));
        element.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
    }

    private static void AssertContainerRadius(Element root, double expected, string occasion)
    {
        var containers = VisualTree.FindAll(root, element => element is ItemContainer);
        Assert.IsGreaterThan(0, containers.Count, "no container was realized, so nothing was checked");

        foreach (var element in containers)
        {
            var container = (ItemContainer)element;
            Assert.AreEqual(expected, container.CornerRadius, 1e-9,
                $"a container does not carry the item radius after {occasion}");
        }
    }
}
