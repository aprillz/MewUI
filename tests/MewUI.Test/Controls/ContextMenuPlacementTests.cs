using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A menu opens where its placement policy says: at the pointer, or against a side of its target,
/// flipping to the other side when the preferred side has no room.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ContextMenuPlacementTests
{
    [TestMethod]
    public void ExplicitPoint_OpensAtThatPoint()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, target) = MakeWindow();
        var menu = MakeMenu();

        menu.Show(target, new Point(100, 80));
        window.PerformLayout();

        Assert.AreEqual(100, menu.Bounds.X, 0.5);
        Assert.AreEqual(80, menu.Bounds.Y, 0.5);
        Assert.AreSame(target, menu.PlacementTarget);
    }

    [TestMethod]
    public void Below_OpensUnderTheTargetWithTheOffset()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, target) = MakeWindow();
        var menu = MakeMenu();
        menu.Placement = MenuPlacement.Below;
        menu.PlacementOffset = new Point(0, 1);

        menu.Show(target);
        window.PerformLayout();

        Assert.AreEqual(target.Bounds.X, menu.Bounds.X, 0.5);
        Assert.AreEqual(target.Bounds.Bottom + 1, menu.Bounds.Y, 0.5);
    }

    [TestMethod]
    public void Below_FlipsAboveTheTargetWhenOutOfRoom()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, target) = MakeWindow(targetTop: 500);
        var menu = MakeMenu();
        menu.Placement = MenuPlacement.Below;
        menu.PlacementOffset = new Point(0, 1);

        menu.Show(target);
        window.PerformLayout();

        Assert.AreEqual(target.Bounds.Y - 1, menu.Bounds.Bottom, 0.5,
            "no room below: the menu's bottom sits on the target's top, offset mirrored");
    }

    [TestMethod]
    public void Right_OpensBesideTheTarget()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, target) = MakeWindow();
        var menu = MakeMenu();
        menu.Placement = MenuPlacement.Right;

        menu.Show(target);
        window.PerformLayout();

        Assert.AreEqual(target.Bounds.Right, menu.Bounds.X, 0.5);
        Assert.AreEqual(target.Bounds.Y, menu.Bounds.Y, 0.5);
    }

    [TestMethod]
    public void PlacementTarget_SurvivesTheMenuClosing()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, target) = MakeWindow();
        var menu = MakeMenu();

        menu.Show(target, new Point(100, 80));
        window.PerformLayout();
        window.CloseAllPopups();
        window.PerformLayout();

        Assert.AreSame(target, menu.PlacementTarget,
            "a handler running after an item click closed the menu still needs the target");
    }

    private static (Window Window, Border Target) MakeWindow(double targetTop = 40)
    {
        var window = HeadlessWindow.Create();
        var target = new Border
        {
            Width = 120,
            Height = 32,
            Margin = new Thickness(60, targetTop, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = target;
        window.PerformLayout();
        return (window, target);
    }

    private static ContextMenu MakeMenu()
        => new ContextMenu().Item("Alpha").Item("Beta").Item("Gamma");
}
