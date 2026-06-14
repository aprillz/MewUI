using System.Collections.ObjectModel;

using Aprillz.MewUI;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TreeItemsViewTests
{
    [TestMethod]
    public void IsExpandable_DefaultsToLoadedChildCount()
    {
        var leaf = new TestNode("leaf");
        var branch = new TestNode("branch");
        branch.Children.Add(new TestNode("child"));
        var view = CreateView([leaf, branch]);

        Assert.IsFalse(view.GetHasChildren(0));
        Assert.IsTrue(view.GetHasChildren(1));
    }

    [TestMethod]
    public void IsExpandableSelector_OverridesLoadedChildCount()
    {
        var lazyBranch = new TestNode("lazy") { IsExpandable = true };
        var forcedLeaf = new TestNode("leaf") { IsExpandable = false };
        forcedLeaf.Children.Add(new TestNode("hidden"));
        var view = CreateView([lazyBranch, forcedLeaf], useExpandableSelector: true);

        Assert.IsTrue(view.GetHasChildren(0));
        Assert.IsFalse(view.GetHasChildren(1));

        view.SetIsExpanded(1, true);

        Assert.IsFalse(view.GetIsExpanded(1));
        Assert.AreEqual(2, view.Count);
    }

    [TestMethod]
    public void ObservableChildren_UpdateExpandedVisibleRows()
    {
        var root = new TestNode("root") { IsExpandable = true };
        var view = CreateView([root], useExpandableSelector: true);
        view.SetIsExpanded(0, true);

        var child = new TestNode("child");
        root.Children.Add(child);

        Assert.AreEqual(2, view.Count);
        Assert.AreSame(child, view.GetItem(1));
        Assert.AreEqual(1, view.GetDepth(1));

        root.Children.Remove(child);

        Assert.AreEqual(1, view.Count);
    }

    [TestMethod]
    public void Collapsing_UnsubscribesHiddenDescendantCollections()
    {
        var branch = new TestNode("branch") { IsExpandable = true };
        var root = new TestNode("root") { IsExpandable = true };
        root.Children.Add(branch);
        var view = CreateView([root], useExpandableSelector: true);

        view.SetIsExpanded(0, true);
        view.SetIsExpanded(1, true);
        view.SetIsExpanded(0, false);

        int changes = 0;
        view.Changed += _ => changes++;

        branch.Children.Add(new TestNode("hidden-child"));

        Assert.AreEqual(0, changes);

        view.SetIsExpanded(0, true);

        Assert.AreEqual(3, view.Count);
    }

    [TestMethod]
    public void SharedObservableChildren_AreSubscribedOnce()
    {
        var shared = new ObservableCollection<TestNode>();
        var first = new TestNode("first") { Children = shared };
        var second = new TestNode("second") { Children = shared };
        var view = CreateView([first, second]);
        int changes = 0;
        view.Changed += _ => changes++;

        shared.Add(new TestNode("child"));

        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void CollectionChange_PreservesSelectionByKey()
    {
        var first = new TestNode("first");
        var second = new TestNode("second");
        var root = new TestNode("root") { IsExpandable = true };
        root.Children.Add(first);
        root.Children.Add(second);
        var view = CreateView([root], useExpandableSelector: true);
        view.SetIsExpanded(0, true);
        view.SelectedItem = second;

        root.Children.Insert(0, new TestNode("inserted"));

        Assert.AreSame(second, view.SelectedItem);
        Assert.AreEqual(3, view.SelectedIndex);
    }

    [TestMethod]
    public void Invalidate_RebuildsVisibleRowsAndExpandability()
    {
        var root = new TestNode("root") { IsExpandable = true };
        var view = CreateView([root], useExpandableSelector: true);
        view.SetIsExpanded(0, true);

        var child = new TestNode("child");
        root.Children = new ObservableCollection<TestNode> { child };
        view.Invalidate();

        Assert.AreEqual(2, view.Count);
        Assert.AreSame(child, view.GetItem(1));

        root.IsExpandable = false;
        view.Invalidate();

        Assert.IsFalse(view.GetHasChildren(0));
    }

    [TestMethod]
    public void CollectionChangeDuringChanged_IsMergedWithoutRecursiveRefresh()
    {
        var root = new TestNode("root") { IsExpandable = true };
        var view = CreateView([root], useExpandableSelector: true);
        view.SetIsExpanded(0, true);
        int changes = 0;

        view.Changed += _ =>
        {
            changes++;
            if (root.Children.Count == 1)
            {
                root.Children.Add(new TestNode("second"));
            }
        };

        root.Children.Add(new TestNode("first"));

        Assert.AreEqual(3, view.Count);
        Assert.AreEqual(2, changes);
    }

    private static TreeItemsView<TestNode> CreateView(
        IReadOnlyList<TestNode> roots,
        bool useExpandableSelector = false)
        => TreeItemsView.Create(
            roots,
            node => node.Children,
            textSelector: node => node.Name,
            keySelector: node => node.Name,
            isExpandableSelector: useExpandableSelector ? node => node.IsExpandable : null);

    private sealed class TestNode
    {
        public TestNode(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public bool IsExpandable { get; set; }
        public ObservableCollection<TestNode> Children { get; set; } = [];
    }
}
