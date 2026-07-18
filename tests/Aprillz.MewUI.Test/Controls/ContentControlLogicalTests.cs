using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ContentControlLogicalTests
{
    [TestMethod]
    public void SetContent_AttachesLogicallyAndVisually()
    {
        var host = new ContentControl();
        var child = new Border();

        host.Content = child;

        Assert.AreSame(host, child.LogicalParent);
        Assert.AreSame(host, child.Parent);
    }

    [TestMethod]
    public void ClearContent_ReleasesBothRelationships()
    {
        var host = new ContentControl();
        var child = new Border();
        host.Content = child;

        host.Content = null;

        Assert.IsNull(child.LogicalParent);
        Assert.IsNull(child.Parent);
    }

    [TestMethod]
    public void ReplaceContent_ReleasesOldAndAttachesNew()
    {
        var host = new ContentControl();
        var first = new Border();
        var second = new Border();
        host.Content = first;

        host.Content = second;

        Assert.IsNull(first.LogicalParent);
        Assert.IsNull(first.Parent);
        Assert.AreSame(host, second.LogicalParent);
        Assert.AreSame(host, second.Parent);
    }

    [TestMethod]
    public void SetContent_Self_ThrowsBeforeCommit()
    {
        var host = new ContentControl();
        var original = new Border();
        host.Content = original;

        Assert.ThrowsExactly<InvalidOperationException>(() => host.Content = host);

        Assert.AreSame(original, host.Content, "rejected set must not change the property value");
        Assert.AreSame(host, original.LogicalParent, "rejected set must not disturb the current child");
    }

    [TestMethod]
    public void SetContent_OwnedElsewhere_ThrowsAndLeavesBothSidesIntact()
    {
        var ownerA = new ContentControl();
        var ownerB = new ContentControl();
        var child = new Border();
        ownerA.Content = child;

        Assert.ThrowsExactly<InvalidOperationException>(() => ownerB.Content = child);

        Assert.IsNull(ownerB.Content, "failed set must not commit the value");
        Assert.AreSame(child, ownerA.Content);
        Assert.AreSame(ownerA, child.LogicalParent);
        Assert.AreSame(ownerA, child.Parent);
    }

    [TestMethod]
    public void MoveContent_WithExplicitRelease_Works()
    {
        var ownerA = new ContentControl();
        var ownerB = new ContentControl();
        var child = new Border();
        ownerA.Content = child;

        ownerA.Content = null;
        ownerB.Content = child;

        Assert.AreSame(ownerB, child.LogicalParent);
        Assert.AreSame(ownerB, child.Parent);
    }

    [TestMethod]
    public void SetContent_OwnedElsewhere_RejectsOnBindingPathToo()
    {
        var ownerA = new ContentControl();
        var ownerB = new ContentControl();
        var child = new Border();
        ownerA.Content = child;

        // Bindings and forwards bypass the CLR setter and write through SetLocal.
        Assert.ThrowsExactly<InvalidOperationException>(
            () => ownerB.PropertyStore.SetLocal(ContentControl.ContentProperty, child));

        Assert.IsNull(ownerB.Content);
        Assert.AreSame(ownerA, child.LogicalParent);
    }

    [TestMethod]
    public void SetContent_LogicalAncestor_ThrowsOnCycle()
    {
        var outer = new ContentControl();
        var inner = new ContentControl();
        outer.Content = inner;

        Assert.ThrowsExactly<InvalidOperationException>(() => inner.Content = outer);

        Assert.IsNull(inner.Content);
        Assert.AreSame(outer, inner.LogicalParent);
    }

    [TestMethod]
    public void LogicalTraversal_ShowsContentExactlyOnce()
    {
        var host = new ContentControl();
        var child = new Border();
        host.Content = child;

        var visited = new List<Element>();
        LogicalTree.Visit(host, visited.Add);

        Assert.AreEqual(1, visited.Count(element => ReferenceEquals(element, child)));
    }

    [TestMethod]
    public void FindLogicalRoot_FollowsNestedContent()
    {
        var outer = new ContentControl();
        var middle = new ContentControl();
        var leaf = new Border();
        outer.Content = middle;
        middle.Content = leaf;

        Assert.AreSame(outer, leaf.FindLogicalRoot());
    }
}
