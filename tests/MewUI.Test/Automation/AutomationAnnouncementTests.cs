using Aprillz.MewUI;
using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Automation;

/// <summary>
/// What readers are told as the user works: the element or part the keyboard acts on, menus and tooltips opening and
/// closing, and the per-window split of the tree that platform providers serve.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AutomationAnnouncementTests
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

    private static string Describe(IEnumerable<AutomationPeer> peers)
        => string.Join(", ", peers.Select(peer => $"{peer.GetControlType()} '{peer.GetName()}'"));

    private static T Record<T>(Func<EventRecorder, T> body)
    {
        var recorder = new EventRecorder();
        AutomationEvents.AddSink(recorder);
        try
        {
            return body(recorder);
        }
        finally
        {
            AutomationEvents.RemoveSink(recorder);
        }
    }

    [TestMethod]
    public void FocusOnAnElementNoClientAskedFor_IsAnnounced()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var textBox = new TextBox();
        Host(new StackPanel().Children(new Button().Content("First"), textBox));

        var focused = Record(recorder =>
        {
            textBox.Focus();
            return recorder.Focused.ToArray();
        });

        CollectionAssert.AreEqual(new[] { PeerOf(textBox) }, focused);
        Assert.IsTrue(PeerOf(textBox).HasKeyboardFocus());
    }

    [TestMethod]
    public void FocusBelowALeaf_IsAnnouncedOnTheLeaf()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var inner = new TextBox { Width = 100 };
        var leaf = new LeafHost { Content = inner };
        var first = new Button().Content("First");
        Host(new StackPanel().Children(first, leaf));
        first.Focus();

        var focused = Record(recorder =>
        {
            inner.Focus();
            return recorder.Focused.ToArray();
        });

        Assert.IsTrue(inner.IsFocused, "the inner text box took focus");
        Assert.IsNull(inner.GetAutomationPeer(), "an element below a leaf has no peer");
        CollectionAssert.AreEqual(new[] { PeerOf(leaf) }, focused, Describe(focused));
        Assert.IsTrue(PeerOf(leaf).HasKeyboardFocus());
    }

    [TestMethod]
    public void ListFocus_GoesToTheSelectedItem_AndFollowsTheSelection()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var list = new ListBox { Height = 120 }.Items("Red", "Green", "Blue");
        list.SelectedIndex = 0;
        var window = Host(new StackPanel().Children(list));

        Record(recorder =>
        {
            list.Focus();
            var listPeer = PeerOf(list);
            var items = listPeer.GetChildren();
            CollectionAssert.AreEqual(new[] { items[0] }, recorder.Focused.ToArray(), "focus lands on the selected item");
            Assert.IsTrue(items[0].HasKeyboardFocus());
            Assert.IsFalse(listPeer.HasKeyboardFocus(), "the list hands focus to its item");

            recorder.Clear();
            window.SendKeyPress(Key.Down);
            window.PerformLayout();
            Assert.AreEqual(1, list.SelectedIndex);
            CollectionAssert.AreEqual(new[] { items[1] }, recorder.Focused.ToArray(), "focus follows the selection");
            Assert.IsTrue(items[1].HasKeyboardFocus());
            Assert.IsFalse(items[0].HasKeyboardFocus());
            return 0;
        });
    }

    [TestMethod]
    public void ContextMenu_AnnouncesOpening_TheHighlightedEntry_AndClosing()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var owner = new Border
        {
            Width = 50,
            Height = 50,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = Host(owner);
        var menu = new ContextMenu();
        menu.AddItem("Open");
        menu.AddItem("Close");

        Record(recorder =>
        {
            menu.Show(owner, new Point(10, 10));
            window.PerformLayout();
            var menuPeer = PeerOf(menu);
            Assert.AreEqual(1, recorder.EventsOf(menuPeer, AutomationEventKind.MenuOpened), "opened");
            Assert.IsTrue(menu.IsFocused, "the menu takes focus; before: " + Describe(recorder.Focused));

            recorder.Clear();
            var bounds = menu.Bounds;
            window.SendMouseMove(new Point(bounds.X + bounds.Width / 2, bounds.Y + 12));
            var entry = menuPeer.GetChildren()[0];
            Assert.AreEqual("Open", entry.GetName());
            CollectionAssert.AreEqual(new[] { entry }, recorder.Focused.ToArray(), "the highlighted entry has focus: " + Describe(recorder.Focused));
            Assert.IsTrue(entry.HasKeyboardFocus());
            Assert.IsFalse(menuPeer.HasKeyboardFocus());

            window.SendKeyPress(Key.Escape);
            window.PerformLayout();
            Assert.AreEqual(1, recorder.EventsOf(menuPeer, AutomationEventKind.MenuClosed), "closed");
            return 0;
        });
    }

    [TestMethod]
    public void SurfaceTree_KeepsInSurfacePopupsInTheWindow_AndFindsPeersByPointAndFocus()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var button = new Button().Content("Go");
        var textBox = new TextBox { Width = 100 };
        var window = Host(new StackPanel().Children(button, textBox));
        var root = PeerOf(window);
        var menu = new ContextMenu();
        menu.AddItem("Copy");
        menu.Show(button, new Point(5, 5));
        window.PerformLayout();

        var menuPeer = PeerOf(menu);
        Assert.AreSame(window, AutomationSurface.Of(menuPeer), "a popup drawn in the window belongs to its tree");
        CollectionAssert.Contains(AutomationSurface.ChildrenOf(root).ToArray(), menuPeer);
        Assert.AreSame(root, AutomationSurface.ParentOf(menuPeer));

        menu.CloseTree(window);
        window.PerformLayout();
        var buttonBounds = AutomationSurface.ScreenBoundsOf(PeerOf(button));
        Assert.AreSame(PeerOf(button), AutomationSurface.HitTest(root, buttonBounds.Center));

        textBox.Focus();
        Assert.AreSame(PeerOf(textBox), AutomationSurface.FocusedIn(window));
    }

    /// <summary>A control whose peer speaks for everything below it, as a custom spinner with an inner edit box would.</summary>
    private sealed class LeafHost : ContentControl
    {
        protected override AutomationPeer? OnCreateAutomationPeer() => new LeafHostPeer(this);
    }

    private sealed class LeafHostPeer : UIElementAutomationPeer
    {
        public LeafHostPeer(UIElement owner)
            : base(owner)
        {
        }

        internal override bool IsLeaf => true;

        protected override AutomationControlType GetControlTypeCore() => AutomationControlType.Spinner;
    }

    private sealed class EventRecorder : IAutomationEventSink
    {
        private readonly List<(AutomationPeer Peer, AutomationEventKind Kind)> _events = [];

        public List<AutomationPeer> Focused { get; } = [];

        public void OnPropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
        {
        }

        public void OnStructureChanged(AutomationPeer peer)
        {
        }

        public void OnFocusChanged(AutomationPeer peer) => Focused.Add(peer);

        public void OnEvent(AutomationPeer peer, AutomationEventKind kind) => _events.Add((peer, kind));

        public int EventsOf(AutomationPeer peer, AutomationEventKind kind)
            => _events.Count(entry => entry.Peer == peer && entry.Kind == kind);

        public void Clear()
        {
            Focused.Clear();
            _events.Clear();
        }
    }
}
