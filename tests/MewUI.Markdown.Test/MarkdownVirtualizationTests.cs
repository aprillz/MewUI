using System.Text;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;

namespace Aprillz.MewUI.Markdown.Test;

/// <summary>
/// Virtualization scenarios on a fixture whose block heights vary widely, so the running estimate is
/// wrong and the anchor correction path runs.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MarkdownVirtualizationTests
{
    private const double WIDTH = 420;
    private const double HEIGHT = 300;

    [TestMethod]
    public void ScrollingUpThroughUnmeasuredBlocksKeepsTheAnchorBlockInPlace()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = VariedDocument(1200) };
        var (scroll, host) = Layout(viewer);
        int deep = 1000;
        scroll.SetScrollOffsets(0, host.GetBlockTop(deep));
        Layout(viewer);
        Assert.IsFalse(host.IsMeasured(deep / 2), "blocks skipped by the jump must stay unmeasured");

        int corrections = 0;
        for (int step = 0; step < 60; step++)
        {
            double offset = scroll.VerticalOffset;
            int trackedIndex = host.IndexAt(offset);
            var tracked = host.GetRealized(trackedIndex);
            Assert.IsNotNull(tracked, $"step {step}: block at the viewport top must be realized");
            double trackedTop = tracked.Bounds.Y;
            int before = host.CorrectionCount;

            scroll.SetScrollOffsets(0, offset - 120);
            Layout(viewer);

            corrections += host.CorrectionCount - before;
            var after = host.GetRealized(trackedIndex);
            Assert.IsNotNull(after, $"step {step}: the tracked block left the realized window");
            Assert.AreEqual(trackedTop + 120, after.Bounds.Y, 1.0, $"step {step}: the tracked block moved by more than the wheel step (offset {offset} -> {scroll.VerticalOffset})");
            AssertConsistent(scroll, host, $"step {step}");
        }
        Assert.IsTrue(corrections > 0, "the fixture must exercise the correction path");
    }

    [TestMethod]
    public void FarJumpRealizesOnlyTheTargetWindow()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = VariedDocument(3000) };
        var (scroll, host) = Layout(viewer);
        int initialRealized = host.RealizedCount;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        scroll.SetScrollOffsets(0, host.GetBlockTop(2700));
        Layout(viewer);
        clock.Stop();

        int measured = Enumerable.Range(0, host.BlockCount).Count(host.IsMeasured);
        Assert.IsTrue(host.RealizedCount <= initialRealized * 3, $"realized {host.RealizedCount} vs initial {initialRealized}");
        Assert.IsTrue(measured < 300, $"measured {measured} blocks; the jump must not measure the skipped range");
        Assert.IsFalse(host.IsMeasured(1500));
        Assert.IsTrue(host.IsMeasured(2700));
        AssertConsistent(scroll, host, "after jump");
        Assert.IsTrue(clock.ElapsedMilliseconds < 500, $"jump took {clock.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public void ScrollingToTheEndLandsTheLastBlockOnTheViewportBottom()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = VariedDocument(800) };
        var (scroll, host) = Layout(viewer);

        // Estimates change as blocks near the end get measured; the owner re-clamps on each pass.
        for (int round = 0; round < 4; round++)
        {
            scroll.SetScrollOffsets(0, double.MaxValue);
            Layout(viewer);
        }

        var last = host.GetRealized(host.BlockCount - 1);
        Assert.IsNotNull(last, "the last block must be realized at the end");
        double viewportBottom = scroll.Bounds.Y + scroll.Padding.Top + scroll.ViewportHeight;
        Assert.AreEqual(viewportBottom, last.Bounds.Bottom, 1.5, "the last block bottom must sit on the viewport bottom");
        AssertConsistent(scroll, host, "at end");
    }

    [TestMethod]
    public void WidthChangeKeepsTheAnchorBlockAtTheTop()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = VariedDocument(600) };
        var (scroll, host) = Layout(viewer);
        scroll.SetScrollOffsets(0, host.GetBlockTop(400));
        Layout(viewer);
        int anchor = host.IndexAt(scroll.VerticalOffset);

        viewer.Measure(new Size(WIDTH - 140, HEIGHT));
        viewer.Arrange(new Rect(0, 0, WIDTH - 140, HEIGHT));

        Assert.AreEqual(anchor, host.IndexAt(scroll.VerticalOffset), "the same block must remain at the viewport top after the width change");
        Assert.IsTrue(host.IsMeasured(anchor));
        AssertConsistent(scroll, host, "after width change");
    }

    [TestMethod]
    public void ThemeChangeKeepsTheOffsetOnTheVariedFixture()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = VariedDocument(600) };
        var (scroll, host) = Layout(viewer);
        scroll.SetScrollOffsets(0, host.GetBlockTop(300));
        Layout(viewer);
        int anchor = host.IndexAt(scroll.VerticalOffset);
        double within = scroll.VerticalOffset - host.GetBlockTop(anchor);

        viewer.MarkdownTheme = new MarkdownTheme { BlockSpacing = viewer.MarkdownTheme.BlockSpacing + 3 };
        Layout(viewer);

        var rebuilt = viewer.BlockHost!;
        Assert.AreNotSame(host, rebuilt);
        Assert.AreEqual(anchor, rebuilt.IndexAt(scroll.VerticalOffset), "the same block must be at the viewport top after the rebuild");
        Assert.AreEqual(within, scroll.VerticalOffset - rebuilt.GetBlockTop(anchor), 1.0, "the position inside the anchor block must survive the rebuild");
        AssertConsistent(scroll, rebuilt, "after theme change");
    }

    [TestMethod]
    public void ReplacingTheDocumentWhileScrolledResetsAndDisposesTheOldHost()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = VariedDocument(600) };
        var (scroll, host) = Layout(viewer);
        scroll.SetScrollOffsets(0, host.GetBlockTop(300));
        Layout(viewer);
        Assert.IsTrue(scroll.VerticalOffset > 0);

        viewer.Markdown = VariedDocument(50);
        Layout(viewer);

        Assert.AreEqual(0, scroll.VerticalOffset);
        Assert.AreEqual(0, host.RealizedCount, "the replaced host must have released its elements");
        Assert.AreNotSame(host, viewer.BlockHost);
        AssertConsistent(scroll, viewer.BlockHost!, "after replacement");
    }

    [TestMethod]
    public void AnchorLinkReachesAHeadingNestedInAQuote()
    {
        EnsureGdi();
        string document = VariedDocument(900) + "> ## Nested target\n>\n> quoted body\n\ntail\n";
        using var viewer = new MarkdownViewer { Markdown = document };
        var (scroll, host) = Layout(viewer);

        Assert.IsTrue(viewer.NavigateToAnchor("nested-target"));
        // The owner clamps against its previous extent, so landing can take a second pass.
        Layout(viewer);
        Layout(viewer);

        int quoteIndex = host.BlockCount - 2;
        var quote = host.GetRealized(quoteIndex);
        Assert.IsNotNull(quote);
        double viewportTop = scroll.Bounds.Y + scroll.Padding.Top;
        Assert.IsTrue(quote.Bounds.Y >= viewportTop - 1 && quote.Bounds.Y < viewportTop + HEIGHT, $"quote y={quote.Bounds.Y}");
        AssertConsistent(scroll, host, "after anchor");
    }

    // Realized blocks are contiguous, follow the prefix positions and cover the viewport.
    private static void AssertConsistent(ScrollViewer scroll, MarkdownBlockHost host, string phase)
    {
        double viewportTop = scroll.Bounds.Y + scroll.Padding.Top;
        double viewportBottom = viewportTop + scroll.ViewportHeight;
        double coveredTop = double.PositiveInfinity, coveredBottom = double.NegativeInfinity;
        FrameworkElement? previous = null;
        for (int index = 0; index < host.BlockCount; index++)
        {
            var element = host.GetRealized(index);
            if (element == null)
            {
                continue;
            }
            if (previous != null && host.GetRealized(index - 1) == previous)
            {
                Assert.IsTrue(element.Bounds.Y >= previous.Bounds.Bottom - 0.5, $"{phase}: block {index} overlaps block {index - 1}");
            }
            coveredTop = Math.Min(coveredTop, element.Bounds.Y);
            coveredBottom = Math.Max(coveredBottom, element.Bounds.Bottom);
            previous = element;
        }
        Assert.IsTrue(coveredTop <= viewportTop + 1, $"{phase}: realized blocks start at {coveredTop - viewportTop:F1} below the viewport top");
        Assert.IsTrue(coveredBottom >= viewportBottom - 1, $"{phase}: realized blocks end at {viewportBottom - coveredBottom:F1} above the viewport bottom");
    }

    private static (ScrollViewer Scroll, MarkdownBlockHost Host) Layout(MarkdownViewer viewer)
    {
        viewer.Measure(new Size(WIDTH, HEIGHT));
        viewer.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
        return ((ScrollViewer)viewer.DocumentRoot!, viewer.BlockHost!);
    }

    /// <summary>Blocks of widely different heights; every block carries its index so text layouts are not shared.</summary>
    internal static string VariedDocument(int blocks)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < blocks; index++)
        {
            switch (index % 7)
            {
                case 0:
                    builder.Append("## Section ").Append(index).Append("\n\n");
                    break;
                case 1:
                    builder.Append("Short line ").Append(index).Append(".\n\n");
                    break;
                case 2:
                    builder.Append("Block ").Append(index).Append(" is a long paragraph that wraps across several lines because it keeps going on about **bold** details, [links](https://example.com/").Append(index).Append("), and `inline code` until the width runs out and the layout has to break it more than once.\n\n");
                    break;
                case 3:
                    builder.Append("```csharp\nvar value").Append(index).Append(" = 1;\nvar other = value").Append(index).Append(" + 2;\nConsole.WriteLine(other);\n```\n\n");
                    break;
                case 4:
                    builder.Append("| Key | Value ").Append(index).Append(" |\n| --- | --- |\n| a | 1 |\n| b | 2 |\n| c | 3 |\n\n");
                    break;
                case 5:
                    builder.Append("> Quote ").Append(index).Append(" with a second line of text\n> that continues here.\n\n");
                    break;
                default:
                    builder.Append("- item ").Append(index).Append("\n- another item\n- third item\n\n");
                    break;
            }
        }
        return builder.ToString();
    }

    private static void EnsureGdi()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
    }
}
