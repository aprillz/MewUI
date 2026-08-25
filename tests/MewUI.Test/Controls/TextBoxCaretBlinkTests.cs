using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class TextBoxCaretBlinkTests
{
    // One tick of the real blink puts it in its hidden phase, which is what these start from.
    private static readonly MethodInfo BlinkTick =
        typeof(TextBase).GetMethod("OnCaretBlink", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [TestMethod]
    public void ShiftRight_RestartsBlinkWhenSelectionStartDoesNotChange()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, textBox) = CreateFocusedTextBox();
        try
        {
            int selectionStart = textBox.SelectionStart;
            HideCaretForBlink(textBox);

            window.SendKeyPress(Key.Right, ModifierKeys.Shift);

            Assert.AreEqual(1, textBox.CaretPosition);
            Assert.AreEqual(selectionStart, textBox.SelectionStart);
            Assert.AreEqual(1, textBox.SelectionLength);
            Assert.IsTrue(textBox.CaretVisible);
        }
        finally
        {
            window.FocusManager.ClearFocus();
        }
    }

    [TestMethod]
    public void End_RestartsBlinkWhenCaretMovesToDocumentEdge()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, textBox) = CreateFocusedTextBox();
        try
        {
            HideCaretForBlink(textBox);

            window.SendKeyPress(Key.End);

            Assert.AreEqual(textBox.Text.Length, textBox.CaretPosition);
            Assert.IsTrue(textBox.CaretVisible);
        }
        finally
        {
            window.FocusManager.ClearFocus();
        }
    }

    [TestMethod]
    public void LeftAtDocumentStart_DoesNotRestartBlink()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, textBox) = CreateFocusedTextBox();
        try
        {
            HideCaretForBlink(textBox);

            window.SendKeyPress(Key.Left);

            Assert.AreEqual(0, textBox.CaretPosition);
            Assert.IsFalse(textBox.CaretVisible);
        }
        finally
        {
            window.FocusManager.ClearFocus();
        }
    }

    private static (Window window, TextBox textBox) CreateFocusedTextBox()
    {
        var window = HeadlessWindow.Create();
        var textBox = new TextBox { Text = "abc" };
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        Assert.IsTrue(textBox.IsFocused);
        return (window, textBox);
    }

    private static void HideCaretForBlink(TextBase textBox)
    {
        BlinkTick.Invoke(textBox, null);
        Assert.IsFalse(textBox.CaretVisible, "The tick left the blink in its visible phase.");
    }
}
