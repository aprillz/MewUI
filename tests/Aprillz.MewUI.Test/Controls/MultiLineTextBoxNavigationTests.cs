using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class MultiLineTextBoxNavigationTests
{
    [TestMethod]
    public void WrappedCaretBoundary_IsOwnedByOnlyOneVisualRow()
    {
        Assert.IsFalse(MultiLineTextBox.IsCaretOwnedByWrappedRow(
            caretColumn: 5,
            segmentStart: 0,
            segmentEnd: 5,
            isLastRow: false));
        Assert.IsTrue(MultiLineTextBox.IsCaretOwnedByWrappedRow(
            caretColumn: 5,
            segmentStart: 5,
            segmentEnd: 10,
            isLastRow: false));
        Assert.IsTrue(MultiLineTextBox.IsCaretOwnedByWrappedRow(
            caretColumn: 10,
            segmentStart: 5,
            segmentEnd: 10,
            isLastRow: true));
    }

    [TestMethod]
    public void ArrowKeys_WithWrap_MoveBetweenVisualRows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = new MultiLineTextBox()
            .Width(140)
            .Height(120)
            .Wrap()
            .Text(new string('W', 120));
        var window = HeadlessWindow.Create(140, 120);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 1;

        Rect initialRect = textBox.GetCharRectInWindow(textBox.CaretPosition);

        window.SendKeyPress(Key.Down);
        int firstDownPosition = textBox.CaretPosition;
        Rect firstDownRect = textBox.GetCharRectInWindow(firstDownPosition);

        window.SendKeyPress(Key.Down);
        int secondDownPosition = textBox.CaretPosition;
        Rect secondDownRect = textBox.GetCharRectInWindow(secondDownPosition);

        window.SendKeyPress(Key.Up);

        Assert.IsGreaterThan(1, firstDownPosition);
        Assert.IsGreaterThan(firstDownPosition, secondDownPosition);
        Assert.AreEqual(initialRect.Y + initialRect.Height, firstDownRect.Y, 0.01);
        Assert.AreEqual(firstDownRect.Y + firstDownRect.Height, secondDownRect.Y, 0.01);
        Assert.AreEqual(firstDownPosition, textBox.CaretPosition);
    }

    [TestMethod]
    public void ShiftDown_WithWrap_ExtendsSelectionToNextVisualRow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = new MultiLineTextBox()
            .Width(140)
            .Height(120)
            .Wrap()
            .Text(new string('W', 120));
        var window = HeadlessWindow.Create(140, 120);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 1;

        window.SendKeyPress(Key.Down, ModifierKeys.Shift);

        Assert.AreEqual(1, textBox.SelectionStart);
        Assert.IsGreaterThan(0, textBox.SelectionLength);
        Assert.AreEqual(textBox.CaretPosition - 1, textBox.SelectionLength);
    }

    [TestMethod]
    public void ArrowKeys_WithWrap_UseCurrentRowXAfterShortLogicalLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        const string text = "WWWWWW\nW\nWWWWWW";
        var textBox = new MultiLineTextBox()
            .Width(300)
            .Height(120)
            .Wrap()
            .Text(text);
        var window = HeadlessWindow.Create(300, 120);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 5;

        window.SendKeyPress(Key.Down);
        Assert.AreEqual(8, textBox.CaretPosition);

        window.SendKeyPress(Key.Down);

        Assert.AreEqual(10, textBox.CaretPosition);
    }

    [TestMethod]
    public void Down_WithNarrowTargetRow_DoesNotSkipVisualRow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        const string firstLine = "WWWWWWWW";
        string text = firstLine + "\n" + new string('i', 34) + "WWWWWWWW";
        var textBox = new MultiLineTextBox()
            .Width(140)
            .Height(120)
            .Wrap()
            .Text(text);
        var window = HeadlessWindow.Create(140, 120);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = firstLine.Length;
        Rect initialRect = textBox.GetCharRectInWindow(textBox.CaretPosition);

        window.SendKeyPress(Key.Down);

        Rect movedRect = textBox.GetCharRectInWindow(textBox.CaretPosition);
        Assert.AreEqual(initialRect.Y + initialRect.Height, movedRect.Y, 0.01);
    }

    [TestMethod]
    public void ArrowKeys_WithMixedGlyphWidths_AlwaysMoveOneVisualRow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        string text =
            new string('W', 18) + new string('i', 37) + new string('M', 13) + "\n" +
            new string('i', 29) + new string('W', 21) + new string('.', 41);
        var textBox = new MultiLineTextBox()
            .Width(140)
            .Height(400)
            .Wrap()
            .Text(text);
        var window = HeadlessWindow.Create(140, 400);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();

        double firstRowY = textBox.GetCharRectInWindow(0).Y;
        double lastRowY = textBox.GetCharRectInWindow(text.Length).Y;
        for (int position = 0; position <= text.Length; position++)
        {
            textBox.CaretPosition = position;
            Rect initialRect = textBox.GetCharRectInWindow(position);
            if (initialRect.Y < lastRowY)
            {
                window.SendKeyPress(Key.Down);

                Rect movedDownRect = textBox.GetCharRectInWindow(textBox.CaretPosition);
                Assert.AreEqual(
                    initialRect.Y + initialRect.Height,
                    movedDownRect.Y,
                    0.01,
                    $"Down from position {position} moved to {textBox.CaretPosition}");
            }

            if (initialRect.Y <= firstRowY)
            {
                continue;
            }

            textBox.CaretPosition = position;
            window.SendKeyPress(Key.Up);

            Rect movedUpRect = textBox.GetCharRectInWindow(textBox.CaretPosition);
            Assert.AreEqual(
                initialRect.Y - initialRect.Height,
                movedUpRect.Y,
                0.01,
                $"Up from position {position} moved to {textBox.CaretPosition}");
        }
    }

    [TestMethod]
    public void ArrowKeys_WithinWrappedLogicalLine_RoundTripToOriginalCaret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        string text =
            new string('W', 18) +
            new string('i', 37) +
            new string('M', 13) +
            new string('.', 41);
        var textBox = new MultiLineTextBox()
            .Width(140)
            .Height(400)
            .Wrap()
            .Text(text);
        var window = HeadlessWindow.Create(140, 400);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();

        double lastRowY = textBox.GetCharRectInWindow(text.Length).Y;
        for (int position = 0; position <= text.Length; position++)
        {
            textBox.CaretPosition = position;
            Rect initialRect = textBox.GetCharRectInWindow(position);
            if (initialRect.Y >= lastRowY)
            {
                continue;
            }

            for (int cycle = 0; cycle < 3; cycle++)
            {
                window.SendKeyPress(Key.Down);
                window.SendKeyPress(Key.Up);

                Assert.AreEqual(
                    position,
                    textBox.CaretPosition,
                    $"Down/Up cycle {cycle + 1} from position {position} ended at {textBox.CaretPosition}");
            }
        }
    }

    [TestMethod]
    public void ArrowKeys_WithoutWrap_ContinueMovingBetweenLogicalLines()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        const string text = "WWWWWW\nW\nWWWWWW";
        var textBox = new MultiLineTextBox()
            .Width(140)
            .Height(120)
            .Wrap(false)
            .Text(text);
        var window = HeadlessWindow.Create(140, 120);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 5;

        window.SendKeyPress(Key.Down);
        Assert.AreEqual(8, textBox.CaretPosition);

        window.SendKeyPress(Key.Down);
        Assert.AreEqual(10, textBox.CaretPosition);

        window.SendKeyPress(Key.Up);
        Assert.AreEqual(8, textBox.CaretPosition);
    }

    [TestMethod]
    public void ArrowKeys_WithWrap_MoveThroughEmptyLogicalLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        const string text = "WWWW\n\nWWWW";
        var textBox = new MultiLineTextBox()
            .Width(300)
            .Height(120)
            .Wrap()
            .Text(text);
        var window = HeadlessWindow.Create(300, 120);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 4;

        window.SendKeyPress(Key.Down);
        Assert.AreEqual(5, textBox.CaretPosition);

        window.SendKeyPress(Key.Down);
        Assert.AreEqual(6, textBox.CaretPosition);

        window.SendKeyPress(Key.Up);
        Assert.AreEqual(5, textBox.CaretPosition);
    }
}
