using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// The text of a multi-line box always scrolls; <see cref="MultiLineTextBox.VerticalScrollBarVisibility"/> only says
/// whether the bar is shown while there is nothing to scroll.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MultiLineTextBoxScrollBarTests
{
    private const string SHORT_TEXT = "one\ntwo";

    [TestMethod]
    public void AShortDocumentShowsNoBarByDefault()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var box = new MultiLineTextBox { Text = SHORT_TEXT, Height = 200 };
        var window = HeadlessWindow.Create(400, 300);
        window.Content = box;
        window.PerformLayout();

        Assert.AreEqual(ScrollBarVisibility.Auto, box.VerticalScrollBarVisibility);
        Assert.IsFalse(box.IsVerticalScrollBarVisible);
    }

    [TestMethod]
    public void VisibleShowsTheBarOverAShortDocument()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var box = new MultiLineTextBox { Text = SHORT_TEXT, Height = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
        var window = HeadlessWindow.Create(400, 300);
        window.Content = box;
        window.PerformLayout();

        Assert.IsTrue(box.IsVerticalScrollBarVisible, "the bar is shown although the document fits");
    }

    [TestMethod]
    public void AShownBarWithNothingToScrollLeavesTheWheelToTheParent()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var box = new MultiLineTextBox
        {
            Text = SHORT_TEXT,
            Height = 120,
            VerticalAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        };
        var page = new StackPanel().Children(box, new Border { Height = 2000 });
        var scroller = new ScrollViewer { Content = page };
        var window = HeadlessWindow.Create(400, 300);
        window.Content = scroller;
        window.PerformLayout();

        window.SendMouseWheel(new Point(box.Bounds.X + 40, box.Bounds.Y + 40), -1);
        window.PerformLayout();

        Assert.IsGreaterThan(0, scroller.VerticalOffset, "the box took a wheel it had nowhere to scroll with");
    }

    [TestMethod]
    public void VisibleOverALongDocumentScrollsAsAutoDoes()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var box = new MultiLineTextBox
        {
            Text = string.Join("\n", Enumerable.Range(1, 200).Select(index => $"line {index}")),
            Height = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        };
        var window = HeadlessWindow.Create(400, 300);
        window.Content = box;
        window.PerformLayout();

        window.SendMouseWheel(new Point(box.Bounds.X + 40, box.Bounds.Y + 40), -1);
        window.PerformLayout();

        Assert.IsTrue(box.IsVerticalScrollBarVisible);
        Assert.IsGreaterThan(0, box.VerticalOffset);
    }
}
