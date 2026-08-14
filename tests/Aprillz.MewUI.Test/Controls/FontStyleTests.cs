using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// FontStyle is an inherited text property like FontWeight, and folds to the engine's italic flag at the
/// boundary: the font layer knows two states, so the surface offers the two it can draw.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FontStyleTests
{
    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    [TestMethod]
    public void ItalicIsInheritedLikeAWeight()
    {
        if (SkipOnNonWindows()) return;

        var text = new TextBlock { Text = "Slanted" };
        var host = new ContentControl { Content = text }.Italic();

        var window = HeadlessWindow.Create();
        window.Content = host;
        window.PerformLayout();

        Assert.AreEqual(FontStyle.Italic, text.FontStyle, "the child did not inherit the style");

        host.Italic(false);
        window.PerformLayout();
        Assert.AreEqual(FontStyle.Normal, text.FontStyle, "the child kept a style the parent gave up");

        window.Close();
    }

    [TestMethod]
    public void ALocalStyleWinsOverTheInheritedOne()
    {
        if (SkipOnNonWindows()) return;

        var upright = new TextBlock { Text = "Upright" }.Italic(false);
        var host = new ContentControl { Content = upright }.Italic();

        var window = HeadlessWindow.Create();
        window.Content = host;
        window.PerformLayout();

        Assert.AreEqual(FontStyle.Normal, upright.FontStyle, "the inherited style overrode the local one");

        window.Close();
    }

    [TestMethod]
    public void TheStyleReachesTheTextTheControlMeasures()
    {
        if (SkipOnNonWindows()) return;

        // A family whose italic is a face of its own, not a shear of the upright: GDI synthesises oblique
        // by shearing glyphs and leaves the advances alone, so a synthesised family would measure alike
        // however well the style travelled.
        const string FAMILY = "Times New Roman";
        var upright = new TextBlock { Text = "Measured text", FontSize = 24, FontFamily = FAMILY };
        var italic = new TextBlock { Text = "Measured text", FontSize = 24, FontFamily = FAMILY }.Italic();

        var window = HeadlessWindow.Create();
        window.Content = new StackPanel().Vertical().Children(upright, italic);
        window.PerformLayout();

        // The two ask the text engine for different fonts, so the run they lay out is not the same one.
        Assert.AreNotEqual(
            upright.DesiredSize.Width,
            italic.DesiredSize.Width,
            "the italic style never reached the text engine");

        window.Close();
    }
}
