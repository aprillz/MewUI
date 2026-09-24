using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;

using static MewUI.MewDock.Test.DockTestSupport;

namespace MewUI.MewDock.Test;

/// <summary>The host is asked before the user closes a pane from the dock, and can keep it where it is.</summary>
[TestClass]
public sealed class PaneClosingTests
{
    private const string TWO_GROUPS = """
        {
          "layout": { "type": "row", "children": [
            { "type": "tabset", "id": "left", "children": [
              { "type": "tab", "id": "a", "name": "A", "component": "document" }
            ] },
            { "type": "tabset", "id": "right", "children": [
              { "type": "tab", "id": "c", "name": "C", "component": "document" },
              { "type": "tab", "id": "d", "name": "D", "component": "document" }
            ] }
          ]}
        }
        """;

    [TestMethod]
    public void AKeptPaneStaysWhereItWas()
    {
        var manager = Load();
        var pane = Pane(manager, "C");
        var group = pane.Group!;
        int groups = manager.Groups.Count;
        var asked = new List<string>();
        manager.PaneClosing += (_, e) =>
        {
            asked.Add(e.Pane.Title!);
            e.Cancel = true;
        };

        manager.RequestClose(pane.Node);

        CollectionAssert.AreEqual(new[] { "C" }, asked);
        Assert.AreSame(pane, Pane(manager, "C"), "the same pane is still there");
        Assert.AreSame(group, pane.Group, "in the same group");
        Assert.AreEqual(0, group.Panes.ToList().IndexOf(pane), "at the same place in it");
        Assert.AreEqual(groups, manager.Groups.Count, "no group was added or removed");
    }

    [TestMethod]
    public void APaneNotKeptCloses()
    {
        var manager = Load();
        manager.PaneClosing += (_, _) => { };

        manager.RequestClose(Pane(manager, "C").Node);

        Assert.IsFalse(manager.DocumentPanes.Any(pane => pane.Title == "C"));
    }

    [TestMethod]
    public void ClosingFromCodeDoesNotAsk()
    {
        var manager = Load();
        int asked = 0;
        manager.PaneClosing += (_, _) => asked++;

        Pane(manager, "C").Close();

        Assert.AreEqual(0, asked);
        Assert.IsFalse(manager.DocumentPanes.Any(pane => pane.Title == "C"));
    }

    [TestMethod]
    public void APaneThatCannotCloseIsNotAskedAbout()
    {
        var manager = new DockingManager { ContentFactory = pane => new TextBlock { Text = pane.Title } };
        manager.LoadLayout(DockedTools(Tool("Locked", "\"enableClose\": false")));
        int asked = 0;
        manager.PaneClosing += (_, _) => asked++;

        manager.RequestClose(Pane(manager, "Locked").Node);

        Assert.AreEqual(0, asked);
        Assert.IsTrue(manager.Panes.Any(pane => pane.Title == "Locked"));
    }

    private static DockingManager Load()
    {
        var manager = new DockingManager { ContentFactory = pane => new TextBlock { Text = pane.Title } };
        manager.LoadLayout(TWO_GROUPS);
        return manager;
    }
}
