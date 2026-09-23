using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using Aprillz.MewUI.MewDock.Model;

using static MewUI.MewDock.Test.DockTestSupport;

namespace MewUI.MewDock.Test;

/// <summary>DockPane / DockGroup state as MewProperty: model sync, bindings, and handle lifetime.</summary>
[TestClass]
public sealed class HandlePropertyTests
{
    [TestMethod]
    public void TitleSetterRenamesTheTab()
    {
        var manager = Load(DockedTools(Tool("Explorer")));
        var pane = Pane(manager, "Explorer");

        pane.Title = "Renamed";

        Assert.AreEqual("Renamed", pane.Title);
        SavedTab(manager.SaveLayout(), "Renamed");
    }

    [TestMethod]
    public void TitleFollowsARenameFromTheModel()
    {
        var manager = Load(DockedTools(Tool("Explorer")));
        var pane = Pane(manager, "Explorer");
        var probe = new Probe<string?>();
        probe.SetBinding(Probe<string?>.ValueProperty, pane, DockPane.TitleProperty);

        manager.PerformAction(DockAction.RenameTab(pane.Id, "FromModel"));

        Assert.AreEqual("FromModel", pane.Title);
        Assert.AreEqual("FromModel", probe.Value);
    }

    [TestMethod]
    public void TwoWayTitleBindingWritesTheModel()
    {
        var manager = Load(DockedTools(Tool("Explorer")));
        var pane = Pane(manager, "Explorer");
        var source = new ObservableValue<string?>("Explorer");
        pane.SetBinding(DockPane.TitleProperty, source, BindingMode.TwoWay);

        source.Value = "Bound";
        Assert.AreEqual("Bound", pane.Title);
        SavedTab(manager.SaveLayout(), "Bound");

        manager.PerformAction(DockAction.RenameTab(pane.Id, "FromModel"));
        Assert.AreEqual("FromModel", source.Value, "a model rename flows back to the source");
    }

    [TestMethod]
    public void IsActiveFollowsActivation()
    {
        var manager = Load(DockedTools(Tool("Explorer"), Tool("Output")));
        var explorer = Pane(manager, "Explorer");
        var output = Pane(manager, "Output");
        var probe = new Probe<bool>();
        probe.SetBinding(Probe<bool>.ValueProperty, explorer, DockPane.IsActiveProperty);

        explorer.Activate();
        Assert.IsTrue(explorer.IsActive);
        Assert.IsTrue(probe.Value);

        output.Activate();
        Assert.IsFalse(explorer.IsActive);
        Assert.IsTrue(output.IsActive);
        Assert.IsFalse(probe.Value);
    }

    [TestMethod]
    public void ActivePaneChangedSeesUpdatedHandles()
    {
        var manager = Load(DockedTools(Tool("Explorer"), Tool("Output")));
        var output = Pane(manager, "Output");
        bool? activeInHandler = null;
        manager.ActivePaneChanged += (_, pane) => activeInHandler = pane?.IsActive;

        output.Activate();

        Assert.IsTrue(activeInHandler);
    }

    [TestMethod]
    public void GroupAndEdgeFollowPinAndUnpin()
    {
        var manager = Load(DockedTools(Tool("Explorer")));
        var pane = Pane(manager, "Explorer");
        var groupProbe = new Probe<DockGroup?>();
        groupProbe.SetBinding(Probe<DockGroup?>.ValueProperty, pane, DockPane.GroupProperty);
        Assert.IsNotNull(pane.Group);
        Assert.AreEqual(DockEdge.Left, pane.Edge);

        pane.Unpin();
        Assert.IsNull(pane.Group);
        Assert.IsNull(groupProbe.Value);
        Assert.AreEqual(DockEdge.Left, pane.Edge, "an auto-hidden tool reports its border edge");

        pane.Pin();
        Assert.IsNotNull(pane.Group);
        Assert.AreSame(pane.Group, groupProbe.Value);
        Assert.AreEqual(DockEdge.Left, pane.Group!.Edge);
    }

    [TestMethod]
    public void GroupActivePaneFollowsSelection()
    {
        var manager = Load(DockedTools(Tool("Explorer"), Tool("Output")));
        var explorer = Pane(manager, "Explorer");
        var output = Pane(manager, "Output");
        var group = explorer.Group!;
        var probe = new Probe<DockPane?>();
        probe.SetBinding(Probe<DockPane?>.ValueProperty, group, DockGroup.ActivePaneProperty);

        output.Activate();
        Assert.AreSame(output, group.ActivePane);
        Assert.AreSame(output, probe.Value);

        explorer.Activate();
        Assert.AreSame(explorer, group.ActivePane);
        Assert.AreSame(explorer, probe.Value);
    }

    [TestMethod]
    public void GroupIsMaximizedFollowsToggle()
    {
        var manager = Load(DockedTools(Tool("Explorer")));
        var group = Pane(manager, "Document").Group!;
        var probe = new Probe<bool>();
        probe.SetBinding(Probe<bool>.ValueProperty, group, DockGroup.IsMaximizedProperty);

        group.ToggleMaximize();
        Assert.IsTrue(group.IsMaximized);
        Assert.IsTrue(probe.Value);

        group.ToggleMaximize();
        Assert.IsFalse(group.IsMaximized);
        Assert.IsFalse(probe.Value);
    }

    [TestMethod]
    public void HandlesAreDetachedByLoadLayout()
    {
        string json = DockedTools(Tool("Explorer"));
        var manager = Load(json);
        var stale = Pane(manager, "Explorer");
        var staleGroup = stale.Group!;

        manager.LoadLayout(json);
        var fresh = Pane(manager, "Explorer");
        Assert.AreNotSame(stale, fresh);
        Assert.AreNotSame(staleGroup, fresh.Group);

        stale.Title = "Stale";
        stale.Close();
        staleGroup.Close();

        Assert.AreEqual("Explorer", fresh.Title, "a detached handle does not touch the new layout");
        Assert.IsTrue(manager.Panes.Contains(fresh));
        Assert.AreEqual("Stale", stale.Title, "a detached handle keeps its own last value");
    }

    [TestMethod]
    public void HandleOfAClosedPaneIsDetached()
    {
        var manager = Load(DockedTools(Tool("Explorer"), Tool("Output")));
        var closed = Pane(manager, "Explorer");
        closed.Close();

        closed.Title = "Ghost";

        Assert.IsFalse(manager.Panes.Any(pane => pane.Title == "Ghost"));
        Assert.HasCount(1, manager.Panes);
    }

    [TestMethod]
    public void ModelSyncDoesNotDispatchActions()
    {
        var manager = Load(DockedTools(Tool("Explorer")));
        var pane = Pane(manager, "Explorer");
        int changes = 0;
        manager.Changed += (_, _) => changes++;

        pane.Title = "Once";

        Assert.AreEqual(1, changes, "the sync after the rename must not rename again");
    }

    /// <summary>A bindable target for observing read-only handle properties.</summary>
    private sealed class Probe<T> : MewObject
    {
        public static readonly MewProperty<T> ValueProperty =
            MewProperty<T>.Register<Probe<T>>(nameof(Value), default!);

        public T Value => GetValue(ValueProperty);
    }
}
