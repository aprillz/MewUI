using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// The run model has to answer every column query exactly as the cluster walk it replaces, for text
/// that mixes the shapes the two representations disagree about most easily: surrogate pairs,
/// combining marks, tabs, wrapped lines and styled ranges.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RunModelEquivalenceTests
{
    private const string ASCII = "The quick brown fox jumps over the lazy dog and keeps running";
    private const string MIXED = "A\U0001F600é한 tab\there 한글 mixed \U0001F600\U0001F600 end";
    private const string TABS = "\tone\ttwo\tthree\tfour";

    [TestMethod]
    [DataRow(ASCII, 120.0)]
    [DataRow(ASCII, double.PositiveInfinity)]
    [DataRow(MIXED, 90.0)]
    [DataRow(MIXED, double.PositiveInfinity)]
    [DataRow(TABS, double.PositiveInfinity)]
    public void CaretColumnsMatchTheClusterWalk(string text, double maxWidth)
    {
        if (!Skip(out var factory)) return;
        using (factory)
        {
            var layout = Layout(factory, text, maxWidth);
            for (int insertion = 0; insertion <= text.Length; insertion++)
            {
                int lineIndex = LineOf(layout, insertion);
                Assert.AreEqual(
                    layout.GetXForInsertionForTest(lineIndex, insertion),
                    layout.GetXForInsertionViaRuns(lineIndex, insertion),
                    1e-9,
                    $"Insertion {insertion} of \"{text}\" at line {lineIndex}.");
            }
        }
    }

    [TestMethod]
    [DataRow(ASCII, 120.0)]
    [DataRow(MIXED, 90.0)]
    [DataRow(MIXED, double.PositiveInfinity)]
    [DataRow(TABS, double.PositiveInfinity)]
    public void HitTestsMatchTheClusterWalk(string text, double maxWidth)
    {
        if (!Skip(out var factory)) return;
        using (factory)
        {
            var layout = Layout(factory, text, maxWidth);
            for (int lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
            {
                var bounds = layout.Lines[lineIndex].Bounds;
                for (double x = bounds.X - 4; x <= bounds.Right + 4; x += 0.5)
                {
                    var expected = layout.HitTestLineForTest(lineIndex, x);
                    var actual = layout.HitTestLineViaRuns(lineIndex, x);
                    Assert.AreEqual(expected.FirstCharacterIndex, actual.FirstCharacterIndex,
                        $"Character at x={x:F1} on line {lineIndex} of \"{text}\".");
                    Assert.AreEqual(expected.TrailingLength, actual.TrailingLength,
                        $"Trailing length at x={x:F1} on line {lineIndex} of \"{text}\".");
                }
            }
        }
    }

    [TestMethod]
    [DataRow(ASCII, 120.0)]
    [DataRow(MIXED, 90.0)]
    [DataRow(TABS, double.PositiveInfinity)]
    public void RangeExtentsMatchTheClusterWalk(string text, double maxWidth)
    {
        if (!Skip(out var factory)) return;
        using (factory)
        {
            var layout = Layout(factory, text, maxWidth);
            for (int start = 0; start < text.Length; start += 3)
            {
                for (int end = start + 1; end <= text.Length; end += 5)
                {
                    for (int lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
                    {
                        bool expected = layout.TryGetLineRangeExtentForTest(
                            lineIndex, start, end, out double expectedLeft, out double expectedRight);
                        bool actual = layout.TryGetLineRangeExtentViaRuns(
                            lineIndex, start, end, out double actualLeft, out double actualRight);

                        Assert.AreEqual(expected, actual,
                            $"Range [{start},{end}) on line {lineIndex} of \"{text}\".");
                        if (!expected)
                        {
                            continue;
                        }

                        Assert.AreEqual(expectedLeft, actualLeft, 1e-9,
                            $"Left of [{start},{end}) on line {lineIndex}.");
                        Assert.AreEqual(expectedRight, actualRight, 1e-9,
                            $"Right of [{start},{end}) on line {lineIndex}.");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void RunsCoverTheLineWithoutGaps()
    {
        if (!Skip(out var factory)) return;
        using (factory)
        {
            var layout = Layout(factory, MIXED, 90.0);
            for (int lineIndex = 0; lineIndex < layout.Lines.Count; lineIndex++)
            {
                var runs = layout.GetRunsForTest(lineIndex);
                var metrics = layout.Lines[lineIndex];
                if (runs.Length == 0)
                {
                    continue;
                }

                Assert.AreEqual(metrics.TextStart, runs[0].TextStart, $"Line {lineIndex} started late.");
                Assert.AreEqual(metrics.TextEnd, runs[^1].TextEnd, $"Line {lineIndex} ended early.");
                for (int index = 1; index < runs.Length; index++)
                {
                    Assert.AreEqual(runs[index - 1].TextEnd, runs[index].TextStart,
                        $"Runs {index - 1} and {index} of line {lineIndex} left a gap.");
                }
            }
        }
    }

    private static int LineOf(ManagedTextLayout layout, int insertion)
    {
        for (int index = 0; index < layout.Lines.Count; index++)
        {
            var metrics = layout.Lines[index];
            int lineEnd = metrics.TextEnd + metrics.NewLineLength;
            bool ownsBoundary = metrics.NewLineLength > 0 || index == layout.Lines.Count - 1;
            if (insertion < lineEnd || (insertion == lineEnd && ownsBoundary))
            {
                return index;
            }
        }
        return layout.Lines.Count - 1;
    }

    private static ManagedTextLayout Layout(IGraphicsFactory factory, string text, double maxWidth)
        => (ManagedTextLayout)factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 12),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = maxWidth,
                Wrapping = double.IsPositiveInfinity(maxWidth) ? TextWrapping.NoWrap : TextWrapping.Wrap
            },
            // A styled range forces the layout off the fast path even where the text would allow it,
            // which is the case the run model has to serve. It has to end on a text element, which
            // is not where a fixed offset lands in text carrying surrogate pairs.
            Runs = [new GeometryStyleRun(0, StyledPrefixLength(text), new TextRunStyle("Segoe UI", 12, FontWeight.Bold))]
        });

    private static int StyledPrefixLength(string text)
    {
        int[] starts = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        foreach (int start in starts)
        {
            if (start >= 4)
            {
                return start;
            }
        }
        return text.Length;
    }

    private static bool Skip(out GdiGraphicsFactory factory)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            factory = null!;
            return false;
        }

        factory = new GdiGraphicsFactory();
        return true;
    }
}
