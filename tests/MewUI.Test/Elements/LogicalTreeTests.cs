using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Elements;

[TestClass]
public sealed class LogicalTreeTests
{
    private class LogicalHost : ContentControl, ILogicalTreeHost
    {
        private readonly List<Element> _logicalChildren = new();

        public void Attach(Element child)
        {
            AttachLogicalChild(child);
            _logicalChildren.Add(child);
        }

        public void Detach(Element child)
        {
            DetachLogicalChild(child);
            _logicalChildren.Remove(child);
        }

        public bool VisitLogicalChildren(Func<Element, bool> visitor)
        {
            foreach (var child in _logicalChildren)
            {
                if (!visitor(child))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class TrackingElement : LogicalHost
    {
        public int LogicalParentChanges { get; private set; }
        public List<(Element? OldRoot, Element? NewRoot)> RootChanges { get; } = new();

        protected override void OnLogicalParentChanged()
        {
            base.OnLogicalParentChanged();
            LogicalParentChanges++;
        }

        protected override void OnLogicalRootChanged(Element? oldRoot, Element? newRoot)
        {
            base.OnLogicalRootChanged(oldRoot, newRoot);
            RootChanges.Add((oldRoot, newRoot));
        }
    }

    [TestMethod]
    public void AttachLogicalChild_SetsLogicalParent()
    {
        var owner = new LogicalHost();
        var child = new Border();

        owner.Attach(child);

        Assert.AreSame(owner, child.LogicalParent);
    }

    [TestMethod]
    public void DetachLogicalChild_ClearsLogicalParent()
    {
        var owner = new LogicalHost();
        var child = new Border();
        owner.Attach(child);

        owner.Detach(child);

        Assert.IsNull(child.LogicalParent);
    }

    [TestMethod]
    public void AttachLogicalChild_SameOwnerAgain_IsNoOp()
    {
        var owner = new LogicalHost();
        var child = new TrackingElement();
        owner.Attach(child);
        int changesAfterFirstAttach = child.LogicalParentChanges;

        owner.Attach(child);

        Assert.AreSame(owner, child.LogicalParent);
        Assert.AreEqual(changesAfterFirstAttach, child.LogicalParentChanges, "re-attach must not re-notify");
    }

    [TestMethod]
    public void AttachLogicalChild_Self_Throws()
    {
        var owner = new LogicalHost();

        Assert.ThrowsExactly<InvalidOperationException>(() => owner.Attach(owner));
    }

    [TestMethod]
    public void AttachLogicalChild_ForeignOwner_Throws()
    {
        var ownerA = new LogicalHost();
        var ownerB = new LogicalHost();
        var child = new Border();
        ownerA.Attach(child);

        Assert.ThrowsExactly<InvalidOperationException>(() => ownerB.Attach(child));
        Assert.AreSame(ownerA, child.LogicalParent, "failed attach must not steal ownership");
    }

    [TestMethod]
    public void AttachLogicalChild_AncestorCycle_Throws()
    {
        var grandParent = new LogicalHost();
        var parent = new LogicalHost();
        var child = new LogicalHost();
        grandParent.Attach(parent);
        parent.Attach(child);

        Assert.ThrowsExactly<InvalidOperationException>(() => child.Attach(grandParent));
        Assert.IsNull(grandParent.LogicalParent, "failed cycle attach must leave the ancestor rootless");
    }

    [TestMethod]
    public void DetachLogicalChild_NotOwner_DoesNotClear()
    {
        var owner = new LogicalHost();
        var other = new LogicalHost();
        var child = new Border();
        owner.Attach(child);

        other.Detach(child);

        Assert.AreSame(owner, child.LogicalParent);
    }

    [TestMethod]
    public void AttachLogicalChild_DoesNotTouchVisualParent()
    {
        // ShadowDecorator.Child is a visual-only relationship (not a logical host).
        var visualParent = new ShadowDecorator();
        var owner = new LogicalHost();
        var child = new TextBlock();
        visualParent.Child = child;

        owner.Attach(child);

        Assert.AreSame(visualParent, child.Parent, "logical attach must not change the visual parent");
        Assert.AreSame(owner, child.LogicalParent);
    }

    [TestMethod]
    public void FindLogicalRoot_WalksChain()
    {
        var root = new LogicalHost();
        var middle = new LogicalHost();
        var leaf = new Border();
        root.Attach(middle);
        middle.Attach(leaf);

        Assert.AreSame(root, leaf.FindLogicalRoot());
        Assert.AreSame(root, root.FindLogicalRoot(), "an element without a logical owner is its own root");
    }

    [TestMethod]
    public void AttachLogicalChild_NotifiesRootChangeAcrossSubtree()
    {
        var newRoot = new LogicalHost();
        var parent = new TrackingElement();
        var descendant = new TrackingElement();
        parent.Attach(descendant);
        parent.RootChanges.Clear();
        descendant.RootChanges.Clear();

        newRoot.Attach(parent);

        Assert.AreEqual(1, parent.RootChanges.Count);
        Assert.AreSame(parent, parent.RootChanges[0].OldRoot);
        Assert.AreSame(newRoot, parent.RootChanges[0].NewRoot);
        Assert.AreEqual(1, descendant.RootChanges.Count, "logical descendants must also observe the root change");
        Assert.AreSame(newRoot, descendant.RootChanges[0].NewRoot);
    }

    [TestMethod]
    public void LogicalTreeVisit_FollowsOnlyLogicalHosts()
    {
        var owner = new LogicalHost();
        var logicalChild = new Border();
        var visualChild = new Border();
        owner.Attach(logicalChild);
        owner.Content = visualChild;

        var visited = new List<Element>();
        LogicalTree.Visit(owner, visited.Add);

        CollectionAssert.Contains(visited, logicalChild);
        CollectionAssert.DoesNotContain(visited, visualChild, "visual-only children must not appear in logical traversal");
    }

    [TestMethod]
    public void LogicalTreeIsInSubtreeOf_UsesLogicalChain()
    {
        var root = new LogicalHost();
        var child = new LogicalHost();
        var visualHost = new ShadowDecorator();
        var visualOnly = new TextBlock();
        root.Attach(child);
        child.Content = visualHost;
        visualHost.Child = visualOnly;

        Assert.IsTrue(LogicalTree.IsInSubtreeOf(child, root));
        Assert.IsFalse(LogicalTree.IsInSubtreeOf(visualOnly, root), "visual containment must not count as logical containment");
    }

    [TestMethod]
    public void FindLogicalChild_FindsByType()
    {
        var root = new LogicalHost();
        var middle = new LogicalHost();
        var leaf = new TextBlock();
        root.Attach(middle);
        middle.Attach(leaf);
        var visualOnly = new TextBlock();
        root.Content = visualOnly;

        Assert.AreSame(leaf, root.FindLogicalChild<TextBlock>());
    }
}
