using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// A wrap leaves a space at the end of a line. Alignment must ignore it, otherwise right-aligned
/// text stops one space short of the edge on every line except the last.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WrappedLineAlignmentTests
{
    private const string LONG_TEXT =
        "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog";

    private const double MAX_WIDTH = 260;

    [TestMethod]
    public void RightAlignedWrappedLinesEndAtTheSameEdge()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var layout = CreateLayout(factory, TextAlignment.Right);
        Assert.IsGreaterThan(1, layout.Lines.Count, "The sample text did not wrap.");

        double lastRight = GetVisibleRight(layout, layout.Lines.Count - 1);
        for (int line = 0; line < layout.Lines.Count - 1; line++)
        {
            Assert.AreEqual(lastRight, GetVisibleRight(layout, line), 0.5,
                $"Line {line} ends at {GetVisibleRight(layout, line)} while the last ends at {lastRight}.");
        }
    }

    [TestMethod]
    public void LineMetricsSeparateVisibleWidthFromTrailingWhitespace()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var layout = CreateLayout(factory, TextAlignment.Right);

        var wrapped = layout.Lines[0];
        Assert.IsGreaterThan(0, wrapped.TrailingWhitespaceWidth,
            "The wrapped line reported no trailing whitespace.");
        Assert.AreEqual(wrapped.Bounds.Width - wrapped.TrailingWhitespaceWidth, wrapped.VisibleWidth, 0.01);

        var last = layout.Lines[^1];
        Assert.AreEqual(0, last.TrailingWhitespaceWidth, 0.01,
            "The last line has no wrap, so it must carry no trailing whitespace.");
    }

    /// <summary>
    /// The character count of the whitespace a wrap left, which is what a caller working in columns
    /// needs; a width cannot be divided back into characters.
    /// </summary>
    [TestMethod]
    public void LineMetricsReportTrailingWhitespaceInCharactersAsWellAsWidth()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var layout = CreateLayout(factory, TextAlignment.Left);
        Assert.IsGreaterThan(1, layout.Lines.Count, "The sample text did not wrap.");

        var wrapped = layout.Lines[0];
        Assert.IsGreaterThan(0, wrapped.TrailingWhitespaceLength,
            "The wrapped line reported no trailing whitespace characters.");
        Assert.AreEqual(wrapped.TextLength - wrapped.TrailingWhitespaceLength, wrapped.VisibleLength);
        for (int index = 0; index < wrapped.TrailingWhitespaceLength; index++)
        {
            Assert.IsTrue(char.IsWhiteSpace(LONG_TEXT[wrapped.TextEnd - 1 - index]),
                "The reported count reaches past the whitespace at the line end.");
        }

        Assert.AreEqual(0, layout.Lines[^1].TrailingWhitespaceLength,
            "The last line has no wrap, so it must carry no trailing whitespace.");
    }

    [TestMethod]
    public void MeasuredWidthCountsTrailingWhitespaceOnlyAfterAHardBreak()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();

        var layout = CreateLayout(factory, TextAlignment.Left);
        double widestFull = layout.Lines.Max(line => line.Bounds.Width);
        Assert.IsLessThan(widestFull, layout.MeasuredSize.Width,
            $"The measured width kept the space a wrap left behind ({layout.MeasuredSize.Width} of {widestFull}).");

        double broken = MeasureWidth(factory, "One\nTwo", wrap: false);
        double brokenWithSpaces = MeasureWidth(factory, "One   \nTwo", wrap: false);
        Assert.IsGreaterThan(broken, brokenWithSpaces,
            "Trailing spaces before a hard break did not reach the measured width.");
    }

    private static double MeasureWidth(GdiGraphicsFactory factory, string text, bool wrap)
        => factory.TextEngine.CreateLayout(
            new TextLayoutRequest
            {
                Text = text.AsMemory(),
                Dpi = 96,
                DefaultStyle = TextRunStyle.Default,
                Paragraph = new TextParagraphStyle
                {
                    Wrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    Alignment = TextAlignment.Left,
                    MaxWidth = wrap ? MAX_WIDTH : double.PositiveInfinity,
                },
            }).MeasuredSize.Width;

    [TestMethod]
    public void LeftAlignedWrappedLinesStillStartAtZero()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var layout = CreateLayout(factory, TextAlignment.Left);

        for (int line = 0; line < layout.Lines.Count; line++)
        {
            Assert.AreEqual(0, layout.Lines[line].Bounds.X, 0.01,
                $"Left aligned line {line} started at {layout.Lines[line].Bounds.X}.");
        }
    }

    /// <summary>Right edge of the last non-whitespace character on the line.</summary>
    private static double GetVisibleRight(ITextLayout layout, int line)
    {
        var metrics = layout.Lines[line];
        int end = metrics.TextStart + metrics.TextLength;
        while (end > metrics.TextStart && char.IsWhiteSpace(LONG_TEXT[end - 1]))
        {
            end--;
        }
        return layout.GetCaretBounds(new CharacterHit(end, 0)).X;
    }

    private static ITextLayout CreateLayout(GdiGraphicsFactory factory, TextAlignment alignment)
        => factory.TextEngine.CreateLayout(
            new TextLayoutRequest
            {
                Text = LONG_TEXT.AsMemory(),
                Dpi = 96,
                DefaultStyle = TextRunStyle.Default,
                Paragraph = new TextParagraphStyle
                {
                    Wrapping = TextWrapping.Wrap,
                    Alignment = alignment,
                    MaxWidth = MAX_WIDTH,
                },
            });
}
