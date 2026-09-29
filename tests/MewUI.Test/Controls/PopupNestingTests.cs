using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A popup opened from inside another popup closes with it, whichever path closes the parent and whether
/// or not the child stays open by policy. An element inside the most recently opened interactive popup
/// shows its tooltip; elements anywhere else stay quiet while that popup is up.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PopupNestingTests
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

    private static object Manager(Window window)
        => typeof(Window).GetField("_popupManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;

    private static int PopupCount(Window window)
    {
        var manager = Manager(window);
        return (int)manager.GetType().GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
    }

    private static UIElement PopupElementAt(Window window, int index)
    {
        var manager = Manager(window);
        var method = manager.GetType().GetMethod("ElementAt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (UIElement)method.Invoke(manager, [index])!;
    }

    private static bool IsToolTipOpen(Window window)
    {
        for (int index = 0; index < PopupCount(window); index++)
        {
            if (PopupElementAt(window, index) is ToolTip)
            {
                return true;
            }
        }

        return false;
    }

    private static Button TippedButton(string text)
        => new() { Width = 60, Height = 24, Content = new TextBlock { Text = text }, ToolTip = new TextBlock { Text = text + " tip" } };

    /// <summary>A panel holding one button, opened as a popup at <paramref name="bounds"/>.</summary>
    private static Button OpenPanel(Window window, UIElement owner, Rect bounds, bool staysOpen = false)
    {
        var inner = new Button { Width = 40, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var panel = new StackPanel { Width = bounds.Width, Height = bounds.Height };
        panel.Add(inner);
        window.ShowPopup(owner, panel, _ => bounds, staysOpen: staysOpen);
        window.PerformLayout();
        return inner;
    }

    private static (Window window, Border anchor, Button outside) HostAnchor()
    {
        var anchor = new Border { Width = 60, Height = 24 };
        var outside = TippedButton("Outside");
        var root = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        root.Add(anchor);
        root.Add(outside);

        var window = HeadlessWindow.Create(800, 600);
        window.Content = root;
        window.PerformLayout();
        return (window, anchor, outside);
    }

    [TestMethod]
    public void ClosingTheParent_ClosesTheChild()
    {
        if (SkipOnNonWindows()) return;

        var (window, anchor, _) = HostAnchor();
        var inner = OpenPanel(window, anchor, new Rect(200, 200, 100, 60));
        OpenPanel(window, inner, new Rect(320, 200, 60, 60));
        Assert.AreEqual(2, PopupCount(window), "both popups opened");

        window.ClosePopup(PopupElementAt(window, 0));

        Assert.AreEqual(0, PopupCount(window), "the child outlived the popup it was opened from");
    }

    [TestMethod]
    public void ScrollClosingTheParent_ClosesTheChild()
    {
        if (SkipOnNonWindows()) return;

        var anchor = new Border { Width = 100, Height = 40 };
        var content = new StackPanel { Height = 2000 };
        content.Add(anchor);
        var scroller = new ScrollViewer { Content = content, Width = 200, Height = 100 };

        var window = HeadlessWindow.Create(800, 600);
        window.Content = scroller;
        window.PerformLayout();

        var inner = OpenPanel(window, anchor, new Rect(300, 200, 100, 60));
        OpenPanel(window, inner, new Rect(420, 200, 60, 60));
        Assert.AreEqual(2, PopupCount(window), "both popups opened");

        scroller.ScrollBy(3);
        window.PerformLayout();

        Assert.AreNotEqual(0, scroller.VerticalOffset, "the owning viewer actually scrolled");
        Assert.AreEqual(0, PopupCount(window), "the child outlived the popup the scroll closed");
    }

    [TestMethod]
    public void OutsidePressClosingTheParent_ClosesAChildThatStaysOpen()
    {
        if (SkipOnNonWindows()) return;

        var (window, anchor, _) = HostAnchor();
        var inner = OpenPanel(window, anchor, new Rect(200, 200, 100, 60));
        OpenPanel(window, inner, new Rect(320, 200, 60, 60), staysOpen: true);
        Assert.AreEqual(2, PopupCount(window), "both popups opened");

        window.SendMouseDown(new Point(700, 500));
        window.SendMouseUp(new Point(700, 500));

        Assert.AreEqual(0, PopupCount(window), "a child that stays open by policy outlived the popup it was opened from");
    }

    [TestMethod]
    public void FocusMovingAway_ClosesBoth()
    {
        if (SkipOnNonWindows()) return;

        var (window, anchor, outside) = HostAnchor();
        var inner = OpenPanel(window, anchor, new Rect(200, 200, 100, 60));
        OpenPanel(window, inner, new Rect(320, 200, 60, 60));
        Assert.AreEqual(2, PopupCount(window), "both popups opened");

        window.FocusManager.SetFocus(outside);

        Assert.AreEqual(0, PopupCount(window), "focus left both popups");
    }

    [TestMethod]
    public void OwnerLeavingTheWindow_ClosesChildAndGrandchild()
    {
        if (SkipOnNonWindows()) return;

        var (window, anchor, _) = HostAnchor();
        var inner = OpenPanel(window, anchor, new Rect(200, 200, 100, 60));
        var nested = OpenPanel(window, inner, new Rect(320, 200, 100, 60));
        OpenPanel(window, nested, new Rect(440, 200, 60, 60));
        Assert.AreEqual(3, PopupCount(window), "three popups opened");

        window.Content = new Border();
        window.PerformLayout();

        Assert.AreEqual(0, PopupCount(window), "popups opened from inside a closed popup stayed up");
    }

    [TestMethod]
    public void ClosingAllPopups_LeavesNone()
    {
        if (SkipOnNonWindows()) return;

        var (window, anchor, _) = HostAnchor();
        var inner = OpenPanel(window, anchor, new Rect(200, 200, 100, 60));
        OpenPanel(window, inner, new Rect(320, 200, 60, 60), staysOpen: true);

        window.CloseAllPopups();

        Assert.AreEqual(0, PopupCount(window));
    }

    [TestMethod]
    public void ClosingARootMenuWithItsSubMenuOpen_ClosesBoth()
    {
        if (SkipOnNonWindows()) return;

        var (window, anchor, _) = HostAnchor();
        var menu = new ContextMenu();
        menu.AddSubMenu("Parent", new Menu().Item("Sub A").Item("Sub B"));
        menu.AddItem("Other");
        menu.Show(anchor, new Point(100, 100));
        window.PerformLayout();

        var bounds = menu.Bounds;
        window.SendMouseMove(new Point(bounds.X + bounds.Width / 2, bounds.Y + 12));
        window.PerformLayout();
        Assert.AreEqual(2, PopupCount(window), "the submenu opened");

        window.ClosePopup(menu);

        Assert.AreEqual(0, PopupCount(window), "closing the root menu left its submenu up");
    }

    [TestMethod]
    public void ToolTip_InsideAPopup_Opens()
    {
        if (SkipOnNonWindows()) return;

        var (window, anchor, _) = HostAnchor();
        var tipped = TippedButton("Inside");
        var panel = new StackPanel { Width = 100, Height = 60 };
        panel.Add(tipped);
        window.ShowPopup(anchor, panel, _ => new Rect(200, 200, 100, 60));
        window.PerformLayout();

        window.SendMouseMove(new Point(700, 500));
        window.SendMouseMove(tipped.CenterOf());

        Assert.IsTrue(tipped.IsMouseOver, "the pointer did enter the button");
        Assert.IsTrue(IsToolTipOpen(window), "an element inside the open popup showed no tooltip");
    }

    [TestMethod]
    public void ToolTip_OutsideAnOpenPopup_StaysClosed()
    {
        if (SkipOnNonWindows()) return;

        var (window, anchor, outside) = HostAnchor();
        OpenPanel(window, anchor, new Rect(200, 200, 100, 60));

        window.SendMouseMove(new Point(700, 500));
        window.SendMouseMove(outside.CenterOf());

        Assert.IsTrue(outside.IsMouseOver, "the pointer did enter the outside button");
        Assert.IsFalse(IsToolTipOpen(window), "an element outside the open popup showed its tooltip");
    }

    [TestMethod]
    public void ToolTip_EnteredWithTheButtonHeld_StaysClosed()
    {
        if (SkipOnNonWindows()) return;

        var (window, anchor, _) = HostAnchor();
        var first = new Border { Width = 60, Height = 24, Background = Color.Gray, ToolTip = new TextBlock { Text = "First" } };
        var second = new Border { Width = 60, Height = 24, Background = Color.Gray, ToolTip = new TextBlock { Text = "Second" } };
        var gap = new Border { Width = 60, Height = 24, Background = Color.White };
        var panel = new StackPanel { Width = 100, Height = 90 };
        panel.Add(first);
        panel.Add(gap);
        panel.Add(second);
        window.ShowPopup(anchor, panel, _ => new Rect(200, 200, 100, 90));
        window.PerformLayout();

        window.SendMouseMove(new Point(700, 500));
        window.SendMouseMove(gap.CenterOf());
        window.SendMouseDrag(second.CenterOf());

        Assert.IsTrue(second.IsMouseOver, "the pointer did enter the second element");
        Assert.IsFalse(IsToolTipOpen(window), "entering with the left button held opened the tooltip");

        window.SendMouseMove(gap.CenterOf());
        window.SendMouseMove(first.CenterOf());

        Assert.IsTrue(IsToolTipOpen(window), "a plain enter inside the popup opened no tooltip");
    }

    private static (Window window, ToolBar bar, ToolBar.GroupVisual group, ScrollViewer scroller, Button outside) HostToolBar(params Element[] items)
    {
        var bar = new ToolBar { Width = 900 };
        bar.Bands.Add(new ToolBarBand(new ToolBarGroup().Items(items)));

        var outside = TippedButton("Outside");
        var content = new StackPanel { Height = 2000 };
        content.Add(bar);
        content.Add(outside);
        var scroller = new ScrollViewer { Content = content, Width = 780, Height = 400 };

        var window = HeadlessWindow.Create(800, 600);
        window.Content = scroller;
        window.PerformLayout();

        var group = bar.VisualsInternal[0].Groups[0];
        var entries = group.Entries;
        bar.Width = ((UIElement)entries[1]).Bounds.X - bar.Bounds.X;
        window.PerformLayout();

        Assert.IsTrue(group.IsTruncated, "the band cut no entries");
        return (window, bar, group, scroller, outside);
    }

    private static void OpenOverflow(Window window, ToolBar bar, ToolBar.GroupVisual group)
    {
        group.OpenOverflow(bar);
        window.PerformLayout();
        Assert.IsTrue(group.OverflowContent.IsOpen, "the overflow popup did not open");
    }

    [TestMethod]
    public void ToolTip_OnABorrowedOverflowEntry_OpensAtThePointer()
    {
        if (SkipOnNonWindows()) return;

        var first = TippedButton("First");
        var second = TippedButton("Second");
        var third = TippedButton("Third");
        var (window, bar, group, _, _) = HostToolBar(first, second, third);
        OpenOverflow(window, bar, group);
        Assert.Contains(second, group.OverflowContent.Items, "the cut entry is not in the popup");

        var pointer = second.CenterOf();
        window.SendMouseMove(new Point(700, 500));
        window.SendMouseMove(pointer);

        Assert.IsTrue(IsToolTipOpen(window), "the borrowed entry showed no tooltip");

        window.PerformLayout();
        var tip = PopupElementAt(window, PopupCount(window) - 1);
        Assert.AreEqual(pointer.X + 12, tip.Bounds.X, 1.0, "the tooltip is not beside the pointer");
        Assert.AreEqual(pointer.Y + 18, tip.Bounds.Y, 1.0, "the tooltip is not below the pointer");
    }

    [TestMethod]
    public void ToolTip_WhileADropDownInTheOverflowIsOpen_WaitsForItToClose()
    {
        if (SkipOnNonWindows()) return;

        var first = TippedButton("First");
        var second = TippedButton("Second");
        var dropDown = new DropDownButton { Width = 60, Height = 24, Content = new TextBlock { Text = "More" }, DropDownMenu = new Menu().Item("A").Item("B") };
        var (window, bar, group, _, _) = HostToolBar(first, second, dropDown);
        OpenOverflow(window, bar, group);
        Assert.Contains(dropDown, group.OverflowContent.Items, "the drop-down is not in the popup");

        dropDown.IsDropDownOpen = true;
        window.PerformLayout();
        Assert.AreEqual(2, PopupCount(window), "the drop-down menu opened over the overflow popup");

        window.SendMouseMove(new Point(700, 500));
        window.SendMouseMove(second.CenterOf());
        Assert.IsFalse(IsToolTipOpen(window), "an overflow entry showed its tooltip under the open menu");

        dropDown.IsDropDownOpen = false;
        window.PerformLayout();
        Assert.AreEqual(1, PopupCount(window), "closing the menu kept the overflow popup");

        window.SendMouseMove(new Point(700, 500));
        window.SendMouseMove(second.CenterOf());
        Assert.IsTrue(IsToolTipOpen(window), "the overflow entry showed no tooltip once the menu closed");
    }

    [TestMethod]
    public void OutsidePress_ClosesTheOverflowAndItsDropDown()
    {
        if (SkipOnNonWindows()) return;

        var first = TippedButton("First");
        var second = TippedButton("Second");
        var dropDown = new DropDownButton { Width = 60, Height = 24, Content = new TextBlock { Text = "More" }, DropDownMenu = new Menu().Item("A").Item("B") };
        var (window, bar, group, _, _) = HostToolBar(first, second, dropDown);
        OpenOverflow(window, bar, group);

        dropDown.IsDropDownOpen = true;
        window.PerformLayout();
        Assert.AreEqual(2, PopupCount(window), "the drop-down menu opened and the overflow popup stayed");

        window.SendMouseDown(new Point(700, 500));
        window.SendMouseUp(new Point(700, 500));

        Assert.AreEqual(0, PopupCount(window), "an outside press left a popup up");
    }

    [TestMethod]
    [DataRow("explicit")]
    [DataRow("press")]
    [DataRow("focus")]
    [DataRow("scroll")]
    public void ClosingTheOverflow_ClosesTheToolTipOfItsEntry(string path)
    {
        if (SkipOnNonWindows()) return;

        var first = TippedButton("First");
        var second = TippedButton("Second");
        var third = TippedButton("Third");
        var (window, bar, group, scroller, outside) = HostToolBar(first, second, third);
        OpenOverflow(window, bar, group);

        window.SendMouseMove(new Point(700, 500));
        window.SendMouseMove(second.CenterOf());
        Assert.IsTrue(IsToolTipOpen(window), "the borrowed entry showed no tooltip");

        switch (path)
        {
            case "explicit":
                group.OverflowContent.Close();
                break;
            case "press":
                window.SendMouseDown(new Point(700, 500));
                window.SendMouseUp(new Point(700, 500));
                break;
            case "focus":
                window.FocusManager.SetFocus(outside);
                break;
            case "scroll":
                scroller.ScrollBy(3);
                break;
        }

        window.PerformLayout();
        Assert.AreEqual(0, PopupCount(window), $"closing the overflow by {path} left a popup up");
    }

    [TestMethod]
    public void ReopeningTheOverflow_ShowsTheToolTipAgain()
    {
        if (SkipOnNonWindows()) return;

        var first = TippedButton("First");
        var second = TippedButton("Second");
        var third = TippedButton("Third");
        var (window, bar, group, _, _) = HostToolBar(first, second, third);
        OpenOverflow(window, bar, group);

        window.SendMouseMove(new Point(700, 500));
        window.SendMouseMove(second.CenterOf());
        Assert.IsTrue(IsToolTipOpen(window), "the borrowed entry showed no tooltip");

        group.OverflowContent.Close();
        window.PerformLayout();
        Assert.AreEqual(0, PopupCount(window), "the overflow closed");

        OpenOverflow(window, bar, group);
        window.SendMouseMove(second.CenterOf());

        Assert.IsTrue(IsToolTipOpen(window), "the entry showed no tooltip the second time the overflow opened");
    }
}
