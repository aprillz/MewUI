using System.Reflection;
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

    [TestMethod]
    public void MenuAndDropDownButtonsHaveNoFixedHeightCapByDefault()
    {
        Assert.IsTrue(double.IsPositiveInfinity(new ContextMenu().MaxMenuHeight));
        Assert.IsTrue(double.IsPositiveInfinity(new DropDownButton().MaxDropDownHeight));
        Assert.IsTrue(double.IsPositiveInfinity(new SplitButton().MaxDropDownHeight));
    }

    [TestMethod]
    public void MenuThatFitsTheRegion_GrowsToItsFullHeightWithoutScrolling()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, target) = MakeWindow();
        var menu = MakeTallMenu(13);
        menu.Placement = MenuPlacement.Below;

        menu.Show(target);
        window.PerformLayout();

        Assert.IsGreaterThanOrEqualTo(13 * ROW_HEIGHT, menu.Bounds.Height, "a menu taller than 320 DIP but inside the region is not capped");
        Assert.IsFalse(IsScrollBarVisible(menu));
    }

    [TestMethod]
    public void MenuTallerThanTheRegion_TakesTheRoomierSideAndScrolls()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, target) = MakeWindow();
        var menu = MakeTallMenu(40);
        menu.Placement = MenuPlacement.Below;

        menu.Show(target);
        window.PerformLayout();

        Assert.AreEqual(target.Bounds.Bottom, menu.Bounds.Y, 0.5, "below has more room than above");
        Assert.AreEqual(window.ClientSize.Height, menu.Bounds.Bottom, 0.5, "the height is cut to the room below");
        Assert.IsTrue(IsScrollBarVisible(menu));
    }

    [TestMethod]
    public void MenuTallerThanTheRegion_FlipsAboveWhenAboveIsRoomier()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, target) = MakeWindow(targetTop: 500);
        var menu = MakeTallMenu(40);
        menu.Placement = MenuPlacement.Below;

        menu.Show(target);
        window.PerformLayout();

        Assert.AreEqual(target.Bounds.Y, menu.Bounds.Bottom, 0.5, "above has more room than below");
        Assert.AreEqual(0, menu.Bounds.Y, 0.5, "the height is cut to the room above");
        Assert.IsTrue(IsScrollBarVisible(menu));
    }

    [TestMethod]
    public void ExplicitMaxMenuHeight_StillCapsTheMenu()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, target) = MakeWindow();
        var menu = MakeTallMenu(13);
        menu.MaxMenuHeight = 200;
        menu.Placement = MenuPlacement.Below;

        menu.Show(target);
        window.PerformLayout();

        Assert.AreEqual(200, menu.Bounds.Height, 0.5);
        Assert.IsTrue(IsScrollBarVisible(menu));
    }

    [TestMethod]
    public void SubMenuTallerThanTheRegion_StaysInsideIt()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, target) = MakeWindow();
        var subMenu = new Menu();
        for (int index = 0; index < 40; index++)
        {
            subMenu.Item($"Sub {index}");
        }
        var menu = new ContextMenu { ItemHeight = ROW_HEIGHT };
        menu.AddSubMenu("Parent", subMenu);
        menu.Show(target, new Point(100, 80));
        window.PerformLayout();

        window.SendMouseMove(new Point(menu.Bounds.X + menu.Bounds.Width / 2, menu.Bounds.Y + ROW_HEIGHT / 2));
        window.PerformLayout();

        var sub = PopupElementAt(window, 1).Bounds;
        Assert.IsGreaterThanOrEqualTo(-0.5, sub.Y);
        Assert.IsLessThanOrEqualTo(window.ClientSize.Height + 0.5, sub.Bottom);
    }

    private const double ROW_HEIGHT = 30;

    private static ContextMenu MakeTallMenu(int itemCount)
    {
        var menu = new ContextMenu { ItemHeight = ROW_HEIGHT };
        for (int index = 0; index < itemCount; index++)
        {
            menu.Item($"Item {index}");
        }
        return menu;
    }

    private static bool IsScrollBarVisible(ContextMenu menu)
    {
        bool visible = false;
        ((IVisualTreeHost)menu).VisitChildren(child =>
        {
            if (child is ScrollBar bar)
            {
                visible = bar.IsVisible;
            }
            return true;
        });
        return visible;
    }

    private static UIElement PopupElementAt(Window window, int index)
    {
        var popupManager = typeof(Window).GetField("_popupManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var method = popupManager.GetType().GetMethod("ElementAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (UIElement)method.Invoke(popupManager, [index])!;
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
