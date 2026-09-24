using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using Aprillz.MewUI.MewDock.Extended;
using Aprillz.MewUI.MewDock.Model;

using static MewUI.MewDock.Test.DockTestSupport;

namespace MewUI.MewDock.Test;

/// <summary>After every action the model's references point into the tree, and moves keep documents and tools apart.</summary>
[TestClass]
public sealed class ModelInvariantTests
{
    private const string TWO_GROUPS = """
        {
          "layout": { "type": "row", "children": [
            { "type": "tabset", "id": "left", "children": [
              { "type": "tab", "id": "a", "name": "A", "component": "document" }
            ] },
            { "type": "tabset", "id": "right", "children": [
              { "type": "tab", "id": "c", "name": "C", "component": "document" }
            ] }
          ]}
        }
        """;

    [TestMethod]
    public void ADocumentAddedAfterTheFocusedGroupClosedIsInTheLayout()
    {
        var manager = new DockingManager { ContentFactory = pane => new TextBlock { Text = pane.Title } };
        manager.LoadLayout(TWO_GROUPS);
        Pane(manager, "C").Activate();

        Pane(manager, "C").Close();
        var added = manager.AddDocumentPane("New", new TextBlock { Text = "new" });

        Assert.IsNotNull(added.Group, "the new document is in a group");
        Assert.Contains(added, manager.DocumentPanes);
    }

    [TestMethod]
    public void ClosingTheLastTabOfAFocusedPopoutLeavesNoFocusBehind()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        model.DoAction(DockAction.PopoutTab("c"));
        var popoutGroup = (TabSetNode)model.GetNodeById("c")!.Parent!;
        model.DoAction(DockAction.SetActiveTabset(popoutGroup.GetId(), popoutGroup.LayoutId));

        model.DoAction(DockAction.DeleteTab("c"));

        Assert.IsTrue(model.FocusedTabSet is null || ReferenceEquals(model.GetNodeById(model.FocusedTabSet.GetId()), model.FocusedTabSet),
            "the focused group is one still in the layout");
    }

    [TestMethod]
    public void PinningTheRevealedToolLeavesTheBorderClosed()
    {
        var model = ExtendedDockModel.FromJson(AutoHiddenTools(Tool("A"), Tool("B"), Tool("C")));
        var border = model.BorderSet.Borders[0];
        var middle = border.Children[1];
        model.DoAction(DockAction.SelectTab(middle.GetId()));
        Assert.AreEqual(1, border.Selected);

        model.DoAction(DockAction.PinTool(middle.GetId()));

        Assert.AreEqual(-1, border.Selected, "no neighbour is revealed in the pinned tool's place");
    }

    [TestMethod]
    public void ClosingAFloatingToolGroupDocksItAsATool()
    {
        var model = ExtendedDockModel.FromJson(DockedTools(Tool("Explorer")));
        TabNode explorer = null!;
        model.VisitNodes((node, level) =>
        {
            if (node is TabNode tab && tab.Name == "Explorer")
            {
                explorer = tab;
            }
        });
        model.DoAction(DockAction.PopoutTabset(explorer.Parent!.GetId()));
        string popoutId = explorer.LayoutId;
        Assert.AreNotEqual(Aprillz.MewUI.MewDock.Model.Model.MainLayoutId, popoutId);

        model.DoAction(DockAction.ClosePopout(popoutId));

        Assert.IsInstanceOfType<DockLayout>(explorer.GetLayout(), "the tool is docked at an edge, not in the document area");
    }

    [TestMethod]
    public void UnpinningAtAnEdgeWithoutABorderMakesOne()
    {
        var model = ExtendedDockModel.FromJson(DockedTools(Tool("Explorer")));
        TabNode explorer = null!;
        model.VisitNodes((node, level) =>
        {
            if (node is TabNode tab && tab.Name == "Explorer")
            {
                explorer = tab;
            }
        });
        model.DoAction(DockAction.EdgeDockTool(explorer.GetId(), DockLocation.Bottom, outer: false));

        model.DoAction(DockAction.UnpinTool(explorer.Parent!.GetId()));

        Assert.IsInstanceOfType<BorderNode>(explorer.Parent, "the tool is auto-hidden");
        Assert.AreEqual(DockLocation.Bottom, ((BorderNode)explorer.Parent!).Location);
    }

    [TestMethod]
    public void AddingAToolIsOneChange()
    {
        var manager = new DockingManager { ContentFactory = pane => new TextBlock { Text = pane.Title } };
        manager.LoadLayout(TWO_GROUPS);
        int changes = 0;
        manager.Changed += (_, _) => changes++;

        manager.AddToolPane("Tool", new TextBlock { Text = "tool" });

        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void SavedLayoutReadsBackTheSame()
    {
        var manager = new DockingManager { ContentFactory = pane => new TextBlock { Text = pane.Title } };
        manager.LoadLayout(TWO_GROUPS);
        var tool = manager.AddToolPane("Tool", new TextBlock { Text = "tool" }, DockEdge.Bottom);
        manager.AddToolPane("Other", new TextBlock { Text = "other" }, DockEdge.Right).Unpin();
        manager.PerformAction(DockAction.AdjustDockSize(tool.Node.GetLayout().LayoutId, 333));
        string saved = manager.SaveLayout();

        manager.LoadLayout(saved);

        Assert.AreEqual(saved, manager.SaveLayout());
    }

    [TestMethod]
    public void ActionsThatChangeNothingAreNotReported()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        int reported = 0;
        model.AddChangeListener(_ => reported++);

        model.DoAction(DockAction.MovePopoutToFront("none"));

        Assert.AreEqual(0, reported);
    }

    [TestMethod]
    public void AddingToAMissingTargetFails()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            model.DoAction(DockAction.AddTab(new Aprillz.MewUI.MewDock.Model.Json.JsonTabNode { Name = "X" }, "missing", DockLocation.Center, -1)));
    }
}
