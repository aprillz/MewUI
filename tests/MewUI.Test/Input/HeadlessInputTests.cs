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
        Assert.AreEqual(HOT_COLOR, target.Background, "Hot trigger applied on hover");

        window.SendMouseMove(new Point(1, 1));
        Assert.IsFalse(target.IsMouseOver);
        Assert.AreEqual(NORMAL_COLOR, target.Background, "base setter restored after hover ends");
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
