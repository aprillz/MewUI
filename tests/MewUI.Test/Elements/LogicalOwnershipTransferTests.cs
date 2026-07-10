using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Elements;

/// <summary>
/// Ownership transfer protocol: when a container adopts an owned element, the previous owner
/// clears its record (slot property or children list), so no stale entry keeps a moved child
/// measured or visited by two parents.
/// </summary>
[TestClass]
public sealed class LogicalOwnershipTransferTests
{
    [TestMethod]
    public void PanelToPanel_MoveClearsOldPanelList()
    {
        var panelA = new StackPanel();
        var panelB = new StackPanel();
        var child = new TextBlock();
        panelA.Add(child);

        panelB.Add(child);

        Assert.AreEqual(0, panelA.Count, "the old panel drops the moved child from its list");
        Assert.AreEqual(1, panelB.Count);
        Assert.AreSame(panelB, child.LogicalParent);
        Assert.AreSame(panelB, child.Parent);
    }

    [TestMethod]
    public void BorderToBorder_MoveClearsOldSlot()
    {
        var borderA = new Border();
        var borderB = new Border();
        var child = new TextBlock();
        borderA.Child = child;

        borderB.Child = child;

        Assert.IsNull(borderA.Child, "the old border clears its slot record");
        Assert.AreSame(child, borderB.Child);
        Assert.AreSame(borderB, child.LogicalParent);
        Assert.AreSame(borderB, child.Parent);
    }

    [TestMethod]
    public void SlotToPanel_TransferClearsSlotRecord()
    {
        var host = new ContentControl();
        var panel = new StackPanel();
        var child = new TextBlock();
        host.Content = child;

        panel.Add(child);

        Assert.IsNull(host.Content, "the content slot clears when a container adopts its child");
        Assert.AreSame(panel, child.LogicalParent);
    }

    [TestMethod]
    public void SlotDestination_StaysStrict()
    {
        var panel = new StackPanel();
        var host = new ContentControl();
        var child = new TextBlock();
        panel.Add(child);

        Assert.ThrowsExactly<InvalidOperationException>(() => host.Content = child,
            "semantic slots keep requiring an explicit release");

        Assert.AreEqual(1, panel.Count, "the rejected assignment must not disturb the current owner");
        Assert.AreSame(panel, child.LogicalParent);
    }

    [TestMethod]
    public void PanelRemove_ReleasesOwnership()
    {
        var panel = new StackPanel();
        var child = new TextBlock();
        panel.Add(child);

        panel.Remove(child);

        Assert.IsNull(child.LogicalParent);
        Assert.IsNull(child.Parent);
        Assert.AreEqual(0, panel.Count);
    }

    [TestMethod]
    public void PanelAdd_SelfAndCycle_StillRejected()
    {
        var outer = new StackPanel();
        var inner = new StackPanel();
        outer.Add(inner);

        Assert.ThrowsExactly<InvalidOperationException>(() => inner.Add(outer), "cycles are never transferable");
        Assert.ThrowsExactly<InvalidOperationException>(() => outer.Add(outer), "self-adoption is never valid");
    }
}
