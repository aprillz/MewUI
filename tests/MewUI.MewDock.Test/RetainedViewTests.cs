using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using Aprillz.MewUI.MewDock.Controls;
using Aprillz.MewUI.Rendering;

using MewUI.Test.Infrastructure;

using static MewUI.MewDock.Test.DockTestSupport;

namespace MewUI.MewDock.Test;

/// <summary>
/// The dock keeps its controls and the panes' content while the layout changes around them: content keeps its parent
/// and its keyboard focus, is made once per tab, and the views of groups that did not change are the same objects.
/// </summary>
[TestClass]
public sealed class RetainedViewTests
{
    private const string TWO_GROUPS = """
        {
          "borders": [ { "location": "left", "children": [] } ],
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
    public void FocusedContentKeepsItsParentAndFocusWhileTheLayoutChanges()
    {
        var editor = new TextBox();
        var manager = new DockingManager { ContentFactory = pane => pane.Title == "A" ? editor : new TextBlock { Text = pane.Title } };
        manager.LoadLayout(TWO_GROUPS);
        var window = DockWindow.Create(manager);
        Assert.IsTrue(editor.Focus(), "the editor takes the keyboard focus");
        var parent = editor.Parent;
        Assert.IsNotNull(parent);

        void Step(string what, Action change)
        {
            change();
            window.PerformLayout();
            Assert.AreSame(parent, editor.Parent, $"{what}: the editor keeps its parent");
            Assert.AreSame(editor, window.FocusManager.FocusedElement, $"{what}: the editor keeps the keyboard focus");
            AssertContentFollowsGroups(manager);
        }

        DockPane? tool = null;
        Step("adding a tool", () => tool = manager.AddToolPane("Tool", new TextBlock { Text = "tool" }));
        Step("renaming the document", () => Pane(manager, "A").Title = "A renamed");
        Step("closing a document in the other group", () => Pane(manager, "D").Close());
        Step("adding a document to the other group", () => Pane(manager, "C").Group!.AddPane("E", new TextBlock { Text = "E" }));
        Step("splitting the other group", () => Pane(manager, "E").SplitOff(DockEdge.Bottom));
        Step("maximizing the editor's group", () => Pane(manager, "A renamed").Group!.ToggleMaximize());
        Step("restoring the editor's group", () => Pane(manager, "A renamed").Group!.ToggleMaximize());
        Step("unpinning the tool", () => tool!.Unpin());
        Step("pinning the tool", () => tool!.Pin());
    }

    [TestMethod]
    public void ContentIsMadeOncePerTab()
    {
        var made = new Dictionary<string, int>();
        var manager = new DockingManager
        {
            ContentFactory = pane =>
            {
                made[pane.Title!] = made.GetValueOrDefault(pane.Title!) + 1;
                return new TextBlock { Text = pane.Title };
            },
        };
        manager.LoadLayout(TWO_GROUPS);
        var window = DockWindow.Create(manager);

        Pane(manager, "D").Activate();
        window.PerformLayout();
        Pane(manager, "C").Activate();
        window.PerformLayout();
        Pane(manager, "C").MoveInto(Pane(manager, "A").Group!);
        window.PerformLayout();
        Pane(manager, "A").Group!.ToggleMaximize();
        window.PerformLayout();
        Pane(manager, "A").Group!.ToggleMaximize();
        window.PerformLayout();
        Pane(manager, "D").Activate();
        window.PerformLayout();

        foreach (var (title, count) in made)
        {
            Assert.AreEqual(1, count, $"content of {title}");
        }
        CollectionAssert.IsSubsetOf(new[] { "A", "C", "D" }, made.Keys.ToArray());
    }

    [TestMethod]
    public void UnchangedGroupsKeepTheirViews()
    {
        var headers = new Dictionary<string, int>();
        var manager = new DockingManager
        {
            ContentFactory = pane => new TextBlock { Text = pane.Title },
            HeaderFactory = pane =>
            {
                headers[pane.Title!] = headers.GetValueOrDefault(pane.Title!) + 1;
                return new TextBlock { Text = pane.Title };
            },
        };
        manager.LoadLayout(TWO_GROUPS);
        var window = DockWindow.Create(manager);
        var left = TabSetView(manager, "left");
        var right = TabSetView(manager, "right");
        var leftButton = Descendants(left).OfType<FlexTabButton>().Single();

        Pane(manager, "C").Title = "C renamed";
        manager.AddToolPane("Tool", new TextBlock { Text = "tool" });
        Pane(manager, "D").Close();
        window.PerformLayout();

        Assert.AreSame(left, TabSetView(manager, "left"), "the untouched group keeps its view");
        Assert.AreSame(right, TabSetView(manager, "right"), "the group that changed keeps its view");
        Assert.AreSame(leftButton, Descendants(left).OfType<FlexTabButton>().Single(), "the untouched tab keeps its button");

        // The host header was made once, when the tab was "C"; this factory does not follow renames.
        var header = HeaderOf(manager, "C");
        Pane(manager, "C renamed").MoveInto(Pane(manager, "A").Group!);
        window.PerformLayout();

        Assert.AreSame(left, TabSetView(manager, "left"), "the group the tab moved into keeps its view");
        Assert.AreSame(header, HeaderOf(manager, "C"), "the moved tab keeps its header");
        foreach (var (title, count) in headers)
        {
            Assert.AreEqual(1, count, $"header of {title}");
        }
    }

    [TestMethod]
    public void ReloadingLetsGoOfTheOldLayout()
    {
        var manager = new DockingManager { ContentFactory = pane => new TextBlock { Text = pane.Title } };
        manager.LoadLayout(TWO_GROUPS);
        DockWindow.Create(manager);
        var previous = manager.Model!;

        manager.LoadLayout(TWO_GROUPS);

        Assert.AreEqual(0, SubscriberCount(previous, "ChangesCompleted"), "nothing listens to the replaced model");
        Assert.AreEqual(0, SubscriberCount(previous, "FocusedChanged"), "no view of the old layout follows its focus");
    }

    [TestMethod]
    public void ShownContentIsDrawn()
    {
        var drawn = new CountingContent();
        var hidden = new CountingContent();
        var manager = new DockingManager
        {
            ContentFactory = pane => pane.Title switch { "A" => drawn, "D" => hidden, _ => new TextBlock { Text = pane.Title ?? string.Empty } },
        };
        manager.LoadLayout(TWO_GROUPS);
        DockWindow.Create(manager);

        manager.Render(new SinkContext());

        Assert.AreEqual(1, drawn.RenderCount, "the selected tab's content is drawn once");
        Assert.AreEqual(0, hidden.RenderCount, "a tab that is not selected is not drawn");
    }

    private sealed class SinkContext : NoOpGraphicsContext
    {
    }

    private sealed class CountingContent : FrameworkElement
    {
        public int RenderCount { get; private set; }

        protected override Size MeasureContent(Size availableSize) => new(10, 10);

        protected override void OnRender(IGraphicsContext context) => RenderCount++;
    }

    private static FlexTabSetView TabSetView(DockingManager manager, string id) =>
        Descendants(manager).OfType<FlexTabSetView>().Single(view => view.Node.GetId() == id);

    private static UIElement HeaderOf(DockingManager manager, string title) =>
        Descendants(manager).OfType<FlexTabButton>()
            .SelectMany(button => Descendants(button).OfType<TextBlock>())
            .First(text => text.Text == title);

    // Every shown content sits exactly where its group put its content area.
    private static void AssertContentFollowsGroups(DockingManager manager)
    {
        foreach (var host in Descendants(manager).OfType<PaneHost>())
        {
            if (host.Tab.Parent?.View is IPaneContentOwner owner)
            {
                Assert.AreEqual(owner.ContentArea.X, host.Bounds.X, 0.5, $"{host.Tab.Name}: x");
                Assert.AreEqual(owner.ContentArea.Y, host.Bounds.Y, 0.5, $"{host.Tab.Name}: y");
                Assert.AreEqual(owner.ContentArea.Width, host.Bounds.Width, 0.5, $"{host.Tab.Name}: width");
                Assert.AreEqual(owner.ContentArea.Height, host.Bounds.Height, 0.5, $"{host.Tab.Name}: height");
            }
        }
    }

    private static int SubscriberCount(object model, string eventName)
    {
        var field = model.GetType().BaseType!.GetField(eventName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? model.GetType().GetField(eventName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (field?.GetValue(model) as Delegate)?.GetInvocationList().Length ?? 0;
    }
}
