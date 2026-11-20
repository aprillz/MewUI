using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Hiding a window takes its pointer state and popups with it: nothing under a hidden window stays hovered,
/// captured or shown, including when a click handler hides the window while its click is still being routed.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HiddenWindowInputTests
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
    public void HidingTheWindow_ClosesItsToolTipAndClearsHover()
    {
        if (SkipOnNonWindows()) return;

        var (window, plain, tipped) = Host();

        window.SendMouseMove(plain.CenterOf());
        window.SendMouseMove(tipped.CenterOf());
        Assert.AreEqual(1, PopupCount(window), "the tooltip opened");

        window.Hide();

        Assert.AreEqual(0, PopupCount(window), "the hidden window kept its tooltip open");
        Assert.IsFalse(tipped.IsMouseOver, "the hidden window kept the button hovered");
    }

    [TestMethod]
    public void HidingTheWindow_ReleasesTheCapture()
    {
        if (SkipOnNonWindows()) return;

        var (window, _, tipped) = Host();

        window.SendMouseMove(tipped.CenterOf());
        window.SendMouseDown(tipped.CenterOf());
        Assert.IsNotNull(window.CapturedElement, "the press captured the mouse");

        window.Hide();

        Assert.IsNull(window.CapturedElement, "the hidden window kept the mouse capture");
    }

    [TestMethod]
    public void ClickThatHidesTheWindow_OpensNoToolTip()
    {
        if (SkipOnNonWindows()) return;

        var (window, plain, tipped) = Host();
        tipped.Click += window.Hide;

        window.SendMouseMove(plain.CenterOf());
        window.SendMouseMove(tipped.CenterOf());
        window.SendMouseDown(tipped.CenterOf());
        window.SendMouseUp(tipped.CenterOf());

        Assert.IsFalse(tipped.IsMouseOver, "the hidden window hovered the button again");
        Assert.AreEqual(0, PopupCount(window), "the hidden window opened a tooltip");
    }
}
