using Aprillz.MewUI;
using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Automation;

/// <summary>Popups, composite controls, drawn parts, and the change notifications of the automation tree.</summary>
[TestClass]
public sealed class AutomationTreeTests
{
    private static bool SkipWithoutGdi()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    private static AutomationPeer PeerOf(UIElement element)
    {
        var peer = element.GetAutomationPeer();
        Assert.IsNotNull(peer, $"{element.GetType().Name} has no peer");
        return peer;
    }

    private static Window Host(Element content)
    {
        var window = HeadlessWindow.Create();
        window.Content = content;
        window.PerformLayout();
        return window;
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static AutomationPeer Find(AutomationPeer root, AutomationControlType type, string name)
    {
        var found = Descendants(root).FirstOrDefault(peer => peer.GetControlType() == type && peer.GetName() == name);
        Assert.IsNotNull(found, $"no {type} named '{name}'");
        return found;
    }

    /// <summary>Every child names its parent as its parent, through the whole tree.</summary>
    private static void AssertConsistent(AutomationPeer root)
    {
        foreach (var child in root.GetChildren())
        {
            Assert.AreSame(root, child.GetParent(), $"{child.GetControlType()} '{child.GetName()}' names another parent");
            AssertConsistent(child);
        }
    }

    [TestMethod]
    public void DropDownItems_AppearUnderTheWindowWhileOpen()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var combo = new ComboBox { Width = 160 }.Items("Red", "Green");
        var window = Host(new StackPanel().Children(combo));
        var root = PeerOf(window);
        Assert.IsFalse(Descendants(root).Any(peer => peer.GetControlType() == AutomationControlType.ListItem));

        PeerOf(combo).GetProvider<IExpandCollapseProvider>()!.Expand();

        // The first pass places the popup and the second realizes its rows, as in ComboBoxDropDownHeightTests.
        window.PerformLayout();
        window.PerformLayout();

        var item = Find(root, AutomationControlType.ListItem, "Green");
        AssertConsistent(root);

        item.GetProvider<ISelectionItemProvider>()!.Select();
        Assert.AreEqual(1, combo.SelectedIndex);
        Assert.AreEqual("Green", PeerOf(combo).GetProvider<IValueProvider>()!.Value);

        combo.IsDropDownOpen = false;
        window.PerformLayout();
        Assert.IsFalse(Descendants(root).Any(peer => peer.GetControlType() == AutomationControlType.ListItem));
    }

    [TestMethod]
    public void TabControl_ListsTabItems_AndSelectSwitchesTheTab()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var first = new TextBlock { Text = "first page" };
        var second = new TextBlock { Text = "second page" };
        var tabs = new TabControl();
        tabs.AddTabs(
            new TabItem { Header = new TextBlock { Text = "General" }, Content = first },
            new TabItem { Header = new TextBlock { Text = "Advanced" }, Content = second });
        var window = Host(tabs);

        var tabsPeer = PeerOf(tabs);
        Assert.AreEqual(AutomationControlType.Tab, tabsPeer.GetControlType());
        var items = tabsPeer.GetChildren().Where(peer => peer.GetControlType() == AutomationControlType.TabItem).ToArray();
        CollectionAssert.AreEqual(new[] { "General", "Advanced" }, items.Select(peer => peer.GetName()).ToArray());
        Assert.IsTrue(tabsPeer.GetChildren().Contains(PeerOf(first)));

        items[1].GetProvider<ISelectionItemProvider>()!.Select();
        window.PerformLayout();

        Assert.AreEqual(1, tabs.SelectedIndex);
        CollectionAssert.AreEqual(new[] { items[1] }, tabsPeer.GetProvider<ISelectionProvider>()!.GetSelection().ToArray());
        Assert.IsTrue(tabsPeer.GetChildren().Contains(PeerOf(second)));
        AssertConsistent(PeerOf(window));
    }

    [TestMethod]
    public void TreeView_NestsRowsByDepth_AndExpandShowsChildren()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var tree = new TreeView { Height = 300 };
        tree.ItemsSource([
            new TreeViewNode("Fruit", [new TreeViewNode("Apple"), new TreeViewNode("Pear")]),
            new TreeViewNode("Stone"),
        ]);
        var window = Host(tree);

        var treePeer = PeerOf(tree);
        Assert.AreEqual(AutomationControlType.Tree, treePeer.GetControlType());
        CollectionAssert.AreEqual(new[] { "Fruit", "Stone" }, treePeer.GetChildren().Select(peer => peer.GetName()).ToArray());

        var fruit = treePeer.GetChildren()[0];
        var expand = fruit.GetProvider<IExpandCollapseProvider>()!;
        Assert.AreEqual(ExpandCollapseState.Collapsed, expand.ExpandCollapseState);
        Assert.AreEqual(ExpandCollapseState.LeafNode, treePeer.GetChildren()[1].GetProvider<IExpandCollapseProvider>()!.ExpandCollapseState);

        expand.Expand();
        window.PerformLayout();

        fruit = treePeer.GetChildren()[0];
        Assert.AreEqual(ExpandCollapseState.Expanded, fruit.GetProvider<IExpandCollapseProvider>()!.ExpandCollapseState);
        CollectionAssert.AreEqual(new[] { "Apple", "Pear" }, fruit.GetChildren().Select(peer => peer.GetName()).ToArray());
        CollectionAssert.AreEqual(new[] { "Fruit", "Stone" }, treePeer.GetChildren().Select(peer => peer.GetName()).ToArray());
        AssertConsistent(treePeer);

        fruit.GetChildren()[1].GetProvider<ISelectionItemProvider>()!.Select();
        Assert.AreEqual("Pear", tree.SelectedNode?.Text);
    }

    [TestMethod]
    public void GridView_ListsHeadersThenRows_WithCellsAsChildren()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var grid = new GridView { Width = 300, Height = 200 };
        grid.ItemsSource = ItemsView.Create(new[] { "Alpha", "Beta" });
        grid.SetColumns(
        [
            new GridViewColumn<string>
            {
                Header = "Name",
                Width = 200,
                CellTemplate = new DelegateTemplate<string>(
                    build: _ => new TextBlock(),
                    bind: (view, item, _, _) => ((TextBlock)view).Text = item),
            },
        ]);
        var window = Host(grid);

        var gridPeer = PeerOf(grid);
        Assert.AreEqual(AutomationControlType.DataGrid, gridPeer.GetControlType());
        var children = gridPeer.GetChildren();
        Assert.AreEqual("Name", children[0].GetName());
        var rows = children.Where(peer => peer.GetControlType() == AutomationControlType.DataItem).ToArray();
        CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, rows.Select(peer => peer.GetName()).ToArray());
        Assert.AreEqual("Beta", rows[1].GetChildren().Single().GetName());
        AssertConsistent(PeerOf(window));

        rows[1].GetProvider<ISelectionItemProvider>()!.Select();
        Assert.AreEqual(1, grid.SelectedIndex);
    }

    [TestMethod]
    public void MenuBar_ExpandOpensTheMenu_AndInvokeRunsTheCommand()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var save = new Command("test.save");
        var bar = new MenuBar().Item("_File", new Menu().Item("_Save", save).Item("Close"));
        var window = Host(new DockPanel().Children(bar));
        int saves = 0;
        window.Commands.Register(save, () => saves++);

        var barPeer = PeerOf(bar);
        Assert.AreEqual(AutomationControlType.MenuBar, barPeer.GetControlType());
        var file = barPeer.GetChildren().Single();
        Assert.AreEqual("File", file.GetName());
        Assert.IsFalse(file.GetBoundingRectangle().IsEmpty);

        file.GetProvider<IExpandCollapseProvider>()!.Expand();
        window.PerformLayout();
        Assert.AreEqual(ExpandCollapseState.Expanded, file.GetProvider<IExpandCollapseProvider>()!.ExpandCollapseState);

        var root = PeerOf(window);
        var menu = root.GetChildren().Single(peer => peer.GetControlType() == AutomationControlType.Menu);
        CollectionAssert.AreEqual(new[] { "Save", "Close" }, menu.GetChildren().Select(peer => peer.GetName()).ToArray());
        AssertConsistent(root);

        menu.GetChildren()[0].GetProvider<IInvokeProvider>()!.Invoke();
        window.PerformLayout();

        Assert.AreEqual(1, saves);
        Assert.IsFalse(root.GetChildren().Any(peer => peer.GetControlType() == AutomationControlType.Menu), "invoking closes the menu");
    }

    [TestMethod]
    public void MenuButtons_InvokeAndExpand()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        int clicks = 0;
        var split = new SplitButton { Content = new TextBlock { Text = "Paste" }, DropDownMenu = new Menu().Item("Paste special") };
        split.Click += () => clicks++;
        var dropDown = new DropDownButton().Content("More");
        dropDown.DropDownMenu = new Menu().Item("Settings");
        Host(new StackPanel().Children(split, dropDown));

        var splitPeer = PeerOf(split);
        Assert.AreEqual("Paste", splitPeer.GetName());
        splitPeer.GetProvider<IInvokeProvider>()!.Invoke();
        Assert.AreEqual(1, clicks);
        splitPeer.GetProvider<IExpandCollapseProvider>()!.Expand();
        Assert.IsTrue(split.IsDropDownOpen);
        splitPeer.GetProvider<IExpandCollapseProvider>()!.Collapse();
        Assert.IsFalse(split.IsDropDownOpen);

        var dropDownPeer = PeerOf(dropDown);
        Assert.AreEqual("More", dropDownPeer.GetName());
        dropDownPeer.GetProvider<IExpandCollapseProvider>()!.Expand();
        Assert.IsTrue(dropDown.IsDropDownOpen);
    }

    [TestMethod]
    public void Pickers_ReportTheirValueAsText()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var date = new DatePicker { SelectedDate = new DateTime(2026, 10, 19) };
        var color = new ColorPicker { SelectedColor = Color.FromArgb(255, 18, 52, 86) };
        Host(new StackPanel().Children(date, color));

        Assert.AreEqual(AutomationControlType.ComboBox, PeerOf(date).GetControlType());
        Assert.AreEqual("2026-10-19", PeerOf(date).GetProvider<IValueProvider>()!.Value);
        Assert.AreEqual("#FF123456", PeerOf(color).GetProvider<IValueProvider>()!.Value);
    }

    [TestMethod]
    public void Changes_RaiseOneNotificationEach()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var textBox = new TextBox();
        var button = new Button().Content("Go");
        var panel = new StackPanel().Children(textBox, button);
        var window = Host(panel);
        var textPeer = PeerOf(textBox);
        var buttonPeer = PeerOf(button);
        var root = PeerOf(window);

        var recorder = new EventRecorder();
        AutomationEvents.AddSink(recorder);
        try
        {
            textBox.Focus();
            Assert.AreEqual(1, recorder.FocusOf(textPeer), "focus");

            textPeer.GetProvider<IValueProvider>()!.SetValue("abc");
            Assert.AreEqual(1, recorder.PropertyOf(textPeer, AutomationProperty.ValueProperty), "value");

            button.IsEnabled = false;
            Assert.AreEqual(1, recorder.PropertyOf(buttonPeer, AutomationProperty.IsEnabledProperty), "enabled");

            recorder.Clear();
            panel.Add(new TextBlock { Text = "added" });
            Assert.AreEqual(1, recorder.StructureOf(root), "child added");

            recorder.Clear();
            panel.Remove(button);
            Assert.AreEqual(1, recorder.StructureOf(root), "child removed");
        }
        finally
        {
            AutomationEvents.RemoveSink(recorder);
        }
    }

    [TestMethod]
    public void ScrollingAVirtualizedList_RenamesItsChildren_AndAnnouncesTheChange()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var list = new ListBox { Height = 120 }.Items(Enumerable.Range(0, 200).Select(index => "Row " + index).ToArray());
        var window = Host(new StackPanel().Children(list));
        var listPeer = PeerOf(list);
        Assert.AreEqual("Row 0", listPeer.GetChildren()[0].GetName());

        var recorder = new EventRecorder();
        AutomationEvents.AddSink(recorder);
        try
        {
            listPeer.GetProvider<IScrollProvider>()!.SetScrollPercent(IScrollProvider.NoScroll, 50);
            window.PerformLayout();
            window.PerformLayout();
        }
        finally
        {
            AutomationEvents.RemoveSink(recorder);
        }

        var names = listPeer.GetChildren().Select(peer => peer.GetName()).ToArray();
        Assert.IsGreaterThan(0, recorder.StructureOf(listPeer), "the realized rows changed");
        Assert.IsFalse(names.Contains("Row 0"), $"stale rows after scrolling: {string.Join(", ", names)}");
        var visible = Enumerable.Range(0, 200).Where(index => list.TryGetContainer(index, out var container) && container.IsVisible)
            .Select(index => "Row " + index).ToArray();
        CollectionAssert.AreEquivalent(visible, names);
    }

    [TestMethod]
    public void TextBelowALeaf_GetsNoPeer()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var label = new Label { Text = "_Name" };
        var check = new CheckBox().Content("Agree");
        Host(new StackPanel().Children(label, check));

        Assert.IsTrue(VisualTree.Find(label, element => element is TextBlockBase && element != label) is UIElement inner
            && inner.GetAutomationPeer() == null);
        Assert.AreEqual(0, PeerOf(check).GetChildren().Count);
    }

    private sealed class EventRecorder : IAutomationEventSink
    {
        private readonly List<(AutomationPeer Peer, AutomationProperty Property)> _properties = [];
        private readonly List<AutomationPeer> _structures = [];
        private readonly List<AutomationPeer> _focuses = [];

        public void OnPropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
            => _properties.Add((peer, property));

        public void OnStructureChanged(AutomationPeer peer) => _structures.Add(peer);

        public void OnFocusChanged(AutomationPeer peer) => _focuses.Add(peer);

        public int PropertyOf(AutomationPeer peer, AutomationProperty property)
            => _properties.Count(entry => entry.Peer == peer && entry.Property == property);

        public int StructureOf(AutomationPeer peer) => _structures.Count(entry => entry == peer);

        public int FocusOf(AutomationPeer peer) => _focuses.Count(entry => entry == peer);

        public void Clear()
        {
            _properties.Clear();
            _structures.Clear();
            _focuses.Clear();
        }
    }
}
