using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A text box lays its lines out with the font it inherits. A font that reaches it from an ancestor, on attach or
/// later, has to rebuild the lines just as one set on the box itself does.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextViewFontTests
{
    private const double INHERITED_FONT_SIZE = 40;

    [TestMethod]
    public void LinesBuiltBeforeAttachTakeTheFontInheritedOnAttach()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = new MultiLineTextBox().Text("abc\ndef");
        var host = (ITextViewHost)textBox;

        // Something asks for a metric before the box is in the window, which builds the lines with the default font.
        double detachedHeight = host.DefaultLineHeight;

        // The ancestor has its font before the box is placed under it, so no change reaches the box from above.
        var ancestor = new ContentControl().FontSize(INHERITED_FONT_SIZE);
        var window = HeadlessWindow.Create(400, 300);
        window.Content = ancestor;
        window.PerformLayout();
        ancestor.Content = textBox;
        window.PerformLayout();

        Assert.AreEqual(INHERITED_FONT_SIZE, textBox.FontSize);
        Assert.IsGreaterThan(detachedHeight * 2, host.DefaultLineHeight,
            "The lines kept the font they were built with before the box was attached.");
    }

    [TestMethod]
    public void AFontChangedOnAnAncestorRebuildsTheLines()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var textBox = new MultiLineTextBox().Text("abc\ndef");
        var host = (ITextViewHost)textBox;
        var ancestor = new ContentControl { Content = textBox };
        var window = HeadlessWindow.Create(400, 300);
        window.Content = ancestor;
        window.PerformLayout();
        double before = host.DefaultLineHeight;

        ancestor.FontSize = INHERITED_FONT_SIZE;
        window.PerformLayout();

        Assert.IsGreaterThan(before * 2, host.DefaultLineHeight,
            "The lines kept the font from before the ancestor's font changed.");
        Assert.IsGreaterThan(before * 2, host.VisibleTextLines[0].Height);
    }
}
