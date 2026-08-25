using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TabControlLogicalTests
{
    private static TabItem Tab(string header, Element content)
        => new() { Header = new TextBlock { Text = header }, Content = content };

    [TestMethod]
    public void AllTabContents_AreLogicalChildren_RegardlessOfSelection()
    {
        var tabs = new TabControl();
        var first = new Border();
        var second = new Border();
        tabs.AddTabs(Tab("a", first), Tab("b", second));

        Assert.AreSame(tabs, first.LogicalParent);
        Assert.AreSame(tabs, second.LogicalParent, "the unselected tab's content stays logically owned");
    }

    [TestMethod]
    public void UnselectedContent_IsLogicalOnly()
    {
        var tabs = new TabControl();
        var first = new Border();
        var second = new Border();
        tabs.AddTabs(Tab("a", first), Tab("b", second));
        tabs.SelectedIndex = 0;

        Assert.IsNull(second.Parent, "offscreen content has no visual position");
        Assert.AreSame(tabs, second.LogicalParent, "but keeps its logical owner (a logical child may live outside the visual tree)");
    }

    [TestMethod]
    public void SwitchingTabs_KeepsOwnershipStable()
    {
        var tabs = new TabControl();
        var first = new Border();
        var second = new Border();
        tabs.AddTabs(Tab("a", first), Tab("b", second));

        tabs.SelectedIndex = 0;
        tabs.SelectedIndex = 1;
        tabs.SelectedIndex = 0;

        Assert.AreSame(tabs, first.LogicalParent);
        Assert.AreSame(tabs, second.LogicalParent);
    }

    [TestMethod]
    public void RemoveTab_ReleasesContent()
    {
        var tabs = new TabControl();
        var first = new Border();
        var second = new Border();
        tabs.AddTabs(Tab("a", first), Tab("b", second));

        tabs.RemoveTabAt(0);

        Assert.IsNull(first.LogicalParent);
        Assert.AreSame(tabs, second.LogicalParent);
    }

    [TestMethod]
    public void ContentChange_ReattachesOwnership()
    {
        var tabs = new TabControl();
        var original = new Border();
        var tab = Tab("a", original);
        tabs.AddTabs(tab);
        var replacement = new Border();

        tab.Content = replacement;

        Assert.IsNull(original.LogicalParent, "the replaced content is released");
        Assert.AreSame(tabs, replacement.LogicalParent);
    }

    [TestMethod]
    public void OwnedElement_AsTabContent_ThrowsBeforeCommit()
    {
        var owner = new ContentControl();
        var element = new Border();
        owner.Content = element;

        var tabs = new TabControl();
        var tab = Tab("a", new Border());
        tabs.AddTabs(tab);

        Assert.ThrowsExactly<InvalidOperationException>(() => tab.Content = element);

        Assert.AreSame(owner, element.LogicalParent, "the rejected set must not disturb the current owner");
        Assert.IsNotNull(tab.Content, "the tab keeps its previous content");
    }

    [TestMethod]
    public void LogicalTraversal_VisitsContentsInTabOrder()
    {
        var tabs = new TabControl();
        var first = new Border();
        var second = new Border();
        tabs.AddTabs(Tab("a", first), Tab("b", second));

        var visited = new List<Element>();
        ((ILogicalTreeHost)tabs).VisitLogicalChildren(child =>
        {
            visited.Add(child);
            return true;
        });

        CollectionAssert.AreEqual(new List<Element> { first, second }, visited);
    }

    [TestMethod]
    public void PanelAdoption_ClearsTabRecord()
    {
        var tabs = new TabControl();
        var content = new Border();
        var tab = Tab("a", content);
        tabs.AddTabs(tab);

        var panel = new StackPanel();
        panel.Add(content);

        Assert.AreSame(panel, content.LogicalParent);
        Assert.IsNull(tab.Content, "the tab's record clears when its content is adopted elsewhere");
    }
}
