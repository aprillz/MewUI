using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class MultiLineTextBoxNavigationTests
{
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
    public void PageDown_ScrollsOneViewportAndKeepsTheCaretOnItsScreenRow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = CreateScrollableTextBox(out var window);
        textBox.CaretPosition = 0;
        Rect initialRect = textBox.GetCharRectInWindow(0);

        window.SendKeyPress(Key.PageDown);

        int pagedPosition = textBox.CaretPosition;
        Assert.IsGreaterThan(0.0, textBox.VerticalOffset);
        Assert.IsGreaterThan(0, pagedPosition);
        Assert.AreEqual(initialRect.Y, textBox.GetCharRectInWindow(pagedPosition).Y, 0.01);

        window.SendKeyPress(Key.PageUp);

        Assert.AreEqual(0.0, textBox.VerticalOffset, 0.01);
        Assert.AreEqual(0, textBox.CaretPosition);
    }

    [TestMethod]
    public void PageUp_WithNothingLeftToScroll_MovesTheCaretToTheDocumentStart()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = CreateScrollableTextBox(out var window);
        textBox.CaretPosition = 20;

        window.SendKeyPress(Key.PageUp);

        Assert.AreEqual(0, textBox.CaretPosition);
    }

    [TestMethod]
    public void ShiftPageDown_ExtendsTheSelectionByAPage()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = CreateScrollableTextBox(out var window);
        textBox.CaretPosition = 0;

        window.SendKeyPress(Key.PageDown, ModifierKeys.Shift);

        Assert.AreEqual(0, textBox.SelectionStart);
        Assert.AreEqual(textBox.CaretPosition, textBox.SelectionLength);
    }

    [TestMethod]
    public void RepeatedPageUp_ReachesTheFirstLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = CreateScrollableTextBox(out var window);
        textBox.CaretPosition = textBox.Text.Length;

        for (int press = 0; press < 400; press++)
        {
            window.SendKeyPress(Key.PageUp);
            if (textBox.CaretPosition == 0)
            {
                break;
            }
        }

        Assert.AreEqual(0, textBox.CaretPosition);
        Assert.AreEqual(0.0, textBox.VerticalOffset, 0.01);
    }

    [TestMethod]
    public void RepeatedPageDown_ReachesTheLastLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = CreateScrollableTextBox(out var window);
        textBox.CaretPosition = 0;

        for (int press = 0; press < 400; press++)
        {
            window.SendKeyPress(Key.PageDown);
            if (textBox.CaretPosition == textBox.Text.Length)
            {
                break;
            }
        }

        Assert.AreEqual(textBox.Text.Length, textBox.CaretPosition);
    }

    [TestMethod]
    public void PrimaryEndAndHome_ScrollTheViewToTheCaret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = CreateScrollableTextBox(out var window);
        textBox.CaretPosition = 0;

        window.SendKeyPress(Key.End, ModifierKeys.Control);

        Assert.AreEqual(textBox.Text.Length, textBox.CaretPosition);
        Assert.IsGreaterThan(0.0, textBox.VerticalOffset, "The document end must be brought into view.");

        window.SendKeyPress(Key.Home, ModifierKeys.Control);

        Assert.AreEqual(0, textBox.CaretPosition);
        Assert.AreEqual(0.0, textBox.VerticalOffset, 0.01);
    }

    [TestMethod]
    public void PrimaryPageKeys_AreLeftForTheTabControl()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = CreateScrollableTextBox(out var window);
        textBox.CaretPosition = 0;

        window.SendKeyPress(Key.PageDown, ModifierKeys.Control);

        Assert.AreEqual(0, textBox.CaretPosition);
        Assert.AreEqual(0.0, textBox.VerticalOffset, 0.01);
    }

    [TestMethod]
    public void PrimaryEnd_OnAWrappedDocument_LandsAtTheSameEndAsScrolling()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // Unmaterialized lines are estimated at one row each, so a document whose lines wrap is
        // taller than the estimate until the tail is built.
        string text = string.Join("\n", Enumerable.Range(0, 5_000)
            .Select(line => $"line {line} " + new string('W', line % 7 == 0 ? 200 : 10)));
        var textBox = new MultiLineTextBox()
            .Width(300)
            .Height(120)
            .Wrap()
            .Text(text);
        var window = HeadlessWindow.Create(300, 120);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 0;

        window.SendKeyPress(Key.End, ModifierKeys.Control);
        double afterJump = textBox.VerticalOffset;

        window.SendKeyPress(Key.PageDown);

        Assert.AreEqual(afterJump, textBox.VerticalOffset, 0.01,
            "Paging after the jump found more document, so the jump stopped short of the end.");
    }

    [TestMethod]
    public void CaretAtTheEndOfTheWidestLine_StaysInsideTheViewport()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // The widest line defines the scroll extent, so a caret at its end sits exactly on the
        // right edge unless the extent leaves room for it.
        string text = "short\n" + new string('W', 400) + "\nshort";
        var textBox = new MultiLineTextBox()
            .Width(300)
            .Height(120)
            .Wrap(false)
            .Text(text);
        var window = HeadlessWindow.Create(300, 120);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();

        int lineEnd = text.IndexOf('\n', 6);
        textBox.CaretPosition = lineEnd;

        Rect caret = textBox.GetCharRectInWindow(lineEnd);

        Assert.IsLessThanOrEqualTo(textBox.TextViewportBounds.Right, caret.Right,
            "The caret was clipped at the right edge of the text viewport.");
    }

    [TestMethod]
    public void End_OnAVirtualizedLine_ScrollsAllTheWayToTheLastCharacter()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // Both the extent and the caret move while the slice under them is measured, so one pass
        // of bringing the caret into view lands short of the end.
        const string PATTERN = "iiiiWWMMlliii W";
        string text = string.Create(2_000_000, 0, static (span, _) =>
        {
            for (int index = 0; index < span.Length; index++)
            {
                span[index] = PATTERN[index % PATTERN.Length];
            }
        });
        var textBox = new MultiLineTextBox()
            .Width(300)
            .Height(120)
            .Wrap(false)
            .Text(text);
        var window = HeadlessWindow.Create(400, 160);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 0;

        window.SendKeyPress(Key.End);

        Rect caret = textBox.GetCharRectInWindow(text.Length);

        Assert.IsLessThanOrEqualTo(textBox.TextViewportBounds.Right, caret.Right,
            "The end of the line stayed outside the viewport.");
    }

    private static MultiLineTextBox CreateScrollableTextBox(out Window window)
    {
        string text = string.Join("\n", Enumerable.Range(0, 200).Select(line => $"line {line}"));
        var textBox = new MultiLineTextBox()
            .Width(300)
            .Height(120)
            .Wrap(false)
            .Text(text);
        window = HeadlessWindow.Create(300, 120);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        return textBox;
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
