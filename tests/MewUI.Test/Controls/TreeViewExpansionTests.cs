using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TreeViewExpansionTests
{
    [TestMethod]
    public void Expand_RaisesEventBeforeStateChange()
    {
        var children = new ObservableCollection<TreeViewNode>();
        var root = new TreeViewNode("root", children, tag: true);
        var view = CreateView([root]);
        var tree = new TreeView { ItemsSource = view };
        int events = 0;

        tree.Expanding += e =>
        {
            events++;
            Assert.AreSame(root, e.Item);
            Assert.IsFalse(view.GetIsExpanded(0));
            children.Add(new TreeViewNode("child"));
        };

        tree.Expand(root);

        Assert.AreEqual(1, events);
        Assert.IsTrue(view.GetIsExpanded(0));
        Assert.AreEqual(2, view.Count);
    }

    [TestMethod]
    public void Collapse_RaisesEventBeforeStateChange()
    {
        var root = new TreeViewNode("root", [new TreeViewNode("child")], tag: true);
        var view = CreateView([root]);
        var tree = new TreeView { ItemsSource = view };
        tree.Expand(root);
        int events = 0;

        tree.Collapsing += e =>
        {
            events++;
            Assert.AreSame(root, e.Item);
            Assert.IsTrue(view.GetIsExpanded(0));
        };

        tree.Collapse(root);

        Assert.AreEqual(1, events);
        Assert.IsFalse(view.GetIsExpanded(0));
    }

    [TestMethod]
    public void RepeatedStateRequest_DoesNotRaiseEventAgain()
    {
        var root = new TreeViewNode("root", [new TreeViewNode("child")], tag: true);
        var tree = new TreeView { ItemsSource = CreateView([root]) };
        int expanding = 0;
        int collapsing = 0;
        tree.Expanding += _ => expanding++;
        tree.Collapsing += _ => collapsing++;

        tree.Expand(root);
        tree.Expand(root);
        tree.Collapse(root);
        tree.Collapse(root);

        Assert.AreEqual(1, expanding);
        Assert.AreEqual(1, collapsing);
    }

    [TestMethod]
    public void Leaf_DoesNotRaiseExpanding()
    {
        var leaf = new TreeViewNode("leaf", tag: false);
        var tree = new TreeView { ItemsSource = CreateView([leaf]) };
        int events = 0;
        tree.Expanding += _ => events++;

        tree.Expand(leaf);

        Assert.AreEqual(0, events);
        Assert.IsFalse(tree.IsExpanded(leaf));
    }

    [TestMethod]
    public void ItemRemovedDuringExpanding_IsNotExpanded()
    {
        var roots = new ObservableCollection<TreeViewNode>();
        var root = new TreeViewNode("root", tag: true);
        roots.Add(root);
        var view = CreateView(roots);
        var tree = new TreeView { ItemsSource = view };

        tree.Expanding += _ => roots.Clear();

        tree.Expand(root);

        Assert.AreEqual(0, view.Count);
    }

    private static TreeItemsView<TreeViewNode> CreateView(IReadOnlyList<TreeViewNode> roots)
        => TreeItemsView.Create(
            roots,
            node => node.Children,
            textSelector: node => node.Text,
            keySelector: node => node,
            isExpandableSelector: node => node.Tag is true);
}
