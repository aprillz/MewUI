using Aprillz.MewUI;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Rendering;

/// <summary>
/// A stationary viewport must keep showing the same text. The long-line virtualizer maps a scroll
/// offset to a character offset, so anything that moves that mapping moves the content under the
/// reader, which is what refining a width estimate used to do.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LongLineScrollStabilityTests
{
    private const int LINE_LENGTH = 400_000;

    [TestMethod]
    public void RepeatedLayoutAtTheSameOffsetShowsTheSameText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var view = CreateView("Segoe UI", out _);
        var viewport = new TextViewport(600, 200, HorizontalOffset: 120_000);

        view.SetViewport(viewport);
        string first = DescribeFirstLine(view);

        for (int pass = 0; pass < 6; pass++)
        {
            view.SetViewport(viewport with { HorizontalOffset = viewport.HorizontalOffset + 1 });
            view.SetViewport(viewport);
        }

        Assert.AreEqual(first, DescribeFirstLine(view),
            "The visible slice drifted while the scroll offset stayed put.");
    }

    [TestMethod]
    public void ScrollingBackReturnsToTheSameText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var view = CreateView("Segoe UI", out _);
        var start = new TextViewport(600, 200, HorizontalOffset: 50_000);

        view.SetViewport(start);
        string before = DescribeFirstLine(view);

        foreach (double offset in new double[] { 200_000, 900_000, 400_000, 90_000 })
        {
            view.SetViewport(start with { HorizontalOffset = offset });
        }
        view.SetViewport(start);

        Assert.AreEqual(before, DescribeFirstLine(view),
            "Returning to the same offset landed on different text.");
    }

    [TestMethod]
    public void WrappedLongLineMaterializesAtEveryOffsetPastTheEstimate()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var source = new StringTextDocument(new string('W', LINE_LENGTH));
        var factory = new GdiGraphicsFactory();
        using var view = new TextViewLayout(
            factory.TextEngine,
            source,
            new TextRunStyle("Segoe UI", 14),
            new TextParagraphStyle { Wrapping = TextWrapping.Wrap },
            new TextViewExtensionPipeline(),
            dpi: 96);
        var viewport = new TextViewport(300, 200, 0, 0);
        view.SetViewport(viewport);

        // The row estimate is refined while scrolling, so an offset that was inside the line can
        // end up past its last row. That must still resolve to text, not to an empty slice.
        double height = view.ExtentHeight;
        foreach (double fraction in new[] { 0.5, 0.9, 0.99, 1.0, 1.5 })
        {
            view.SetViewport(viewport with { VerticalOffset = height * fraction });

            Assert.IsNotEmpty(view.MaterializedLines, $"Nothing materialized at {fraction:P0} of the line.");
            Assert.IsGreaterThan(0, view.MaterializedLines[0].LogicalLine.Length,
                $"An empty slice was built at {fraction:P0} of the line.");
        }
    }

    [TestMethod]
    public void CaretLandsOnTheCharacterThatWasHitInsideAVirtualizedLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        // One character of snapping is inherent: a hit inside a glyph resolves to its boundary.
        // Anything beyond that means the caret and the text are in different coordinate systems.
        const double WIDEST_CHARACTER = 13;

        using var view = CreateView("Segoe UI", out _);
        var viewport = new TextViewport(600, 40, 0, 0);
        view.SetViewport(viewport);
        double offsetX = Math.Floor(view.ExtentWidth / 2);
        view.SetViewport(viewport with { HorizontalOffset = offsetX });

        for (int screenX = 0; screenX < 600; screenX += 13)
        {
            int hit = view.HitTest(new Point(screenX, 5)).DocumentOffset;
            double caretScreenX = view.GetCaretBounds(hit).X - offsetX;

            Assert.IsLessThan(WIDEST_CHARACTER, Math.Abs(caretScreenX - screenX),
                $"Hitting {screenX} put the caret at {caretScreenX:F1}.");
        }
    }

    private static string DescribeFirstLine(TextViewLayout view)
    {
        var line = view.MaterializedLines[0];
        return $"{line.LogicalLine.Offset}+{line.LogicalLine.Length}@{line.DocumentX:F2}";
    }

    private static TextViewLayout CreateView(string fontFamily, out IReadOnlyTextDocument document)
    {
        // Deliberately uneven advances: the estimate a slice observes then depends on which slice
        // was measured, which is what makes the mapping move.
        const string PATTERN = "iiiiWWMMlliii W";
        var text = string.Create(LINE_LENGTH, 0, static (span, _) =>
        {
            for (int index = 0; index < span.Length; index++)
            {
                span[index] = PATTERN[index % PATTERN.Length];
            }
        });
        var source = new StringTextDocument(text);
        document = source;
        var factory = new GdiGraphicsFactory();
        var view = new TextViewLayout(
            factory.TextEngine,
            source,
            new TextRunStyle(fontFamily, 14),
            new TextParagraphStyle { Wrapping = TextWrapping.NoWrap },
            new TextViewExtensionPipeline(),
            dpi: 96);
        return view;
    }
}
