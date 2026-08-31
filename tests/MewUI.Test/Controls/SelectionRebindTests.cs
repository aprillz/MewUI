using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// Changing the selection repaints; it does not rebind the realized containers. Nothing reads the
/// selection during bind, so rebinding a screenful per arrow key would be pure waste.
/// </summary>
[TestClass]
public sealed class SelectionRebindTests
{
    private const double WIDTH = 400;
    private const double HEIGHT = 300;

    [TestMethod]
    public void ListBox_SelectionChange_DoesNotRebindItems()
    {
        int binds = 0;
        var box = new ListBox
        {
            ItemsSource = ItemsView.Create(Items()),
            ItemTemplate = CountingTemplate(() => binds++),
            Width = WIDTH,
            Height = HEIGHT,
        };
        Layout(box);
        Assert.IsGreaterThan(0, binds, "the first layout must realize items");

        binds = 0;
        box.SelectedIndex = 3;
        Layout(box);
        box.SelectedIndex = 4;
        Layout(box);

        Assert.AreEqual(0, binds);
    }

    [TestMethod]
    public void ListBox_MultiSelectionChange_DoesNotRebindItems()
    {
        int binds = 0;
        // Every item fits on screen, so no selection can scroll one into view and realize a new
        // container: any bind after the first layout is a rebind.
        var box = new ListBox
        {
            ItemsSource = ItemsView.Create(new[] { "a", "b", "c", "d" }),
            ItemTemplate = CountingTemplate(() => binds++),
            SelectionMode = ItemsSelectionMode.Multiple,
            Width = WIDTH,
            Height = HEIGHT,
        };
        Layout(box);
        Assert.IsGreaterThan(0, binds, "the first layout must realize items");

        binds = 0;
        box.SelectRange(1, 2);
        Layout(box);
        box.SelectAll();
        Layout(box);

        Assert.AreEqual(0, binds);
    }

    [TestMethod]
    public void TreeView_SelectionChange_DoesNotRebindItems()
    {
        int binds = 0;
        var nodes = Enumerable.Range(0, 30).Select(i => new Node("Node " + i)).ToArray();
        var tree = new TreeView
        {
            ItemTemplate = CountingTemplate(() => binds++),
            Width = WIDTH,
            Height = HEIGHT,
        };
        tree.ItemsSource = TreeItemsView.Create<Node>(nodes, static node => node.Children, textSelector: static node => node.Name);
        Layout(tree);
        Assert.IsGreaterThan(0, binds, "the first layout must realize items");

        binds = 0;
        tree.SelectedItem = nodes[3];
        Layout(tree);
        tree.SelectedItem = nodes[4];
        Layout(tree);

        Assert.AreEqual(0, binds);
    }

    [TestMethod]
    public void NavigationList_SelectionChange_DoesNotRebindItems()
    {
        int binds = 0;
        var list = new NavigationList
        {
            ItemsSource = ItemsView.Create(Items()),
            ItemTemplate = CountingTemplate(() => binds++),
            Width = WIDTH,
            Height = HEIGHT,
        };
        Layout(list);
        Assert.IsGreaterThan(0, binds, "the first layout must realize items");

        binds = 0;
        list.SelectedIndex = 3;
        Layout(list);
        list.SelectedIndex = 4;
        Layout(list);

        Assert.AreEqual(0, binds);
    }

    [TestMethod]
    public void GridView_SelectionChange_DoesNotRebindCells()
    {
        int binds = 0;
        var grid = new GridView { Width = WIDTH, Height = HEIGHT };
        grid.ItemsSource = ItemsView.Create(Items());
        grid.SetColumns(
        [
            new GridViewColumn<string>
            {
                Header = "Text",
                Width = 200,
                CellTemplate = new DelegateTemplate<string>(
                    build: _ => new TextBlock(),
                    bind: (view, item, _, _) => { binds++; ((TextBlock)view).Text = item; }),
            },
        ]);
        Layout(grid);
        Assert.IsGreaterThan(0, binds, "the first layout must realize cells");

        binds = 0;
        grid.SelectedIndex = 3;
        Layout(grid);
        grid.SelectedIndex = 4;
        Layout(grid);

        Assert.AreEqual(0, binds);
    }

    private static string[] Items() => Enumerable.Range(0, 30).Select(i => "Item " + i).ToArray();

    private static IDataTemplate CountingTemplate(Action onBind)
        => new DelegateTemplate<object?>(
            build: _ => new TextBlock(),
            bind: (view, item, _, _) =>
            {
                onBind();
                ((TextBlock)view).Text = item as string ?? item?.ToString() ?? string.Empty;
            });

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(WIDTH, HEIGHT));
        element.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
    }

    private sealed class Node(string name)
    {
        public string Name { get; } = name;

        public IReadOnlyList<Node>? Children => null;
    }
}
