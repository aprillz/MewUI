using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Input;

/// <summary>
/// Input-driven end-to-end coverage on the headless window: hover state triggers, click
/// routing/focus, and popup close policy, all through the production input router.
/// Not parallelizable: input routing shares process-wide static state (WindowDragDropRouter),
/// so concurrent injected streams from different windows would interfere.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HeadlessInputTests
{
    private static readonly Color NORMAL_COLOR = Color.FromRgb(10, 20, 30);
    private static readonly Color HOT_COLOR = Color.FromRgb(200, 40, 40);

    [TestMethod]
    public void PointerRouting_HitTestsWindowOncePerEvent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = new CountingWindow();
        window.AttachBackend(new HeadlessWindowBackend());
        window.SetClientSizeDip(800, 600);
        window.Content = new Border();
        window.PerformLayout();

        window.SendMouseMove(new Point(10, 10));
        Assert.AreEqual(1, window.HitTestCount, "mouse move");

        window.HitTestCount = 0;
        window.SendMouseDown(new Point(10, 10));
        Assert.AreEqual(1, window.HitTestCount, "mouse button");
    }

    private sealed class CountingWindow : Window
    {
        public int HitTestCount { get; set; }

        protected override UIElement? OnHitTest(Point point)
        {
            HitTestCount++;
            return base.OnHitTest(point);
        }
    }

    private static StyleSheet HotSheet(double hotWidth = 100)
    {
        var sheet = new StyleSheet();
        sheet.Define("hot", () => new Style(typeof(Border))
        {
            Setters =
            [
                Setter.Create(FrameworkElement.WidthProperty, 100.0),
                Setter.Create(FrameworkElement.HeightProperty, 40.0),
                Setter.Create(Control.BackgroundProperty, NORMAL_COLOR),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, HOT_COLOR),
                        Setter.Create(FrameworkElement.WidthProperty, hotWidth),
                    ],
                },
            ],
        });
        return sheet;
    }

    [TestMethod]
    public void MouseMove_AppliesAndRestoresHotTrigger()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var container = new Border { StyleSheet = HotSheet() };
        var target = new Border { StyleName = "hot" };
        container.Child = target;
        window.Content = container;
        window.PerformLayout();

        Assert.AreEqual(NORMAL_COLOR, target.Background);

        window.SendMouseMove(target.CenterOf());
        Assert.IsTrue(target.IsMouseOver, "hit test routed mouse-over to the target");
        window.PerformLayout(); // state reconciliation happens in the pre-layout visual-state update
        Assert.AreEqual(HOT_COLOR, target.Background, "Hot trigger applied at the next update pass");

        window.SendMouseMove(new Point(1, 1));
        Assert.IsFalse(target.IsMouseOver);
        window.PerformLayout();
        Assert.AreEqual(NORMAL_COLOR, target.Background, "base setter restored after hover ends");
    }

    // Multiple state changes inside one frame resolve once, to the final state, at the
    // visual-state update.
    [TestMethod]
    public void SameFrameEnterLeave_ResolvesToFinalStateOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var container = new Border { StyleSheet = HotSheet() };
        var target = new Border { StyleName = "hot" };
        container.Child = target;
        window.Content = container;
        window.PerformLayout();

        window.SendMouseMove(target.CenterOf());
        window.SendMouseMove(new Point(1, 1));
        window.PerformLayout();

        Assert.IsFalse(target.IsMouseOver);
        Assert.AreEqual(NORMAL_COLOR, target.Background, "enter+leave within one frame nets out to the base state");
    }

    [TestMethod]
    public void HotTrigger_LayoutPropertyReflowsOnNextLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var container = new Border { StyleSheet = HotSheet(hotWidth: 200) };
        var target = new Border { StyleName = "hot", HorizontalAlignment = HorizontalAlignment.Left };
        container.Child = target;
        window.Content = container;
        window.PerformLayout();
        Assert.AreEqual(100, target.Bounds.Width);

        window.SendMouseMove(target.CenterOf());
        window.PerformLayout();
        Assert.AreEqual(200, target.Bounds.Width, "Hot trigger width applied in the following layout pass");
    }

    [TestMethod]
    public void Click_RaisesButtonClickAndFocuses()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var button = new Button
        {
            Content = new TextBlock { Text = "Click Me" },
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = button;
        window.PerformLayout();

        int clicks = 0;
        button.Click += () => clicks++;

        window.SendClick(button.CenterOf());

        Assert.AreEqual(1, clicks, "click routed through hit test and raised Click");
        Assert.IsTrue(button.IsFocused, "pointer down focused the button");
    }

    private sealed class PressableControl : ContentControl
    {
        public void Press(bool pressed) => SetPressed(pressed);
    }

    // On-screen elements animate their state transition; off-screen elements snap at the
    // visual-state update so no animation runs on invisible pixels.
    [TestMethod]
    public void VisualStateUpdate_AnimatesOnScreenAndSnapsOffScreen()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var normal = Color.FromRgb(10, 10, 10);
        var pressed = Color.FromRgb(220, 30, 30);
        var sheet = new StyleSheet();
        sheet.Define("pressable", () => new Style(typeof(ContentControl))
        {
            Transitions = [Transition.Create(Control.BackgroundProperty, durationMs: 500)],
            Setters = [Setter.Create(Control.BackgroundProperty, normal)],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters = [Setter.Create(Control.BackgroundProperty, pressed)],
                },
            ],
        });

        var window = HeadlessWindow.Create();
        var stack = new StackPanel { StyleSheet = sheet };
        var onScreen = new PressableControl { StyleName = "pressable", Height = 40 };
        var spacer = new Border { Height = 2000 };
        var offScreen = new PressableControl { StyleName = "pressable", Height = 40 };
        stack.Add(onScreen);
        stack.Add(spacer);
        stack.Add(offScreen);
        window.Content = stack;
        window.PerformLayout();

        onScreen.Press(true);
        offScreen.Press(true);
        window.PerformLayout();

        Assert.AreEqual(pressed, offScreen.Background, "off-screen element snapped to the target value");
        Assert.AreNotEqual(pressed, onScreen.Background, "on-screen element is animating from the base value");
    }

    [TestMethod]
    public void ClickOutsidePopup_ClosesIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();

        // Keep the owner small: clicking the owner counts as "related" and keeps the popup open,
        // so the outside click must land on empty window space.
        var owner = new Border
        {
            Width = 50,
            Height = 50,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = owner;
        window.PerformLayout();

        var popupRoot = new Border { Background = Color.FromRgb(1, 2, 3) };
        window.ShowPopup(owner, popupRoot, new Rect(200, 200, 100, 50));
        Assert.AreSame(window, popupRoot.FindVisualRoot(), "popup attached");

        window.SendClick(new Point(600, 500));
        Assert.IsNull(popupRoot.FindVisualRoot(), "pointer-down close policy detached the popup");
    }
}
