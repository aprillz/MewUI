using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Elements;

[TestClass]
public sealed class ParentContextTests
{
    private sealed class RootChangeTracker : ContentControl
    {
        public List<(Element? OldRoot, Element? NewRoot)> Changes { get; } = new();

        protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
        {
            base.OnVisualRootChanged(oldRoot, newRoot);
            Changes.Add((oldRoot, newRoot));
        }
    }

    private static StyleSheet SheetWithAccent(Color background)
    {
        var sheet = new StyleSheet();
        sheet.Define("accent", () => new Style(typeof(Border))
        {
            Setters = [Setter.Create(Control.BackgroundProperty, background)],
        });
        return sheet;
    }

    // A direct non-null to non-null Parent reassignment must run the full detach then attach
    // sequence so context handling (style release, root notifications) cannot be bypassed.
    [TestMethod]
    public void DirectReparent_NormalizesIntoDetachThenAttach()
    {
        var window = new Window();
        var stack = new StackPanel();
        var containerA = new Border();
        var containerB = new Border();
        window.Content = stack;
        stack.Add(containerA);
        stack.Add(containerB);

        var tracker = new RootChangeTracker();
        containerA.Child = tracker;
        tracker.Changes.Clear();

        tracker.Parent = containerB;

        Assert.AreEqual(2, tracker.Changes.Count, "detach and attach notifications");
        Assert.AreSame(window, tracker.Changes[0].OldRoot);
        Assert.IsNull(tracker.Changes[0].NewRoot);
        Assert.IsNull(tracker.Changes[1].OldRoot);
        Assert.AreSame(window, tracker.Changes[1].NewRoot);
    }

    // Moving a styled element between two containers with different StyleSheets inside the
    // same window must re-resolve StyleName against the new location.
    [TestMethod]
    public void DirectReparent_ReResolvesStyleFromNewStyleSheet()
    {
        var colorA = Color.FromRgb(10, 0, 0);
        var colorB = Color.FromRgb(0, 20, 0);

        var window = new Window();
        var stack = new StackPanel();
        var containerA = new Border { StyleSheet = SheetWithAccent(colorA) };
        var containerB = new Border { StyleSheet = SheetWithAccent(colorB) };
        window.Content = stack;
        stack.Add(containerA);
        stack.Add(containerB);

        var child = new Border { StyleName = "accent" };
        containerA.Child = child;
        Assert.AreEqual(colorA, child.Background, "resolved from containerA's sheet");

        containerB.Child = child;
        Assert.AreEqual(colorB, child.Background, "re-resolved from containerB's sheet");
    }

    // A detached (windowless) subtree may hold a stale style after a move; attaching it to a
    // window must heal it through the attach-time resolution.
    [TestMethod]
    public void WindowlessMove_HealsStyleOnAttach()
    {
        var colorA = Color.FromRgb(30, 0, 0);
        var colorB = Color.FromRgb(0, 40, 0);

        var containerA = new Border { StyleSheet = SheetWithAccent(colorA) };
        var containerB = new Border { StyleSheet = SheetWithAccent(colorB) };

        var child = new Border { StyleName = "accent" };
        containerA.Child = child;
        child.Measure(new Size(100, 100));
        Assert.AreEqual(colorA, child.Background, "resolved via measure while detached");

        containerB.Child = child;

        var window = new Window();
        var stack = new StackPanel();
        stack.Add(containerB);
        window.Content = stack;
        Assert.AreEqual(colorB, child.Background, "healed by attach-time resolution");
    }

    // The context-version stamp must re-resolve the style on the next measure after a move,
    // even when nothing else triggers a resolution.
    [TestMethod]
    public void WindowlessMove_ReResolvesOnNextMeasure()
    {
        var colorA = Color.FromRgb(50, 0, 0);
        var colorB = Color.FromRgb(0, 60, 0);

        var containerA = new Border { StyleSheet = SheetWithAccent(colorA) };
        var containerB = new Border { StyleSheet = SheetWithAccent(colorB) };

        var child = new Border { StyleName = "accent" };
        containerA.Child = child;
        child.Measure(new Size(100, 100));
        Assert.AreEqual(colorA, child.Background);

        containerB.Child = child;
        child.Measure(new Size(200, 200));
        Assert.AreEqual(colorB, child.Background, "context-version mismatch forces re-resolution");
    }

    // Inherited values must re-resolve from the new parent chain after a direct move.
    [TestMethod]
    public void DirectReparent_ReResolvesInheritedValues()
    {
        var containerA = new Border { FontSize = 20 };
        var containerB = new Border { FontSize = 30 };

        var child = new Border();
        containerA.Child = child;
        Assert.AreEqual(20, child.FontSize);

        containerB.Child = child;
        Assert.AreEqual(30, child.FontSize);
    }

    // ContextParentOverride diverts style and inherited resolution through the owner while
    // DPI/visual root keep following the visual parent.
    [TestMethod]
    public void ContextParentOverride_ResolvesStyleAndInheritanceThroughOwner()
    {
        var accent = Color.FromRgb(70, 0, 0);

        var window = new Window();
        var stack = new StackPanel();
        var owner = new Border { StyleSheet = SheetWithAccent(accent), FontSize = 25 };
        var hostSlot = new Border();
        hostSlot.ContextParentOverride = owner;

        var hosted = new Border { StyleName = "accent" };
        hostSlot.Child = hosted;

        stack.Add(owner);
        stack.Add(hostSlot);
        window.Content = stack;

        Assert.AreEqual(accent, hosted.Background, "style resolved through the owner context");
        Assert.AreEqual(25, hosted.FontSize, "inherited value resolved through the owner context");
        Assert.AreSame(window, hosted.FindVisualRoot(), "visual root keeps following Parent");
    }

    // Clearing the override must restore resolution through the visual chain.
    [TestMethod]
    public void ContextParentOverride_ClearRestoresVisualChainResolution()
    {
        var owner = new Border { FontSize = 25 };
        var visualParent = new Border { FontSize = 15 };
        var hostSlot = new Border();
        var hosted = new Border();
        visualParent.Child = hostSlot;
        hostSlot.Child = hosted;

        hostSlot.ContextParentOverride = owner;
        Assert.AreEqual(25, hosted.FontSize, "diverted past the slot to the owner");

        hostSlot.ContextParentOverride = null;
        Assert.AreEqual(15, hosted.FontSize, "cache cleared and re-resolved via visual parent");
    }

    // Runtime StyleName changes on an attached control must apply immediately.
    [TestMethod]
    public void StyleNameChange_AppliesImmediatelyWhenAttached()
    {
        var colorA = Color.FromRgb(80, 0, 0);
        var colorB = Color.FromRgb(0, 90, 0);

        var sheet = new StyleSheet();
        sheet.Define("a", () => new Style(typeof(Border))
        {
            Setters = [Setter.Create(Control.BackgroundProperty, colorA)],
        });
        sheet.Define("b", () => new Style(typeof(Border))
        {
            Setters = [Setter.Create(Control.BackgroundProperty, colorB)],
        });

        var window = new Window();
        var container = new Border { StyleSheet = sheet };
        var child = new Border { StyleName = "a" };
        container.Child = child;
        window.Content = container;
        Assert.AreEqual(colorA, child.Background);

        child.StyleName = "b";
        Assert.AreEqual(colorB, child.Background, "no measure pass needed");
    }

    // Properties set by the old style but absent from the new one must not linger after a swap.
    [TestMethod]
    public void StyleSwap_ClearsValuesTheNewStyleDoesNotSet()
    {
        var sheet = new StyleSheet();
        sheet.Define("wide", () => new Style(typeof(Border))
        {
            Setters =
            [
                Setter.Create(FrameworkElement.WidthProperty, 100.0),
                Setter.Create(Control.BackgroundProperty, Color.FromRgb(1, 2, 3)),
            ],
        });
        sheet.Define("plain", () => new Style(typeof(Border))
        {
            Setters = [Setter.Create(Control.BackgroundProperty, Color.FromRgb(4, 5, 6))],
        });

        var window = new Window();
        var container = new Border { StyleSheet = sheet };
        var child = new Border { StyleName = "wide" };
        container.Child = child;
        window.Content = container;
        Assert.AreEqual(100.0, child.Width);

        child.StyleName = "plain";
        Assert.IsTrue(double.IsNaN(child.Width), "stale style width cleared back to default");
    }
}
