using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Test.Rendering;

/// <summary>
/// A line box taller than the text it holds - a font with line gap, or a line height the paragraph set -
/// splits that room above and below the text. Given to the descent side alone the text is held against the
/// top of its box, which shows wherever the box is centred on something else, a button face above all.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HalfLeadingTests
{
    private static ITextLayout Layout(GdiGraphicsFactory factory, double lineHeight)
        => factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = "Text".AsMemory(),
            DefaultStyle = new TextRunStyle("Segoe UI", 16),
            Paragraph = new TextParagraphStyle
            {
                Wrapping = TextWrapping.NoWrap,
                MaxWidth = double.PositiveInfinity,
                LineHeight = lineHeight > 0 ? lineHeight : null,
            },
        });

    [TestMethod]
    public void TheRoomALineBoxHasBeyondItsTextIsSplitAboveAndBelow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();

        var natural = Layout(factory, 0).Lines[0];
        var stretched = Layout(factory, natural.Bounds.Height + 20).Lines[0];

        Assert.AreEqual(
            natural.Baseline + 10,
            stretched.Baseline,
            0.51,
            "the line's extra room went under the text instead of around it");

        // What the eye reads: the gap above the text now matches the gap below it.
        double above = stretched.Baseline - natural.Baseline;
        double below = (stretched.Bounds.Height - stretched.Baseline)
            - (natural.Bounds.Height - natural.Baseline);
        Assert.AreEqual(above, below, 0.51, "the two gaps are not the same size");
    }

    [TestMethod]
    public void ALineNoTallerThanItsTextKeepsTheFontsBaseline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var line = Layout(factory, 0).Lines[0];

        // Nothing to split: the baseline stays where the font puts it, inside its own box.
        Assert.IsGreaterThan(0, line.Baseline, "the line reported no baseline at all");
        Assert.IsLessThanOrEqualTo(line.Bounds.Height, line.Baseline, "the baseline left the line box");
    }
}
