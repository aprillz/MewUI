using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class TextBoxNavigationTests
{
    [TestMethod]
    public void End_OnAVirtualizedLine_ScrollsAllTheWayToTheLastCharacter()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // Past the virtualization threshold the extent is estimated from an average character
        // width, and the scroll limit comes from that estimate.
        const string PATTERN = "iiiiWWMMlliii W";
        string text = string.Create(100_000, 0, static (span, _) =>
        {
            for (int index = 0; index < span.Length; index++)
            {
                span[index] = PATTERN[index % PATTERN.Length];
            }
        });
        var textBox = new TextBox().Width(300).Text(text);
        var window = HeadlessWindow.Create(400, 80);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 0;

        window.SendKeyPress(Key.End);

        Rect caret = textBox.GetCharRectInWindow(text.Length);

        Assert.IsLessThanOrEqualTo(textBox.Bounds.Right, caret.Right,
            "The end of the line stayed outside the viewport.");
    }

    [TestMethod]
    public void PrimaryEndAndHome_ScrollTheViewToTheCaret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = new TextBox()
            .Width(120)
            .Text(new string('W', 400));
        var window = HeadlessWindow.Create(200, 80);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        textBox.CaretPosition = 0;

        window.SendKeyPress(Key.End, ModifierKeys.Control);

        Assert.AreEqual(textBox.Text.Length, textBox.CaretPosition);
        Assert.IsGreaterThan(0.0, textBox.HorizontalOffset, "The document end must be brought into view.");

        window.SendKeyPress(Key.Home, ModifierKeys.Control);

        Assert.AreEqual(0, textBox.CaretPosition);
        Assert.AreEqual(0.0, textBox.HorizontalOffset, 0.01);
    }
}
