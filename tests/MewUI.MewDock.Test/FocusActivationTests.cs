using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;

using static MewUI.MewDock.Test.DockTestSupport;

namespace MewUI.MewDock.Test;

/// <summary>The active pane and the keyboard focus follow each other, and a pane's focus comes back to where it was.</summary>
[TestClass]
public sealed class FocusActivationTests
{
    private const string TWO_GROUPS = """
        {
          "borders": [ { "location": "left", "children": [] } ],
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
    public void FocusInContentActivatesItsPane()
    {
        var editors = new Dictionary<string, TextBox>();
        var manager = LoadWithEditors(editors);
        var window = DockWindow.Create(manager);

        Assert.IsTrue(editors["C"].Focus());
        window.PerformLayout();

        Assert.AreEqual("C", manager.ActivePane?.Title);
        Assert.IsTrue(Pane(manager, "C").IsActive);
    }

    [TestMethod]
    public void ActivatingAPaneReturnsTheFocusToWhereItWas()
    {
        var second = new TextBox();
        var editors = new Dictionary<string, TextBox>();
        var manager = new DockingManager
        {
            ContentFactory = pane =>
            {
                var editor = new TextBox();
                editors[pane.Title!] = editor;
                return pane.Title == "A" ? new StackPanel().Children(editor, second) : editor;
            },
        };
        manager.LoadLayout(TWO_GROUPS);
        var window = DockWindow.Create(manager);
        Assert.IsTrue(second.Focus());

        Pane(manager, "C").Activate();
        window.PerformLayout();
        Assert.AreSame(editors["C"], window.FocusManager.FocusedElement, "the activated pane's content takes the focus");

        Pane(manager, "A").Activate();
        window.PerformLayout();
        Assert.AreSame(second, window.FocusManager.FocusedElement, "the focus returns to the element that had it");
    }

    [TestMethod]
    public void PinningARevealedToolKeepsItsFocus()
    {
        var toolEditor = new TextBox();
        var manager = LoadWithEditors(new Dictionary<string, TextBox>());
        var window = DockWindow.Create(manager);
        var tool = manager.AddToolPane("Tool", toolEditor);
        tool.Unpin();
        window.PerformLayout();

        tool.Activate();
        window.PerformLayout();
        Assert.AreSame(toolEditor, window.FocusManager.FocusedElement, "activating the auto-hidden tool reveals and focuses it");

        tool.Pin();
        window.PerformLayout();
        Assert.AreSame(toolEditor, window.FocusManager.FocusedElement, "the pinned tool keeps the focus");
    }

    [TestMethod]
    public void ActivatingARevealedToolKeepsItRevealed()
    {
        var manager = LoadWithEditors(new Dictionary<string, TextBox>());
        var window = DockWindow.Create(manager);
        var tool = manager.AddToolPane("Tool", new TextBox());
        tool.Unpin();
        tool.Activate();
        window.PerformLayout();

        tool.Activate();
        window.PerformLayout();

        Assert.IsTrue(Descendants(manager).OfType<Aprillz.MewUI.MewDock.Controls.PaneHost>().Any(host => host.Tab.Name == "Tool"),
            "the tool is still revealed");
    }

    private static DockingManager LoadWithEditors(Dictionary<string, TextBox> editors)
    {
        var manager = new DockingManager
        {
            ContentFactory = pane =>
            {
                var editor = new TextBox();
                editors[pane.Title!] = editor;
                return editor;
            },
        };
        manager.LoadLayout(TWO_GROUPS);
        return manager;
    }
}
