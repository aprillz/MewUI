using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// <see cref="TextWrapping.WrapWithOverflow"/> breaks where <see cref="TextWrapping.Wrap"/> does and
/// differs only where the line offers no break opportunity: there it overflows instead of splitting
/// the word.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WrapWithOverflowTests
{
    private const string WORDS = "alpha beta gamma delta epsilon zeta eta theta";
    private const string SINGLE_WORD = "supercalifragilisticexpialidocious";

    [TestMethod]
    public void BreaksAtWordBoundariesLikeWrap()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        double full = Layout(factory, WORDS, TextWrapping.NoWrap, double.PositiveInfinity).MeasuredSize.Width;

        var wrapped = Layout(factory, WORDS, TextWrapping.Wrap, full * 0.4);
        var overflowed = Layout(factory, WORDS, TextWrapping.WrapWithOverflow, full * 0.4);

        Assert.IsGreaterThan(1, wrapped.Lines.Count);
        Assert.AreEqual(wrapped.Lines.Count, overflowed.Lines.Count,
            "WrapWithOverflow split the text at a different place than Wrap.");
        for (int index = 0; index < wrapped.Lines.Count; index++)
        {
            Assert.AreEqual(wrapped.Lines[index].TextStart, overflowed.Lines[index].TextStart,
                $"Line {index} started at a different offset.");
            Assert.AreEqual(wrapped.Lines[index].TextLength, overflowed.Lines[index].TextLength,
                $"Line {index} covered a different range.");
        }
    }

    [TestMethod]
    public void KeepsAnUnbreakableWordOnOneLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        double full = Layout(factory, SINGLE_WORD, TextWrapping.NoWrap, double.PositiveInfinity).MeasuredSize.Width;

        var overflowed = Layout(factory, SINGLE_WORD, TextWrapping.WrapWithOverflow, full * 0.4);

        Assert.AreEqual(1, overflowed.Lines.Count, "The word was split instead of overflowing.");
        Assert.AreEqual(SINGLE_WORD.Length, overflowed.Lines[0].TextLength);
        Assert.IsGreaterThan(full * 0.4, overflowed.MeasuredSize.Width,
            "The line reported a width inside the constraint although its text overflows it.");
        Assert.IsGreaterThan(1, Layout(factory, SINGLE_WORD, TextWrapping.Wrap, full * 0.4).Lines.Count,
            "Wrap no longer splits a word that cannot break.");
    }

    [TestMethod]
    public void OverflowsOnlyTheLineThatCannotBreak()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        string text = "ab " + SINGLE_WORD + " cd";
        double wordWidth = Layout(factory, SINGLE_WORD, TextWrapping.NoWrap, double.PositiveInfinity).MeasuredSize.Width;

        var overflowed = Layout(factory, text, TextWrapping.WrapWithOverflow, wordWidth * 0.5);

        int wordStart = text.IndexOf(SINGLE_WORD, StringComparison.Ordinal);
        var carrying = overflowed.Lines.Single(line =>
            line.TextStart <= wordStart && line.TextStart + line.TextLength >= wordStart + SINGLE_WORD.Length);
        Assert.AreEqual(wordStart, carrying.TextStart,
            "The overflowing word shared its line with the text before it.");
        // The word plus the space that follows it: trailing whitespace stays on the line it broke.
        Assert.AreEqual(SINGLE_WORD.Length + 1, carrying.TextLength,
            "The line kept going past the first break opportunity after the overflowing word.");
        Assert.AreEqual(text.Length, overflowed.Lines[^1].TextStart + overflowed.Lines[^1].TextLength);
        Assert.AreEqual(3, overflowed.Lines.Count, "Expected the lines \"ab \", the word, and \"cd\".");
    }

    private static ITextLayout Layout(IGraphicsFactory factory, string text, TextWrapping wrapping, double maxWidth)
        => factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 12),
            Paragraph = new TextParagraphStyle { MaxWidth = maxWidth, Wrapping = wrapping }
        });
}
