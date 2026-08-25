using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Every navigation key must leave the caret visible, on both sides of the line virtualization
/// threshold (64K characters). Below it a line is laid out whole; above it the view works on
/// estimated slices, and every estimate-versus-measurement bug so far has lived on that side only.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextNavigationMatrixTests
{
    private const int BELOW_THRESHOLD = 10_000;
    private const int ABOVE_THRESHOLD = 200_000;
    private const string PATTERN = "iiiiWWMMlliii W";

    [TestMethod]
    [DataRow(BELOW_THRESHOLD)]
    [DataRow(ABOVE_THRESHOLD)]
    public void TextBox_EndAndHome_KeepTheCaretVisible(int lineLength)
    {
        if (SkipOffWindows()) return;

        var textBox = new TextBox().Width(300).Text(MakeUnevenText(lineLength));
        var window = HeadlessWindow.Create(400, 80);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 0;

        window.SendKeyPress(Key.End);
        AssertCaretInside(textBox.GetCharRectInWindow(textBox.CaretPosition), textBox.Bounds, "End");
        Assert.AreEqual(lineLength, textBox.CaretPosition);

        window.SendKeyPress(Key.Home);
        AssertCaretInside(textBox.GetCharRectInWindow(textBox.CaretPosition), textBox.Bounds, "Home");
        Assert.AreEqual(0, textBox.CaretPosition);
        Assert.AreEqual(0.0, textBox.HorizontalOffset, 0.01);
    }

    [TestMethod]
    [DataRow(BELOW_THRESHOLD)]
    [DataRow(ABOVE_THRESHOLD)]
    public void MultiLine_NoWrap_NavigationKeysKeepTheCaretVisible(int lineLength)
    {
        if (SkipOffWindows()) return;

        string text = "short\n" + MakeUnevenText(lineLength) + "\nshort";
        var textBox = CreateMultiLine(text, wrap: false, out var window);

        // End of the long middle line, which is also the widest line and so defines the extent.
        textBox.CaretPosition = 6;
        window.SendKeyPress(Key.End);
        AssertCaretInside(
            textBox.GetCharRectInWindow(textBox.CaretPosition), textBox.TextViewportBounds, "End");
        Assert.AreEqual(6 + lineLength, textBox.CaretPosition);

        window.SendKeyPress(Key.Home);
        AssertCaretInside(
            textBox.GetCharRectInWindow(textBox.CaretPosition), textBox.TextViewportBounds, "Home");
        Assert.AreEqual(0.0, textBox.HorizontalOffset, 0.01);

        window.SendKeyPress(Key.End, ModifierKeys.Control);
        AssertCaretInside(
            textBox.GetCharRectInWindow(textBox.CaretPosition), textBox.TextViewportBounds, "Ctrl+End");
        Assert.AreEqual(text.Length, textBox.CaretPosition);

        window.SendKeyPress(Key.Home, ModifierKeys.Control);
        AssertCaretInside(
            textBox.GetCharRectInWindow(textBox.CaretPosition), textBox.TextViewportBounds, "Ctrl+Home");
        Assert.AreEqual(0.0, textBox.VerticalOffset, 0.01);
        Assert.AreEqual(0.0, textBox.HorizontalOffset, 0.01);
    }

    [TestMethod]
    [DataRow(BELOW_THRESHOLD)]
    [DataRow(ABOVE_THRESHOLD)]
    public void MultiLine_Wrap_NavigationKeysKeepTheCaretVisible(int lineLength)
    {
        if (SkipOffWindows()) return;

        // One huge logical line: wrapped, it becomes thousands of visual rows whose heights are
        // estimated until visited.
        var textBox = CreateMultiLine(MakeUnevenText(lineLength), wrap: true, out var window);

        textBox.CaretPosition = 0;
        window.SendKeyPress(Key.End);
        AssertCaretInside(
            textBox.GetCharRectInWindow(textBox.CaretPosition), textBox.TextViewportBounds, "End");
        Assert.AreEqual(lineLength, textBox.CaretPosition);

        double atEnd = textBox.VerticalOffset;
        window.SendKeyPress(Key.PageDown);
        Assert.AreEqual(atEnd, textBox.VerticalOffset, 0.01,
            "PageDown after End found more document, so End stopped short.");

        window.SendKeyPress(Key.Home, ModifierKeys.Control);
        AssertCaretInside(
            textBox.GetCharRectInWindow(textBox.CaretPosition), textBox.TextViewportBounds, "Ctrl+Home");
        Assert.AreEqual(0, textBox.CaretPosition);
        Assert.AreEqual(0.0, textBox.VerticalOffset, 0.01);
    }

    [TestMethod]
    [DataRow(BELOW_THRESHOLD)]
    [DataRow(ABOVE_THRESHOLD)]
    public void MultiLine_NoWrap_PagingThroughALongLineDocumentStaysVisible(int lineLength)
    {
        if (SkipOffWindows()) return;

        string text = string.Join("\n",
            Enumerable.Range(0, 40).Select(line => line == 20 ? MakeUnevenText(lineLength) : $"line {line}"));
        var textBox = CreateMultiLine(text, wrap: false, out var window);
        textBox.CaretPosition = 0;

        for (int press = 0; press < 40 && textBox.CaretPosition < text.Length; press++)
        {
            window.SendKeyPress(Key.PageDown);
            AssertCaretInside(
                textBox.GetCharRectInWindow(textBox.CaretPosition),
                textBox.TextViewportBounds,
                $"PageDown #{press + 1}");
        }
        Assert.AreEqual(text.Length, textBox.CaretPosition, "Paging never reached the document end.");
    }

    [TestMethod]
    [DataRow(BELOW_THRESHOLD)]
    [DataRow(ABOVE_THRESHOLD)]
    public void MultiLine_NoWrap_ClickInAScrolledLineLandsWhereClicked(int lineLength)
    {
        if (SkipOffWindows()) return;

        // Inherent snap: a hit inside a glyph resolves to its boundary, so one widest glyph is the
        // tolerance. More than that means the caret and the drawn text disagree on coordinates.
        const double WIDEST_CHARACTER = 13;

        var textBox = CreateMultiLine(MakeUnevenText(lineLength), wrap: false, out var window);
        textBox.CaretPosition = lineLength / 2;
        textBox.ScrollToCaret();

        var viewport = textBox.TextViewportBounds;
        foreach (double fraction in new[] { 0.2, 0.5, 0.8 })
        {
            var click = new Point(
                viewport.X + viewport.Width * fraction,
                viewport.Y + 5);
            window.SendClick(click);

            Rect caret = textBox.GetCharRectInWindow(textBox.CaretPosition);
            Assert.IsLessThan(WIDEST_CHARACTER + 0.5, Math.Abs(caret.X - click.X),
                $"Clicking {click.X:F1} put the caret at {caret.X:F1}.");
        }
    }

    [TestMethod]
    [DataRow(BELOW_THRESHOLD)]
    [DataRow(ABOVE_THRESHOLD)]
    public void MultiLine_NoWrap_TypingAtTheEndKeepsTheCaretVisible(int lineLength)
    {
        if (SkipOffWindows()) return;

        var textBox = CreateMultiLine(MakeUnevenText(lineLength), wrap: false, out var window);
        textBox.CaretPosition = 0;
        window.SendKeyPress(Key.End);

        for (int keystroke = 0; keystroke < 5; keystroke++)
        {
            textBox.ReplaceSelection("X");
            AssertCaretInside(
                textBox.GetCharRectInWindow(textBox.CaretPosition),
                textBox.TextViewportBounds,
                $"typing #{keystroke + 1}");
        }
        Assert.AreEqual(lineLength + 5, textBox.CaretPosition);
    }

    [TestMethod]
    [DataRow(BELOW_THRESHOLD)]
    [DataRow(ABOVE_THRESHOLD)]
    public void MultiLine_NoWrap_EndIsIdempotentAcrossHomeRoundTrips(int lineLength)
    {
        if (SkipOffWindows()) return;

        var textBox = CreateMultiLine(MakeUnevenText(lineLength), wrap: false, out var window);
        textBox.CaretPosition = 0;

        window.SendKeyPress(Key.End);
        double firstEnd = textBox.HorizontalOffset;

        window.SendKeyPress(Key.Home);
        window.SendKeyPress(Key.End);

        // The second visit works from refined estimates; landing somewhere else would read as the
        // view sliding under a key that did not move the caret.
        Assert.AreEqual(firstEnd, textBox.HorizontalOffset, 0.5,
            "End landed at a different scroll offset on the second visit.");
    }

    [TestMethod]
    [DataRow(BELOW_THRESHOLD)]
    [DataRow(ABOVE_THRESHOLD)]
    public void MultiLine_NoWrap_ClickAtTheFarRightOfTheScrolledEndHitsTheLastCharacter(int lineLength)
    {
        if (SkipOffWindows()) return;

        var textBox = CreateMultiLine(MakeUnevenText(lineLength), wrap: false, out var window);
        textBox.CaretPosition = 0;
        window.SendKeyPress(Key.End);

        // The control's own right edge, outside the text viewport: a click on the border padding
        // must still resolve into the row and land on its last character.
        window.SendClick(new Point(textBox.Bounds.Right - 1, textBox.TextViewportBounds.Y + 5));

        Assert.IsGreaterThanOrEqualTo(lineLength - 1, textBox.CaretPosition,
            $"Clicking the far right landed at {textBox.CaretPosition} of {lineLength}.");
    }

    [TestMethod]
    [DataRow(BELOW_THRESHOLD)]
    [DataRow(ABOVE_THRESHOLD)]
    public void MultiLine_NoWrap_DragSelectingPastTheRightEdgeScrolls(int lineLength)
    {
        if (SkipOffWindows()) return;

        var textBox = CreateMultiLine(MakeUnevenText(lineLength), wrap: false, out var window);
        var viewport = textBox.TextViewportBounds;
        var inside = new Point(viewport.X + 20, viewport.Y + 5);
        var pastRight = new Point(viewport.Right + 30, viewport.Y + 5);

        window.SendMouseDown(inside);
        window.SendMouseDrag(pastRight);
        window.SendMouseDrag(pastRight);
        double scrolled = textBox.HorizontalOffset;
        window.SendMouseUp(pastRight);

        Assert.IsGreaterThan(0.0, scrolled,
            "Dragging past the right edge must scroll the view, as it does in TextBox.");
        Assert.IsGreaterThan(0, textBox.SelectionLength);
    }

    [TestMethod]
    [DataRow(BELOW_THRESHOLD)]
    [DataRow(ABOVE_THRESHOLD)]
    public void TextBox_TypingAtTheEndKeepsTheCaretVisible(int lineLength)
    {
        if (SkipOffWindows()) return;

        var textBox = new TextBox().Width(300).Text(MakeUnevenText(lineLength));
        var window = HeadlessWindow.Create(400, 80);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 0;
        window.SendKeyPress(Key.End);

        for (int keystroke = 0; keystroke < 5; keystroke++)
        {
            textBox.ReplaceSelection("X");
            AssertCaretInside(
                textBox.GetCharRectInWindow(textBox.CaretPosition),
                textBox.Bounds,
                $"typing #{keystroke + 1}");
        }
        Assert.AreEqual(lineLength + 5, textBox.CaretPosition);
    }

    [TestMethod]
    public void TextBox_ShrinkingTheTextAfterEnd_PullsTheViewportBackToTheText()
    {
        if (SkipOffWindows()) return;

        var textBox = new TextBox().Width(300).Text(MakeUnevenText(ABOVE_THRESHOLD));
        var window = HeadlessWindow.Create(400, 80);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 0;
        window.SendKeyPress(Key.End);

        // Replacing the text leaves the scroll offset pointing far past the new extent unless the
        // viewport re-clamps it.
        textBox.Text = MakeUnevenText(BELOW_THRESHOLD);
        window.PerformLayout();

        AssertCaretInside(
            textBox.GetCharRectInWindow(textBox.CaretPosition), textBox.Bounds, "shrink");
    }

    [TestMethod]
    public void MultiLine_ShrinkingTheTextAfterEnd_PullsTheViewportBackToTheText()
    {
        if (SkipOffWindows()) return;

        var textBox = CreateMultiLine(MakeUnevenText(ABOVE_THRESHOLD), wrap: false, out var window);
        textBox.CaretPosition = 0;
        window.SendKeyPress(Key.End);

        textBox.Text = MakeUnevenText(BELOW_THRESHOLD);
        window.PerformLayout();

        AssertCaretInside(
            textBox.GetCharRectInWindow(textBox.CaretPosition), textBox.TextViewportBounds, "shrink");
    }

    private static bool SkipOffWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return true;
        }
        return false;
    }

    private static string MakeUnevenText(int length)
        => string.Create(length, 0, static (span, _) =>
        {
            for (int index = 0; index < span.Length; index++)
            {
                span[index] = PATTERN[index % PATTERN.Length];
            }
        });

    private static MultiLineTextBox CreateMultiLine(string text, bool wrap, out Window window)
    {
        var textBox = new MultiLineTextBox().Width(300).Height(120).Wrap(wrap).Text(text);
        window = HeadlessWindow.Create(400, 160);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        return textBox;
    }

    private static void AssertCaretInside(Rect caret, Rect viewport, string action)
    {
        Assert.IsLessThanOrEqualTo(viewport.Right + 0.5, caret.Right,
            $"{action}: caret right {caret.Right:F1} is past the viewport right {viewport.Right:F1}.");
        Assert.IsGreaterThanOrEqualTo(viewport.X - 0.5, caret.X,
            $"{action}: caret left {caret.X:F1} is before the viewport left {viewport.X:F1}.");
        Assert.IsLessThanOrEqualTo(viewport.Bottom + 0.5, caret.Bottom,
            $"{action}: caret bottom {caret.Bottom:F1} is past the viewport bottom {viewport.Bottom:F1}.");
        Assert.IsGreaterThanOrEqualTo(viewport.Y - 0.5, caret.Y,
            $"{action}: caret top {caret.Y:F1} is above the viewport top {viewport.Y:F1}.");
    }
}
