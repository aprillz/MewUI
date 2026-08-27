using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A popup is anchored to the element that opened it, so only movement of that element dismisses it:
/// content growing under an unrelated list, or a list scrolling on the other side of the window, must
/// leave it alone.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ScrollPopupCloseTests
{
    private static int PopupCount(Window window)
    {
        var manager = typeof(Window).GetField("_popupManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        return (int)manager.GetType().GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
    }

    private static ScrollViewer CreateScroller(out Border anchor, double contentHeight = 2000)
    {
        anchor = new Border { Width = 100, Height = 40 };
        var content = new StackPanel { Height = contentHeight };
        content.Add(anchor);
        return new ScrollViewer { Content = content, Width = 200, Height = 100 };
    }

    private static Border ShowPopupOn(Window window, UIElement owner)
    {
        var popup = new Border { Width = 80, Height = 60 };
        window.ShowPopup(owner, popup, _ => new Rect(0, 0, 80, 60));
        window.PerformLayout();
        Assert.AreEqual(1, PopupCount(window), "the popup opened");
        return popup;
    }

    [TestMethod]
    public void GrowingContentKeepsThePopupOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scroller = CreateScroller(out var anchor);
        var window = HeadlessWindow.Create(400, 300);
        window.Content = scroller;
        window.PerformLayout();

        ShowPopupOn(window, anchor);

        ((StackPanel)scroller.Content!).Height = 4000;
        window.PerformLayout();

        Assert.AreEqual(0, scroller.VerticalOffset, "the test grew the extent without scrolling");
        Assert.AreEqual(1, PopupCount(window), "extent growth is not scrolling and must not close the popup");
    }

    [TestMethod]
    public void ScrollingAnUnrelatedViewerKeepsThePopupOpen()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var owning = CreateScroller(out var anchor);
        var unrelated = CreateScroller(out _);
        var root = new StackPanel();
        root.Add(owning);
        root.Add(unrelated);

        var window = HeadlessWindow.Create(400, 400);
        window.Content = root;
        window.PerformLayout();

        ShowPopupOn(window, anchor);

        unrelated.ScrollBy(3);
        window.PerformLayout();

        Assert.AreNotEqual(0, unrelated.VerticalOffset, "the unrelated viewer actually scrolled");
        Assert.AreEqual(1, PopupCount(window), "a viewer the popup owner is not inside must not close it");
    }

    [TestMethod]
    public void ScrollingTheOwningViewerClosesThePopup()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var scroller = CreateScroller(out var anchor);
        var window = HeadlessWindow.Create(400, 300);
        window.Content = scroller;
        window.PerformLayout();

        ShowPopupOn(window, anchor);

        scroller.ScrollBy(3);
        window.PerformLayout();

        Assert.AreNotEqual(0, scroller.VerticalOffset, "the owning viewer actually scrolled");
        Assert.AreEqual(0, PopupCount(window), "the owner moved out from under its popup");
    }

    [TestMethod]
    public void DetachingTheOwnerClosesThePopup()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var anchor = new Border { Width = 100, Height = 40 };
        var root = new StackPanel();
        root.Add(anchor);

        var window = HeadlessWindow.Create(400, 300);
        window.Content = root;
        window.PerformLayout();

        ShowPopupOn(window, anchor);

        root.Remove(anchor);
        window.PerformLayout();

        Assert.AreEqual(0, PopupCount(window), "a popup cannot outlive the element it is anchored to");
    }
}
