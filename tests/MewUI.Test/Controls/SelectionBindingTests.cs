using System.Collections.ObjectModel;

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
        grid.ItemsSource = ItemsView.Create(new[] { "a", "b", "c" });

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
        grid.ItemsSource = ItemsView.Create(new[] { "a", "b", "c" });

        int observed = -99;
        grid.SelectionChanged += _ => observed = grid.SelectedIndex;

        grid.SelectedIndex = 2;
        Assert.AreEqual(2, observed);
    }

    [TestMethod]
    public void GridView_ItemsSource_ResetsSelectionProperty()
    {
        var grid = new GridView();
        grid.ItemsSource = ItemsView.Create(new[] { "a", "b", "c" });
        grid.SelectedIndex = 2;
        Assert.AreEqual(2, grid.SelectedIndex);

        // A wholesale replacement with unrelated items clears selection; the bindable
        // property must not stay stale at 2.
        grid.ItemsSource = ItemsView.Create(new[] { "x", "y", "z" });
        Assert.AreEqual(-1, grid.SelectedIndex);
    }

    [TestMethod]
    public void GridView_TwoWayBinding_SelectedIndex()
    {
        var grid = new GridView();
        grid.ItemsSource = ItemsView.Create(new[] { "a", "b", "c" });

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
        grid.ItemsSource = ItemsView.Create(new[] { "a", "b", "c" });

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
    public void GridView_SelectedItem_SetGetAndSyncsWithIndex()
    {
        var grid = new GridView();
        grid.ItemsSource = ItemsView.Create(new[] { "a", "b", "c" });

        grid.SelectedItem = "b";
        Assert.AreEqual("b", grid.SelectedItem);
        Assert.AreEqual(1, grid.SelectedIndex);

        grid.SelectedIndex = 2;
        Assert.AreEqual("c", grid.SelectedItem);
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
    public void ListBox_SelectedItems_ReflectsSelection()
    {
        var list = new ListBox { ItemsSource = ItemsView.Create(new[] { "a", "b", "c" }) };
        Assert.AreEqual(0, list.SelectedItems.Count);

        list.SelectedIndex = 1;
        Assert.AreEqual(1, list.SelectedItems.Count);
        Assert.AreEqual("b", list.SelectedItems[0]);

        list.SelectedIndex = -1;
        Assert.AreEqual(0, list.SelectedItems.Count);
    }

    [TestMethod]
    public void SelectedItem_IsFreshInSelectionChangedHandler_OnProgrammaticSet()
    {
        // Regression: a consumer that reads the control's SelectedItem getter inside the
        // SelectionChanged handler (e.g. NavigationView.UpdateContent) must see the new item,
        // not a stale value, even when selection was set programmatically via SelectedIndex.
        var list = new ListBox { ItemsSource = ItemsView.Create(new[] { "a", "b", "c" }) };
        object? observed = "sentinel";
        list.SelectionChanged += _ => observed = list.SelectedItem;

        list.SelectedIndex = 1;
        Assert.AreEqual("b", observed);
    }

    [TestMethod]
    public void RemovingSelectedLastItem_DoesNotThrow()
    {
        // Regression: removing the selected last item fires SelectionChanged before the view has
        // remapped its selected set, so the SelectedItems projection briefly saw an out-of-range
        // index and threw from GetItem.
        var data = new ObservableCollection<string> { "a", "b", "c" };
        var list = new ListBox { ItemsSource = ItemsView.Create(data) };
        list.SelectedIndex = 2;

        data.RemoveAt(2);

        Assert.IsTrue(list.SelectedIndex < 2);
        foreach (var item in list.SelectedItems)
            Assert.IsTrue(data.Contains(item));
    }

    [TestMethod]
    public void MultiSelect_CollectionMutations_StayConsistent()
    {
        var data = new ObservableCollection<string> { "a", "b", "c", "d" };
        var list = new ListBox { ItemsSource = ItemsView.Create(data) };
        list.SelectionMode = ItemsSelectionMode.Multiple;
        list.SelectRange(1, 3);

        data.RemoveAt(3);
        foreach (var item in list.SelectedItems)
            Assert.IsTrue(data.Contains(item));

        data.Insert(0, "z");
        foreach (var item in list.SelectedItems)
            Assert.IsTrue(data.Contains(item));

        data.Clear();
        Assert.AreEqual(0, list.SelectedItems.Count);
        Assert.AreEqual(-1, list.SelectedIndex);
    }

    [TestMethod]
    public void Selector_Interfaces_AreSatisfied()
    {
        var list = new ListBox { ItemsSource = ItemsView.Create(new[] { "a", "b", "c" }) };

        ISelector sel = list;
        sel.SelectedItem = "b";
        Assert.AreEqual("b", sel.SelectedItem);

        IIndexedSelector indexed = list;
        indexed.SelectedIndex = 2;
        Assert.AreEqual(2, indexed.SelectedIndex);

        IMultiSelector multi = list;
        multi.SelectionMode = ItemsSelectionMode.Multiple;
        multi.SelectAll();
        Assert.AreEqual(3, multi.SelectedItems.Count);
        Assert.IsTrue(multi.IsSelected(0));
        multi.ClearSelection();
        Assert.AreEqual(0, multi.SelectedIndices.Count);

        // TreeView is a multi-selector but not an indexed selector (node-based).
        var tree = new TreeView();
        Assert.IsInstanceOfType<ISelector>(tree);
        Assert.IsInstanceOfType<IMultiSelector>(tree);
        Assert.IsFalse(tree is IIndexedSelector);
    }

    [TestMethod]
    public void ListBox_MultiSelect_OperationsAndProjection()
    {
        var list = new ListBox { ItemsSource = ItemsView.Create(new[] { "a", "b", "c" }) };
        list.SelectionMode = ItemsSelectionMode.Multiple;

        list.SelectAll();
        Assert.AreEqual(3, list.SelectedIndices.Count);
        Assert.AreEqual(3, list.SelectedItems.Count);
        Assert.IsTrue(list.IsSelected(0));
        Assert.IsTrue(list.IsSelected(2));

        list.ClearSelection();
        Assert.AreEqual(0, list.SelectedItems.Count);
        Assert.IsFalse(list.IsSelected(1));

        list.SelectRange(1, 2);
        Assert.AreEqual(2, list.SelectedItems.Count);
        Assert.AreEqual("b", list.SelectedItems[0]);
        Assert.AreEqual("c", list.SelectedItems[1]);
        Assert.IsFalse(list.IsSelected(0));
    }

    [TestMethod]
    public void GridView_SelectionMode_SetGetAndPersistsAcrossSourceSwap()
    {
        var grid = new GridView();
        grid.ItemsSource = ItemsView.Create(new[] { "a", "b", "c" });
        Assert.AreEqual(ItemsSelectionMode.Single, grid.SelectionMode);

        grid.SelectionMode = ItemsSelectionMode.Multiple;
        Assert.AreEqual(ItemsSelectionMode.Multiple, grid.SelectionMode);

        // Mode is a control-level setting; it survives a wholesale source swap.
        grid.ItemsSource = ItemsView.Create(new[] { "x", "y", "z" });
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
