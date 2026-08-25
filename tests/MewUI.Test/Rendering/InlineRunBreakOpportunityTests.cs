using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Test.Rendering;

/// <summary>
/// An inline object covers columns the breaker can no longer read as text, so an object standing in
/// for a space takes the break with it unless it says otherwise. An editor showing whitespace
/// markers puts one on every space, and without this its wrapping falls back to breaking mid-word.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class InlineRunBreakOpportunityTests
{
    private const string TEXT = "alpha beta gamma delta epsilon zeta";

    private sealed class SpaceSizedInline(double width) : IInlineTextObject
    {
        public InlineMetrics Measure() => new(width, 16, 12);
        public void Draw(ITextRenderContext context, Point origin) { }
    }

    [TestMethod]
    public void AnInlineThatDeclaresItBreaksLinesKeepsTheWrapOnWords()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();

        int[] plain = RowStarts(factory, inlines: []);
        Assert.IsGreaterThan(1, plain.Length, "The sample text did not wrap.");
        foreach (int start in plain.Skip(1))
        {
            Assert.IsTrue(char.IsWhiteSpace(TEXT[start - 1]), $"Row at {start} did not start after a space.");
        }

        var marked = SpaceRuns(factory, breaksLine: true);
        CollectionAssert.AreEqual(plain, RowStarts(factory, marked),
            "Objects standing in for the spaces moved the wrap positions.");
    }

    private static IReadOnlyList<InlineRun> SpaceRuns(GdiGraphicsFactory factory, bool breaksLine)
    {
        double spaceWidth = MeasureSpaceWidth(factory);
        var runs = new List<InlineRun>();
        for (int index = TEXT.IndexOf(' '); index >= 0; index = TEXT.IndexOf(' ', index + 1))
        {
            runs.Add(new InlineRun(index, 1, new SpaceSizedInline(spaceWidth), breaksLine));
        }
        return runs;
    }

    /// <summary>The object has to take the width of the space, or the line wraps somewhere else.</summary>
    private static double MeasureSpaceWidth(GdiGraphicsFactory factory)
    {
        var withSpace = Layout(factory, "a a", [], double.PositiveInfinity);
        var without = Layout(factory, "aa", [], double.PositiveInfinity);
        return withSpace.MeasuredSize.Width - without.MeasuredSize.Width;
    }

    private static int[] RowStarts(GdiGraphicsFactory factory, IReadOnlyList<InlineRun> inlines)
        => Layout(factory, TEXT, inlines, 120).Lines.Select(static line => line.TextStart).ToArray();

    private static ITextLayout Layout(
        GdiGraphicsFactory factory, string text, IReadOnlyList<InlineRun> inlines, double maxWidth)
        => factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            DefaultStyle = new TextRunStyle("Segoe UI", 14),
            Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.Wrap, MaxWidth = maxWidth },
            Inlines = inlines
        });
}
