using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class LineSpacingTests
{
    [TestMethod]
    public void PositiveSpacing_OpensGapsBetweenLinesOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var plain = factory.TextEngine.CreateLayout(CreateRequest("one\ntwo\nthree", 0));
        var spaced = factory.TextEngine.CreateLayout(CreateRequest("one\ntwo\nthree", 6));

        Assert.HasCount(3, spaced.Lines);
        Assert.AreEqual(0, spaced.Lines[0].Bounds.Y, 0.001, "The first line must not move.");
        for (int index = 1; index < spaced.Lines.Count; index++)
        {
            double gap = spaced.Lines[index].Bounds.Y - spaced.Lines[index - 1].Bounds.Bottom;
            Assert.AreEqual(6, gap, 0.001);
        }
        Assert.AreEqual(plain.ContentHeight + 12, spaced.ContentHeight, 0.001);
    }

    [TestMethod]
    public void NegativeSpacing_TightensWithoutReordering()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var plain = factory.TextEngine.CreateLayout(CreateRequest("one\ntwo\nthree", 0));
        var tightened = factory.TextEngine.CreateLayout(CreateRequest("one\ntwo\nthree", -4));

        Assert.HasCount(3, tightened.Lines);
        for (int index = 1; index < tightened.Lines.Count; index++)
        {
            double advance = tightened.Lines[index].Bounds.Y - tightened.Lines[index - 1].Bounds.Y;
            double plainAdvance = plain.Lines[index].Bounds.Y - plain.Lines[index - 1].Bounds.Y;
            Assert.AreEqual(plainAdvance - 4, advance, 0.001);
        }
        Assert.AreEqual(plain.ContentHeight - 8, tightened.ContentHeight, 0.001);
    }

    [TestMethod]
    public void ExtremeNegativeSpacing_ClampsSoLineTopsStayMonotonic()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var layout = factory.TextEngine.CreateLayout(CreateRequest("one\ntwo\nthree", -10_000));

        Assert.HasCount(3, layout.Lines);
        for (int index = 1; index < layout.Lines.Count; index++)
        {
            Assert.IsGreaterThanOrEqualTo(
                layout.Lines[index - 1].Bounds.Y,
                layout.Lines[index].Bounds.Y,
                "A tightened line moved above the previous one.");
        }
    }

    [TestMethod]
    public void NonFiniteSpacing_IsRejected()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => factory.TextEngine.CreateLayout(CreateRequest("one\ntwo", double.NaN)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => factory.TextEngine.CreateLayout(CreateRequest("one\ntwo", double.PositiveInfinity)));
    }

    private static TextLayoutRequest CreateRequest(string text, double lineSpacing)
        => new()
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 16),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = double.PositiveInfinity,
                Wrapping = TextWrapping.NoWrap,
                LineSpacing = lineSpacing
            }
        };
}
