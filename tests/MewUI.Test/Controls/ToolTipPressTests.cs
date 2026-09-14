using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A tooltip opens when the pointer comes to rest on its element. A pointer that arrives with the left
/// or right button held is mid-click or mid-drag, so the tooltip stays away until a fresh enter with the
/// buttons up (issue #259: a splitter dragged fast onto a button popped the button's tooltip).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ToolTipPressTests
{
    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    private static int PopupCount(Window window)
    {
        var manager = typeof(Window).GetField("_popupManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        return (int)manager.GetType().GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
    }

    private static (Window window, Border plain, Button tipped) Host()
    {
        var plain = new Border { Width = 100, Height = 100 };
        var tipped = new Button { Width = 100, Height = 100, ToolTip = new TextBlock { Text = "Tip" } };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Add(plain);
        row.Add(tipped);

        var window = HeadlessWindow.Create(400, 200);
        window.Content = row;
        window.PerformLayout();
        return (window, plain, tipped);
    }

    [TestMethod]
    public void EnteringWithTheButtonsUp_OpensTheToolTip()
    {
        if (SkipOnNonWindows()) return;

        var (window, plain, tipped) = Host();

        window.SendMouseMove(plain.CenterOf());
        window.SendMouseMove(tipped.CenterOf());

        Assert.AreEqual(1, PopupCount(window), "a plain enter opens the tooltip");
    }

    [TestMethod]
    public void EnteringWithTheLeftButtonHeld_DoesNotOpenTheToolTip()
    {
        if (SkipOnNonWindows()) return;

        var (window, plain, tipped) = Host();

        window.SendMouseMove(plain.CenterOf());
        window.SendMouseDown(plain.CenterOf());
        window.SendMouseDrag(tipped.CenterOf());

        Assert.IsTrue(tipped.IsMouseOver, "the pointer did enter the button");
        Assert.AreEqual(0, PopupCount(window), "entering with the left button held opened the tooltip");
    }

    [TestMethod]
    public void EnteringWithTheRightButtonHeld_DoesNotOpenTheToolTip()
    {
        if (SkipOnNonWindows()) return;

        var (window, plain, tipped) = Host();

        window.SendMouseMove(plain.CenterOf());
        window.SendMouseDown(plain.CenterOf(), MouseButton.Right);
        var target = tipped.CenterOf();
        WindowInputRouter.MouseMove(window, target, target, leftDown: false, rightDown: true, middleDown: false);

        Assert.IsTrue(tipped.IsMouseOver, "the pointer did enter the button");
        Assert.AreEqual(0, PopupCount(window), "entering with the right button held opened the tooltip");
    }

    [TestMethod]
    public void ReleasingOverTheElement_WaitsForAFreshEnter()
    {
        if (SkipOnNonWindows()) return;

        var (window, plain, tipped) = Host();

        window.SendMouseMove(plain.CenterOf());
        window.SendMouseDown(plain.CenterOf());
        window.SendMouseDrag(tipped.CenterOf());
        window.SendMouseUp(tipped.CenterOf());

        Assert.AreEqual(0, PopupCount(window), "the release alone is not an enter");

        window.SendMouseMove(plain.CenterOf());
        window.SendMouseMove(tipped.CenterOf());

        Assert.AreEqual(1, PopupCount(window), "leaving and coming back with the buttons up opens the tooltip");
    }
}
