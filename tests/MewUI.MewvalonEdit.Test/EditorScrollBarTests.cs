using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewvalonEdit;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>The editor's vertical scroll bar setting reaches the surface that draws the bar.</summary>
[TestClass]
[DoNotParallelize]
public sealed class EditorScrollBarTests
{
    [TestMethod]
    public void AVisibleBarIsShownOverAShortDocument()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("The GDI backend is Windows-only."); return; }

        var editor = new TextEditor { Text = "one\ntwo", VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
        var window = HeadlessWindow.Create(400, 300);
        window.Content = editor;
        window.PerformLayout();

        var surface = VisualTree.Find(editor, element => element is MultiLineTextBox) as MultiLineTextBox;
        Assert.IsNotNull(surface);
        Assert.AreEqual(ScrollBarVisibility.Visible, surface.VerticalScrollBarVisibility);
        var bar = VisualTree.Find(surface, element => element is ScrollBar { Orientation: Orientation.Vertical }) as ScrollBar;
        Assert.IsNotNull(bar);
        Assert.IsTrue(bar.IsVisible, "the bar is shown although the document fits");
        Assert.IsFalse(bar.IsEnabled, "with nothing to scroll the bar is disabled");
    }
}
