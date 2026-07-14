using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Interaction coverage for menus through the production input router. NOTE: these run against the
/// in-surface popup host (headless has no OS windows), so they cannot exercise the native-window
/// activation/deactivation close path.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MenuInteractionTests
{
    private static int PopupCount(Window window)
    {
        var pm = typeof(Window).GetField("_popupManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var count = pm.GetType().GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pm);
        return (int)count!;
    }

    [TestMethod]
    public void HoveringSubMenuItem_OpensSubMenuAndKeepsParentOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var owner = new Border
        {
            Width = 50,
            Height = 50,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = owner;
        window.PerformLayout();

        var subMenu = new Menu().Item("Sub A").Item("Sub B");
        var menu = new ContextMenu();
        menu.AddSubMenu("Parent", subMenu);
        menu.AddItem("Other");
        menu.ShowAt(owner, new Point(100, 100));
        window.PerformLayout();

        Assert.AreEqual(1, PopupCount(window), "context menu opened");

        var bounds = menu.Bounds;
        window.SendMouseMove(new Point(bounds.X + bounds.Width / 2, bounds.Y + 12));
        window.PerformLayout();

        Assert.AreEqual(2, PopupCount(window), "hovering the submenu row opens the nested submenu");
        Assert.AreSame(window, menu.FindVisualRoot(), "parent menu stays open while its submenu opens");

        // Now move the pointer INTO the submenu (second movement) and verify the parent survives.
        var subMenuElement = PopupElementAt(window, 1);
        var sub = subMenuElement.Bounds;
        window.SendMouseMove(new Point(sub.X + sub.Width / 2, sub.Y + 12));
        window.PerformLayout();

        Assert.AreEqual(2, PopupCount(window), "both menus stay open when the pointer enters the submenu");
        Assert.AreSame(window, menu.FindVisualRoot(), "parent menu stays open when the pointer is over the submenu");
    }

    private static UIElement PopupElementAt(Window window, int index)
    {
        var pm = typeof(Window).GetField("_popupManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var method = pm.GetType().GetMethod("ElementAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (UIElement)method.Invoke(pm, [index])!;
    }
}
