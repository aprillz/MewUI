using Aprillz.MewUI.MewDock;
using Aprillz.MewUI.MewDock.Extended;
using Aprillz.MewUI.MewDock.Model;

using static MewUI.MewDock.Test.DockTestSupport;

namespace MewUI.MewDock.Test;

/// <summary>The model reports each change as it happens (children, values, layouts) and each action once it is done.</summary>
[TestClass]
public sealed class ModelNotificationTests
{
    private const string TWO_GROUPS = """
        {
          "layout": { "type": "row", "children": [
            { "type": "tabset", "id": "left", "children": [
              { "type": "tab", "id": "a", "name": "A", "component": "document" },
              { "type": "tab", "id": "b", "name": "B", "component": "document" }
            ] },
            { "type": "tabset", "id": "right", "children": [
              { "type": "tab", "id": "c", "name": "C", "component": "document" }
            ] }
          ]}
        }
        """;

    [TestMethod]
    public void MovingATabRemovesItFromOneGroupBeforeInsertingItInTheOther()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        var left = (TabSetNode)model.GetNodeById("left")!;
        var right = (TabSetNode)model.GetNodeById("right")!;
        var events = new List<string>();
        left.ChildRemoved += (child, index) => events.Add($"left-{child.GetId()}@{index}");
        right.ChildInserted += (child, index) => events.Add($"right+{child.GetId()}@{index}");

        model.DoAction(DockAction.MoveNode("b", "right", DockLocation.Center, -1));

        CollectionAssert.AreEqual(new[] { "left-b@1", "right+b@1" }, events);
    }

    [TestMethod]
    public void RenameReportsOnlyTheName()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        var tab = model.GetNodeById("a")!;
        var left = model.GetNodeById("left")!;
        var properties = new List<NodeProperty>();
        int childChanges = 0;
        tab.PropertyChanged += (_, property) => properties.Add(property);
        left.ChildInserted += (_, _) => childChanges++;
        left.ChildRemoved += (_, _) => childChanges++;

        model.DoAction(DockAction.RenameTab("a", "Renamed"));

        CollectionAssert.AreEqual(new[] { NodeProperty.Name }, properties);
        Assert.AreEqual(0, childChanges);
    }

    [TestMethod]
    public void SelectingATabReportsTheGroupsSelection()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        var left = model.GetNodeById("left")!;
        var properties = new List<NodeProperty>();
        left.PropertyChanged += (_, property) => properties.Add(property);

        model.DoAction(DockAction.SelectTab("b"));

        CollectionAssert.Contains(properties, NodeProperty.Selected);
    }

    [TestMethod]
    public void AdjustingWeightsReportsEachChangedWeight()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        var left = model.GetNodeById("left")!;
        var right = model.GetNodeById("right")!;
        int weightChanges = 0;
        left.PropertyChanged += (_, property) => weightChanges += property == NodeProperty.Weight ? 1 : 0;
        right.PropertyChanged += (_, property) => weightChanges += property == NodeProperty.Weight ? 1 : 0;

        model.DoAction(DockAction.AdjustWeights(model.GetRootRow().GetId(), new[] { 30.0, 70.0 }));

        Assert.AreEqual(2, weightChanges);
    }

    [TestMethod]
    public void MaximizeAndFocusAreReported()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        int maximized = 0;
        int focused = 0;
        model.MainLayout.MaximizedChanged += _ => maximized++;
        model.FocusedChanged += () => focused++;

        model.DoAction(DockAction.MaximizeToggle("right"));
        model.DoAction(DockAction.SetActiveTabset("left", DockModelIds.MainLayoutId));

        Assert.AreEqual(1, maximized);
        Assert.IsGreaterThanOrEqualTo(1, focused);
    }

    [TestMethod]
    public void BorderSizeIsReported()
    {
        var model = ExtendedDockModel.FromJson(AutoHiddenTools(Tool("Explorer")));
        var border = model.BorderSet.Borders[0];
        var properties = new List<NodeProperty>();
        border.PropertyChanged += (_, property) => properties.Add(property);

        model.DoAction(DockAction.AdjustBorderSplit(border.GetId(), 321));

        CollectionAssert.Contains(properties, NodeProperty.Size);
    }

    [TestMethod]
    public void PoppingOutAndClosingAWindowAddsAndRemovesItsLayout()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        var added = new List<string>();
        var removed = new List<string>();
        model.LayoutAdded += layout => added.Add(layout.LayoutId);
        model.LayoutRemoved += layout => removed.Add(layout.LayoutId);

        model.DoAction(DockAction.PopoutTab("c"));
        Assert.HasCount(1, added);
        model.DoAction(DockAction.ClosePopout(added[0]));

        CollectionAssert.AreEqual(added, removed);
    }

    [TestMethod]
    public void PinningAToolAddsItsDock()
    {
        var model = ExtendedDockModel.FromJson(AutoHiddenTools(Tool("Explorer")));
        var tab = model.BorderSet.Borders[0].Children[0];
        var added = new List<Layout>();
        model.LayoutAdded += added.Add;

        model.DoAction(DockAction.PinTool(tab.GetId()));

        Assert.HasCount(1, added);
        Assert.IsInstanceOfType<DockLayout>(added[0]);
    }

    [TestMethod]
    public void AnActionDispatchedByAListenerIsReportedAfterTheOneBeingReported()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        var reported = new List<string>();
        int rounds = 0;
        model.AddChangeListener(action =>
        {
            reported.Add(action.GetType().Name);
            if (action is RenameTabAction { Text: "First" })
            {
                model.DoAction(DockAction.RenameTab("b", "Second"));
                reported.Add("dispatched");
            }
        });
        model.AddChangeListener(action => reported.Add("late:" + action.GetType().Name));
        model.ChangesCompleted += () => rounds++;

        model.DoAction(DockAction.RenameTab("a", "First"));

        CollectionAssert.AreEqual(
            new[] { "RenameTabAction", "dispatched", "late:RenameTabAction", "RenameTabAction", "late:RenameTabAction" },
            reported);
        Assert.AreEqual(1, rounds);
    }

    [TestMethod]
    public void DeferredActionsAreReportedTogether()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        int rounds = 0;
        int actions = 0;
        model.AddChangeListener(_ => actions++);
        model.ChangesCompleted += () => rounds++;

        using (model.DeferNotifications())
        {
            model.DoAction(DockAction.RenameTab("a", "One"));
            model.DoAction(DockAction.RenameTab("b", "Two"));
            Assert.AreEqual(0, actions, "nothing is reported inside the scope");
        }

        Assert.AreEqual(2, actions);
        Assert.AreEqual(1, rounds);
    }

    [TestMethod]
    public void AFailingListenerDoesNotStopTheOthers()
    {
        var model = ExtendedDockModel.FromJson(TWO_GROUPS);
        bool secondHeard = false;
        model.AddChangeListener(_ => throw new InvalidOperationException("listener"));
        model.AddChangeListener(_ => secondHeard = true);

        Assert.ThrowsExactly<InvalidOperationException>(() => model.DoAction(DockAction.RenameTab("a", "X")));

        Assert.IsTrue(secondHeard);
    }

    private static class DockModelIds
    {
        public const string MainLayoutId = Aprillz.MewUI.MewDock.Model.Model.MainLayoutId;
    }
}
