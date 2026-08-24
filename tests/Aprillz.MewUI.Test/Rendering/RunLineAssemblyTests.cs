using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// Lines assembled from fragments have to come out where the cluster assembler put them: the same
/// breaks, the same boxes, and the same columns for every insertion.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RunLineAssemblyTests
{
    private const string ASCII = "The quick brown fox jumps over the lazy dog and keeps on running past the edge";
    private const string MIXED = "A\U0001F600é한 tab\there 한글 mixed \U0001F600\U0001F600 end of it";
    private const string LINES = "first line\r\nsecond line is longer\n\nfourth";
    private const string TABS = "\tone\ttwo\tthree\tfour and some text after the tabs";
    private const string LONGWORD = "ab supercalifragilisticexpialidocious cd";

    [TestMethod]
    [DataRow(ASCII, 140.0, TextWrapping.Wrap, TextAlignment.Left)]
    [DataRow(ASCII, 140.0, TextWrapping.Wrap, TextAlignment.Center)]
    [DataRow(ASCII, 140.0, TextWrapping.Wrap, TextAlignment.Right)]
    [DataRow(ASCII, double.PositiveInfinity, TextWrapping.NoWrap, TextAlignment.Left)]
    [DataRow(MIXED, 90.0, TextWrapping.Wrap, TextAlignment.Left)]
    [DataRow(MIXED, double.PositiveInfinity, TextWrapping.NoWrap, TextAlignment.Left)]
    [DataRow(LINES, 80.0, TextWrapping.Wrap, TextAlignment.Left)]
    [DataRow(LINES, double.PositiveInfinity, TextWrapping.NoWrap, TextAlignment.Left)]
    [DataRow(TABS, double.PositiveInfinity, TextWrapping.NoWrap, TextAlignment.Left)]
    [DataRow(TABS, 150.0, TextWrapping.Wrap, TextAlignment.Left)]
    [DataRow(LONGWORD, 60.0, TextWrapping.Wrap, TextAlignment.Left)]
    [DataRow(LONGWORD, 60.0, TextWrapping.WrapWithOverflow, TextAlignment.Left)]
    [DataRow("", double.PositiveInfinity, TextWrapping.NoWrap, TextAlignment.Left)]
    public void LinesMatchTheClusterAssembler(
        string text, double maxWidth, TextWrapping wrapping, TextAlignment alignment)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var engine = (ManagedTextEngine)factory.TextEngine;
        var snapshot = Snapshot(text, maxWidth, wrapping, alignment);

        var expected = engine.CreateLayoutCore(snapshot);
        var actual = engine.CreateLayoutViaRuns(snapshot);

        Assert.AreEqual(expected.Lines.Count, actual.Lines.Count, "The two assemblers broke the text differently.");
        for (int index = 0; index < expected.Lines.Count; index++)
        {
            var want = expected.Lines[index];
            var got = actual.Lines[index];
            string where = $"Line {index} of \"{text}\"";
            Assert.AreEqual(want.TextStart, got.TextStart, $"{where} started elsewhere.");
            Assert.AreEqual(want.TextLength, got.TextLength, $"{where} covered a different range.");
            Assert.AreEqual(want.NewLineLength, got.NewLineLength, $"{where} ended on a different break.");
            Assert.AreEqual(want.Bounds.X, got.Bounds.X, 1e-4, $"{where} sits at a different x.");
            Assert.AreEqual(want.Bounds.Y, got.Bounds.Y, 1e-4, $"{where} sits at a different y.");
            Assert.AreEqual(want.Bounds.Width, got.Bounds.Width, 1e-4, $"{where} measured differently.");
            Assert.AreEqual(want.Bounds.Height, got.Bounds.Height, 1e-4, $"{where} has a different height.");
            Assert.AreEqual(want.Baseline, got.Baseline, 1e-4, $"{where} has a different baseline.");
            Assert.AreEqual(want.TrailingWhitespaceWidth, got.TrailingWhitespaceWidth, 1e-4,
                $"{where} counted its trailing whitespace differently.");
            Assert.AreEqual(want.TrailingWhitespaceLength, got.TrailingWhitespaceLength,
                $"{where} counted its trailing whitespace characters differently.");
        }

        Assert.AreEqual(expected.MeasuredSize.Width, actual.MeasuredSize.Width, 1e-4, "Measured widths differ.");
        Assert.AreEqual(expected.ContentHeight, actual.ContentHeight, 1e-4, "Content heights differ.");
    }

    [TestMethod]
    [DataRow(ASCII, 140.0, TextWrapping.Wrap)]
    [DataRow(MIXED, 90.0, TextWrapping.Wrap)]
    [DataRow(LINES, 80.0, TextWrapping.Wrap)]
    [DataRow(TABS, double.PositiveInfinity, TextWrapping.NoWrap)]
    public void ColumnsMatchTheClusterAssembler(string text, double maxWidth, TextWrapping wrapping)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var engine = (ManagedTextEngine)factory.TextEngine;
        var snapshot = Snapshot(text, maxWidth, wrapping, TextAlignment.Left);

        var expected = engine.CreateLayoutCore(snapshot);
        var actual = engine.CreateLayoutViaRuns(snapshot);

        for (int insertion = 0; insertion <= text.Length; insertion++)
        {
            var want = expected.GetCaretBounds(new CharacterHit(insertion, 0));
            var got = actual.GetCaretBounds(new CharacterHit(insertion, 0));
            Assert.AreEqual(want.X, got.X, 1e-4, $"Insertion {insertion} of \"{text}\" sits at a different x.");
            Assert.AreEqual(want.Y, got.Y, 1e-4, $"Insertion {insertion} of \"{text}\" sits on a different line.");
        }

        for (int lineIndex = 0; lineIndex < expected.Lines.Count; lineIndex++)
        {
            var bounds = expected.Lines[lineIndex].Bounds;
            double y = bounds.Y + bounds.Height * 0.5;
            for (double x = bounds.X - 4; x <= bounds.Right + 8; x += 1.0)
            {
                var want = expected.HitTestPoint(new Point(x, y));
                var got = actual.HitTestPoint(new Point(x, y));
                Assert.AreEqual(want.FirstCharacterIndex, got.FirstCharacterIndex,
                    $"Hit at x={x:F1} on line {lineIndex} of \"{text}\".");
                Assert.AreEqual(want.TrailingLength, got.TrailingLength,
                    $"Trailing length at x={x:F1} on line {lineIndex} of \"{text}\".");
            }
        }
    }

    private static TextLayoutRequestSnapshot Snapshot(
        string text, double maxWidth, TextWrapping wrapping, TextAlignment alignment)
        => TextLayoutRequestSnapshot.Create(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 12),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = maxWidth,
                Wrapping = wrapping,
                Alignment = alignment
            }
        });
}
