using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// The window's shared tooltip only borrows an element's ToolTip content while it is shown, and a window
/// closed by a click handler routes nothing more for that click (a click that closed its window from inside
/// a modal loop came back to hover the button and threw reopening its tooltip).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ToolTipContentLifetimeTests
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

    private static (Window window, Border plain, Button tipped, TextBlock tip) Host()
    {
        var tip = new TextBlock { Text = "Tip" };
        var plain = new Border { Width = 100, Height = 100 };
        var tipped = new Button { Width = 100, Height = 100, ToolTip = tip };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Add(plain);
        row.Add(tipped);

        var window = HeadlessWindow.Create(400, 200);
        window.Content = row;
        window.PerformLayout();
        return (window, plain, tipped, tip);
    }

    [TestMethod]
    public void ClosingTheToolTip_ReturnsItsContent()
    {
        if (SkipOnNonWindows()) return;

        var (window, plain, tipped, tip) = Host();

        window.SendMouseMove(plain.CenterOf());
        window.SendMouseMove(tipped.CenterOf());
        Assert.AreEqual(1, PopupCount(window), "the tooltip opened");

        window.SendMouseMove(plain.CenterOf());

        Assert.AreEqual(0, PopupCount(window), "leaving the button closed the tooltip");
        Assert.IsNull(tip.LogicalParent, "the closed tooltip kept the borrowed content");
    }

    [TestMethod]
    public void ClickThatClosesTheWindow_RoutesNothingMore()
    {
        if (SkipOnNonWindows()) return;

        var (window, plain, tipped, tip) = Host();

        // The teardown a native close runs, in its order, from inside the click as a modal loop would.
        tipped.Click += () =>
        {
            window.RaiseClosed();
            window.ClearMouseOverState();
            window.DisposeVisualTree();
        };

        window.SendMouseMove(plain.CenterOf());
        window.SendMouseMove(tipped.CenterOf());
        window.SendMouseDown(tipped.CenterOf());
        window.SendMouseUp(tipped.CenterOf());

        Assert.IsTrue(window.IsClosed, "the click closed the window");
        Assert.IsFalse(tipped.IsMouseOver, "the closed window hovered the button again");
        Assert.AreEqual(0, PopupCount(window), "the closed window opened a popup");
        Assert.IsNull(tip.LogicalParent, "the disposed tooltip kept the borrowed content");
    }
}
