using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.Text;
using MewUI.MewvalonEdit.Test.Infrastructure;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// Alt+drag draws a rectangular selection. The editor claims the press before the surface's own
/// drag selection starts, manages the capture itself, and grows the rectangle on every move.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RectangleMouseTests
{
    private static (Window window, TextEditor editor) CreateHost(string text)
    {
        var window = ScaledWindow.Create(1.0);
        var editor = new TextEditor
        {
            Text = text,
            SkipViewportCull = true,
            FontFamily = "Consolas",
            FontSize = 13
        };
        window.Content = editor;
        window.PerformLayout();
        return (window, editor);
    }

    private static Point PointAt(TextEditor editor, int line, int visualColumn)
    {
        var visualLine = editor.TextArea.TextView.GetOrConstructVisualLine(
            editor.Document.GetLineByNumber(line));
        Assert.IsNotNull(visualLine);
        double documentX = visualLine.GetVisualXPosition(visualColumn);
        double documentY = editor.TextArea.TextView.GetVisualTopByDocumentLine(line) + 2;
        ITextViewHost host = editor.Surface;
        var viewport = editor.Surface.TextViewportBounds;
        return new Point(
            viewport.X + documentX - host.ScrollOffset.X,
            viewport.Y + documentY - host.ScrollOffset.Y);
    }

    private static void Drag(Window window, Point from, Point to, ModifierKeys modifiers)
    {
        WindowInputRouter.MouseButton(window, from, from, MouseButton.Left,
            isDown: true, leftDown: true, rightDown: false, middleDown: false,
            clickCount: 1, modifiers);
        WindowInputRouter.MouseMove(window, to, to,
            leftDown: true, rightDown: false, middleDown: false, modifiers);
        WindowInputRouter.MouseButton(window, to, to, MouseButton.Left,
            isDown: false, leftDown: false, rightDown: false, middleDown: false,
            clickCount: 1, modifiers);
    }

    [TestMethod]
    public void AltDragDrawsARectangle()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, editor) = CreateHost("abcdef\nghijkl\nmnopqr");

        Drag(window, PointAt(editor, 1, 2), PointAt(editor, 3, 4), ModifierKeys.Alt);

        var selection = editor.TextArea.Selection;
        Assert.IsInstanceOfType<RectangleSelection>(selection);
        var segments = selection.Segments.ToArray();
        Assert.HasCount(3, segments);
        foreach (var segment in segments)
        {
            Assert.AreEqual(2, segment.StartVisualColumn);
            Assert.AreEqual(4, segment.EndVisualColumn);
        }
    }

    [TestMethod]
    public void APlainDragStaysASurfaceSelection()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, editor) = CreateHost("abcdef\nghijkl\nmnopqr");

        Drag(window, PointAt(editor, 1, 2), PointAt(editor, 2, 4), ModifierKeys.None);

        Assert.IsNotInstanceOfType<RectangleSelection>(editor.TextArea.Selection);
        Assert.IsGreaterThan(0, editor.SelectionLength, "The surface's own drag selection must still work.");
    }

    [TestMethod]
    public void AltDragIntoVirtualSpaceKeepsTheColumn()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var (window, editor) = CreateHost("abcdef\nab\nmnopqr");

        Drag(window, PointAt(editor, 1, 2), PointAt(editor, 2, 5), ModifierKeys.Alt);

        var selection = editor.TextArea.Selection;
        Assert.IsInstanceOfType<RectangleSelection>(selection);
        var segments = selection.Segments.ToArray();
        Assert.HasCount(2, segments);
        Assert.AreEqual(5, segments[1].EndVisualColumn,
            "The short line's segment must reach the dragged column virtually.");
        Assert.AreEqual(5, editor.TextArea.Caret.VisualColumn,
            "The caret follows the drag into virtual space.");
    }
}
