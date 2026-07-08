using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

// GridView.SelectedIndex and TreeView.SelectedNode/SelectedItem are bindable MewProperties
// kept in sync with the underlying selection model without divergence or reentrancy loops.
[TestClass]
public sealed class SelectionBindingTests
{
    [TestMethod]
    public void GridView_SelectedIndex_SetGetAndClamp()
    {
        var grid = new GridView();
        grid.SetItemsSource(new[] { "a", "b", "c" });

        Assert.AreEqual(-1, grid.SelectedIndex);

        grid.SelectedIndex = 1;
        Assert.AreEqual(1, grid.SelectedIndex);
        Assert.AreEqual("b", grid.SelectedItem);

        // Out-of-range is coerced by the core and written back to the property.
        grid.SelectedIndex = 99;
        Assert.AreEqual(2, grid.SelectedIndex);

        grid.SelectedIndex = -50;
        Assert.AreEqual(-1, grid.SelectedIndex);
    }

    [TestMethod]
    public void GridView_SelectionChanged_GetterFreshInHandler()
    {
        var grid = new GridView();
        grid.SetItemsSource(new[] { "a", "b", "c" });

        int observed = -99;
        grid.SelectionChanged += _ => observed = grid.SelectedIndex;

        grid.SelectedIndex = 2;
        Assert.AreEqual(2, observed);
    }

    [TestMethod]
    public void GridView_SetItemsSource_ResetsSelectionProperty()
    {
        var grid = new GridView();
        grid.SetItemsSource(new[] { "a", "b", "c" });
        grid.SelectedIndex = 2;
        Assert.AreEqual(2, grid.SelectedIndex);

        // A wholesale replacement with unrelated items clears selection; the bindable
        // property must not stay stale at 2.
        grid.SetItemsSource(new[] { "x", "y", "z" });
        Assert.AreEqual(-1, grid.SelectedIndex);
    }

    [TestMethod]
    public void GridView_TwoWayBinding_SelectedIndex()
    {
        var grid = new GridView();
        grid.SetItemsSource(new[] { "a", "b", "c" });

        var source = new ObservableValue<int>(0);
        grid.SetBinding(GridView.SelectedIndexProperty, source);

        // source -> control
        source.Value = 2;
        Assert.AreEqual(2, grid.SelectedIndex);
        Assert.AreEqual("c", grid.SelectedItem);

        // control -> source (BindsTwoWayByDefault)
        grid.SelectedIndex = 1;
        Assert.AreEqual(1, source.Value);
    }

    [TestMethod]
    public void GridView_ForwardBinding_FlowsToSelectedIndex()
    {
        var grid = new GridView();
        grid.SetItemsSource(new[] { "a", "b", "c" });

        // Property-to-property forward binding writes at the local tier, so it drives the
        // control's selection instead of losing to the selection value the control sets.
        var source = new IndexSource();
        grid.SetBinding(GridView.SelectedIndexProperty, source, IndexSource.ValueProperty);

        source.Value = 2;
        Assert.AreEqual(2, grid.SelectedIndex);
        Assert.AreEqual("c", grid.SelectedItem);
    }

    [TestMethod]
    public void TreeView_SelectedItem_SetGet()
    {
        var tree = new TreeView();
        var nodes = new[] { new Node("n0"), new Node("n1"), new Node("n2") };
        tree.ItemsSource = TreeItemsView.Create<Node>(
            nodes, node => node.Children, textSelector: node => node.Name);

        tree.SelectedItem = nodes[1];
        Assert.AreSame(nodes[1], tree.SelectedItem);

        tree.SelectedItem = null;
        Assert.IsNull(tree.SelectedItem);
    }

    [TestMethod]
    public void TreeView_SelectionChanged_EventAndFreshGetter()
    {
        var tree = new TreeView();
        var nodes = new[] { new Node("n0"), new Node("n1") };
        tree.ItemsSource = TreeItemsView.Create<Node>(
            nodes, node => node.Children, textSelector: node => node.Name);

        object? observed = "sentinel";
        tree.SelectionChanged += _ => observed = tree.SelectedItem;

        tree.SelectedItem = nodes[1];
        Assert.AreSame(nodes[1], observed);
    }

    [TestMethod]
    public void TreeView_TwoWayBinding_SelectedItem()
    {
        var tree = new TreeView();
        var nodes = new[] { new Node("n0"), new Node("n1") };
        tree.ItemsSource = TreeItemsView.Create<Node>(
            nodes, node => node.Children, textSelector: node => node.Name);

        var source = new ObservableValue<object?>(null);
        tree.SetBinding(TreeView.SelectedItemProperty, source);

        // source -> control
        source.Value = nodes[1];
        Assert.AreSame(nodes[1], tree.SelectedItem);

        // control -> source (BindsTwoWayByDefault)
        tree.SelectedItem = nodes[0];
        Assert.AreSame(nodes[0], source.Value);
    }

    [TestMethod]
    public void ListBox_SelectedItem_SetGetAndSyncsWithIndex()
    {
        var list = new ListBox { ItemsSource = ItemsView.Create(new[] { "a", "b", "c" }) };

        list.SelectedItem = "b";
        Assert.AreEqual("b", list.SelectedItem);
        Assert.AreEqual(1, list.SelectedIndex);

        list.SelectedIndex = 2;
        Assert.AreEqual("c", list.SelectedItem);

        // An item not in the source is rejected; SelectedItem self-corrects to the real selection.
        list.SelectedItem = "zzz";
        Assert.AreEqual(2, list.SelectedIndex);
        Assert.AreEqual("c", list.SelectedItem);
    }

    [TestMethod]
    public void ListBox_SelectedItem_TwoWayBinding()
    {
        var list = new ListBox { ItemsSource = ItemsView.Create(new[] { "a", "b", "c" }) };
        var source = new ObservableValue<object?>(null);
        list.SetBinding(ListBox.SelectedItemProperty, source);

        source.Value = "c";
        Assert.AreEqual("c", list.SelectedItem);
        Assert.AreEqual(2, list.SelectedIndex);

        list.SelectedIndex = 0;
        Assert.AreEqual("a", source.Value);
    }

    [TestMethod]
    public void GridView_SelectionMode_SetGetAndPersistsAcrossSourceSwap()
    {
        var grid = new GridView();
        grid.SetItemsSource(new[] { "a", "b", "c" });
        Assert.AreEqual(ItemsSelectionMode.Single, grid.SelectionMode);

        grid.SelectionMode = ItemsSelectionMode.Multiple;
        Assert.AreEqual(ItemsSelectionMode.Multiple, grid.SelectionMode);

        // Mode is a control-level setting; it survives a wholesale source swap.
        grid.SetItemsSource(new[] { "x", "y", "z" });
        Assert.AreEqual(ItemsSelectionMode.Multiple, grid.SelectionMode);
    }

    [TestMethod]
    public void ListBox_SelectionMode_SourceBinding()
    {
        var list = new ListBox();
        var source = new ObservableValue<ItemsSelectionMode>(ItemsSelectionMode.Single);
        list.SetBinding(ListBox.SelectionModeProperty, source);

        source.Value = ItemsSelectionMode.Multiple;
        Assert.AreEqual(ItemsSelectionMode.Multiple, list.SelectionMode);
    }

    private sealed class IndexSource : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<IndexSource>(nameof(Value), 0);

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class Node
    {
        public Node(string name) => Name = name;
        public string Name { get; }
        public List<Node> Children { get; } = [];
    }
}
