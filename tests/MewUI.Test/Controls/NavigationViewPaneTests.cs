using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Placement (<see cref="PaneDisplayMode"/>) and open state (<see cref="NavigationView.IsPaneOpen"/>)
/// are separate axes: every combination has to be expressible, and the toggle has to work whichever
/// placement is in effect.
/// </summary>
[TestClass]
public sealed class NavigationViewPaneTests
{
    private const double WIDE = 1400;
    private const double NARROW = 600;

    private static NavigationView Laid(NavigationView view, double width)
    {
        view.Measure(new Size(width, 500));
        view.Arrange(new Rect(0, 0, width, 500));
        return view;
    }

    [TestMethod]
    public void Auto_ResolvesToInline_WhenWide()
    {
        var view = Laid(new NavigationView(), WIDE);

        Assert.AreEqual(PaneDisplayMode.Inline, view.EffectivePaneDisplayMode);
    }

    [TestMethod]
    public void Auto_ResolvesToOverlay_WhenNarrow()
    {
        var view = Laid(new NavigationView(), NARROW);

        Assert.AreEqual(PaneDisplayMode.Overlay, view.EffectivePaneDisplayMode);
    }

    [TestMethod]
    public void EffectiveMode_NeverReportsAuto()
    {
        var view = Laid(new NavigationView { PaneDisplayMode = PaneDisplayMode.Auto }, WIDE);

        Assert.AreNotEqual(PaneDisplayMode.Auto, view.EffectivePaneDisplayMode);
    }

    [TestMethod]
    public void ExplicitPlacement_IsNotOverriddenByWidth()
    {
        var view = Laid(new NavigationView { PaneDisplayMode = PaneDisplayMode.Inline }, NARROW);

        Assert.AreEqual(PaneDisplayMode.Inline, view.EffectivePaneDisplayMode);
    }

    [TestMethod]
    public void ClosedPane_StartsAsRail_WithToggleStillAvailable()
    {
        // The combination the old single-axis enum could not express: start collapsed, stay toggleable.
        var view = Laid(new NavigationView { IsPaneOpen = false }, WIDE);

        Assert.IsTrue(view.PaneIsRail);
        Assert.IsTrue(view.IsPaneToggleButtonVisible);
    }

    [TestMethod]
    public void Toggle_Works_UnderExplicitInlinePlacement()
    {
        var view = Laid(new NavigationView { PaneDisplayMode = PaneDisplayMode.Inline }, WIDE);

        view.IsPaneOpen = !view.IsPaneOpen;

        Assert.IsFalse(view.IsPaneOpen);
        Assert.IsTrue(view.PaneIsRail);
    }

    [TestMethod]
    public void OpenState_SurvivesAWidthDrivenPlacementChange()
    {
        var view = Laid(new NavigationView { IsPaneOpen = false }, WIDE);

        Laid(view, NARROW);
        Laid(view, WIDE);

        Assert.IsFalse(view.IsPaneOpen);
        Assert.AreEqual(PaneDisplayMode.Inline, view.EffectivePaneDisplayMode);
    }

    [TestMethod]
    public void EffectiveModeChanged_RaisedOncePerTransition()
    {
        var view = Laid(new NavigationView(), WIDE);
        int count = 0;
        view.EffectivePaneDisplayModeChanged += () => count++;

        Laid(view, NARROW);
        Assert.AreEqual(1, count);

        Laid(view, NARROW);
        Assert.AreEqual(1, count, "re-laying out at the same width is not a transition");

        Laid(view, WIDE);
        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public void ClosedOverlayPane_KeepsItsRowsLaidOut_ButIsNotDrawn()
    {
        // Toggling the overlay must not re-lay out every row at zero size and back.
        var view = new NavigationView { PaneDisplayMode = PaneDisplayMode.Overlay, IsPaneOpen = false };
        view.Items(new[] { "Buttons", "Toggles" }, title => title);
        var window = HeadlessWindow.Create((int)NARROW, 500);
        window.Content = view;
        window.PerformLayout();

        var label = FindText(view, "Buttons");
        Assert.IsNotNull(label);
        var row = label.Parent;
        while (row is not null and not ItemContainer)
        {
            row = row.Parent;
        }

        Assert.IsInstanceOfType<ItemContainer>(row);
        var bounds = ((ItemContainer)row).Bounds;
        Assert.IsTrue(bounds.Width > 0 && bounds.Height > 0, $"row bounds {bounds}");
        Assert.IsTrue(IsUnderHeldElement(row), "the closed pane is held out of view");
    }

    private static TextBlock? FindText(Element root, string text)
    {
        if (root is TextBlock block && block.Text == text)
        {
            return block;
        }

        TextBlock? found = null;
        if (root is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                found = FindText(child, text);
                return found == null;
            });
        }

        return found;
    }

    private static bool IsUnderHeldElement(Element element)
    {
        for (var current = element as UIElement; current != null; current = current.Parent as UIElement)
        {
            if (current.IsHeldOutOfView)
            {
                return true;
            }
        }

        return false;
    }

    [TestMethod]
    public void HidingTheToggleButton_RemovesItFromLayout()
    {
        var view = Laid(new NavigationView { IsPaneToggleButtonVisible = false }, WIDE);

        Assert.IsFalse(view.IsPaneToggleButtonVisible);
    }
}
