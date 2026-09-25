using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewvalonEdit;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// A scroll bar the editor always shows gets its own room, so the text and what a host draws over the text end where
/// the bar begins.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EditorScrollBarRoomTests
{
    [TestMethod]
    public void AnAlwaysShownBarLiesRightOfTheText()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (surface, bar) = LayOut(ScrollBarVisibility.Visible);

        Assert.IsTrue(bar.IsVisible);
        Assert.IsLessThanOrEqualTo(bar.Bounds.X, surface.TextViewportBounds.Right, "the text runs under the bar");
    }

    [TestMethod]
    public void TheBarsRoomGoesWhenItIsNoLongerAlwaysShown()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var (visibleSurface, _) = LayOut(ScrollBarVisibility.Visible);
        var (autoSurface, _) = LayOut(ScrollBarVisibility.Auto);

        Assert.IsGreaterThan(visibleSurface.TextViewportBounds.Width, autoSurface.TextViewportBounds.Width,
            "the text keeps the bar's room although the bar is not always shown");
    }

    private static (MultiLineTextBox Surface, ScrollBar Bar) LayOut(ScrollBarVisibility visibility)
    {
        var editor = new TextEditor { Text = "one\ntwo", VerticalScrollBarVisibility = visibility };
        var window = HeadlessWindow.Create(400, 300);
        window.Content = editor;
        window.PerformLayout();
        var surface = (MultiLineTextBox)VisualTree.Find(editor, element => element is MultiLineTextBox)!;
        var bar = (ScrollBar)VisualTree.Find(surface, element => element is ScrollBar { Orientation: Orientation.Vertical })!;
        return (surface, bar);
    }
}
