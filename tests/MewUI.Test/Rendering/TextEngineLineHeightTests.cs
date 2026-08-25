using Aprillz.MewUI;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// Every line of one font takes the same height, whether or not it renders a glyph. A backend that
/// pads measured runs to whole device pixels otherwise leaves empty and tab-only lines shorter than
/// the text around them, which shows up as uneven line spacing at some scales.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextEngineLineHeightTests
{
    private static double LineHeight(ITextEngine engine, string text, uint dpi)
        => engine.CreateLayout(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = dpi,
            DefaultStyle = new TextRunStyle("Consolas", 13),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = double.PositiveInfinity,
                Wrapping = TextWrapping.NoWrap
            }
        }).Lines[0].Bounds.Height;

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    public void Direct2D_GlyphlessLinesTakeTheHeightOfATextLine(uint dpi)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        var engine = factory.TextEngine;
        double text = LineHeight(engine, "abc", dpi);

        Assert.AreEqual(text, LineHeight(engine, string.Empty, dpi), 0.0001, "empty line");
        Assert.AreEqual(text, LineHeight(engine, "\t", dpi), 0.0001, "tab-only line");
        Assert.AreEqual(text, LineHeight(engine, "\t\t", dpi), 0.0001, "line of tabs");
        Assert.AreEqual(text, LineHeight(engine, "   ", dpi), 0.0001, "line of spaces");
    }
}
