using System.Collections.ObjectModel;

using Aprillz.MewUI;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TreeViewMultiSelectTests
{
    [TestMethod]
    public void Extended_FlatTree_CtrlAndShift()
    {
        var view = CreateFlatView(6, ItemsSelectionMode.Extended);

        ItemsSelectionInput.HandleClick(view, 1, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 3, ModifierKeys.Control);
        Assert.AreEqual("1,3", Set(view));

        ItemsSelectionInput.HandleClick(view, 5, ModifierKeys.Shift); // Extended shift replaces with range from anchor 3
        Assert.AreEqual("3,4,5", Set(view));

        // Ctrl+Shift adds the range instead of replacing.
        ItemsSelectionInput.HandleClick(view, 0, ModifierKeys.Control);
        ItemsSelectionInput.HandleClick(view, 1, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.AreEqual("0,1,3,4,5", Set(view));
    }

    [TestMethod]
    public void Multiple_FlatTree_Toggle()
    {
        var view = CreateFlatView(5, ItemsSelectionMode.Multiple);

        ItemsSelectionInput.HandleClick(view, 0, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 2, ModifierKeys.None);
        Assert.AreEqual("0,2", Set(view));

        ItemsSelectionInput.HandleClick(view, 0, ModifierKeys.None);
        Assert.AreEqual("2", Set(view));
    }

    [TestMethod]
    public void RangeFollowsFlattenedVisibleOrder()
    {
        // root0 (expanded: c0a, c0b), root1
        var c0a = new Node("c0a");
        var c0b = new Node("c0b");
        var root0 = new Node("root0") { IsExpandable = true, Children = { c0a, c0b } };
        var root1 = new Node("root1");
        var view = CreateView([root0, root1]);
        view.SetIsExpanded(0, true); // visible: root0(0), c0a(1), c0b(2), root1(3)
        view.SelectionMode = ItemsSelectionMode.Extended;

        ItemsSelectionInput.HandleClick(view, 0, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 3, ModifierKeys.Shift);
        Assert.AreEqual("0,1,2,3", Set(view));
    }

    [TestMethod]
    public void Selection_PersistsByKey_AcrossCollapseExpand()
    {
        var c0 = new Node("c0");
        var c1 = new Node("c1");
        var root = new Node("root") { IsExpandable = true, Children = { c0, c1 } };
        var view = CreateView([root]);
        view.SetIsExpanded(0, true); // root(0), c0(1), c1(2)
        view.SelectionMode = ItemsSelectionMode.Extended;

        ItemsSelectionInput.HandleClick(view, 1, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 2, ModifierKeys.Control);
        Assert.AreEqual("1,2", Set(view));

        view.SetIsExpanded(0, false); // children hidden
        Assert.AreEqual(string.Empty, Set(view));

        view.SetIsExpanded(0, true); // children visible again
        Assert.AreEqual("1,2", Set(view));
    }

    [TestMethod]
    public void SwitchToSingle_CollapsesToPrimary()
    {
        var view = CreateFlatView(5, ItemsSelectionMode.Extended);
        ItemsSelectionInput.HandleClick(view, 0, ModifierKeys.None);
        ItemsSelectionInput.HandleClick(view, 2, ModifierKeys.Control);
        ItemsSelectionInput.HandleClick(view, 4, ModifierKeys.Control);

        view.SelectionMode = ItemsSelectionMode.Single;

        Assert.AreEqual("4", Set(view));
    }

    private static string Set(IMultiSelectableItemsView view) => string.Join(",", view.SelectedIndices);

    private static TreeItemsView<Node> CreateFlatView(int count, ItemsSelectionMode mode)
    {
        var roots = new List<Node>();
        for (int index = 0; index < count; index++)
        {
            roots.Add(new Node($"n{index}"));
        }
        return CreateView(roots, mode);
    }

    private static TreeItemsView<Node> CreateView(IReadOnlyList<Node> roots, ItemsSelectionMode mode = ItemsSelectionMode.Single)
    {
        var view = TreeItemsView.Create<Node>(
            roots,
            node => node.Children,
            textSelector: node => node.Name,
            keySelector: node => node.Name,
            isExpandableSelector: node => node.IsExpandable);
        view.SelectionMode = mode;
        return view;
    }

    private sealed class Node
    {
        public Node(string name) => Name = name;
        public string Name { get; }
        public bool IsExpandable { get; set; }
        public ObservableCollection<Node> Children { get; set; } = [];
    }
}
