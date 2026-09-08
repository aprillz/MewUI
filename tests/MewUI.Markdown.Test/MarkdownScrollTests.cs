using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;

namespace Aprillz.MewUI.Markdown.Test;

[TestClass]
[DoNotParallelize]
public sealed class MarkdownScrollTests
{
    private const string TALL_DOCUMENT = "# Title\n\n" + "paragraph\n\n" + "paragraph\n\n" + "paragraph\n\n" +
        "paragraph\n\n" + "paragraph\n\n" + "paragraph\n\n" + "paragraph\n\n" + "paragraph\n\n" + "paragraph\n\n";

    [TestMethod]
    public void ViewerKeepsOneScrollViewerAcrossDocumentChanges()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = TALL_DOCUMENT };
        Layout(viewer);
        var scroll = Assert.IsInstanceOfType<ScrollViewer>(viewer.DocumentRoot);

        viewer.Markdown = TALL_DOCUMENT + "more\n";
        Layout(viewer);

        Assert.AreSame(scroll, viewer.DocumentRoot);
    }

    [TestMethod]
    public void NewDocumentStartsAtTopWithContentAndBarInStep()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = TALL_DOCUMENT };
        Layout(viewer);
        var scroll = (ScrollViewer)viewer.DocumentRoot!;
        double restingTop = ContentTop(scroll);
        scroll.SetScrollOffsets(0, 40);
        Layout(viewer);
        Assert.AreEqual(40, scroll.VerticalOffset);

        viewer.Markdown = TALL_DOCUMENT + "other\n";
        Layout(viewer);

        Assert.AreEqual(0, scroll.VerticalOffset);
        AssertContentMatchesOffset(scroll, restingTop);
    }

    [TestMethod]
    public void ThemeChangeKeepsOffsetWithContentAndBarInStep()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = TALL_DOCUMENT };
        Layout(viewer);
        var scroll = (ScrollViewer)viewer.DocumentRoot!;
        double restingTop = ContentTop(scroll);
        scroll.SetScrollOffsets(0, 40);
        Layout(viewer);

        int anchor = viewer.BlockHost!.IndexAt(40);
        viewer.MarkdownTheme = new MarkdownTheme { BlockSpacing = viewer.MarkdownTheme.BlockSpacing + 2 };
        Layout(viewer);

        // The rebuilt tree keeps the same block at the viewport top; the offset shifts by the spacing change above it.
        Assert.AreEqual(40 + anchor * 2, scroll.VerticalOffset, 1.0);
        AssertContentMatchesOffset(scroll, restingTop);
    }

    private static void Layout(MarkdownViewer viewer)
    {
        viewer.Measure(new Size(240, 80));
        viewer.Arrange(new Rect(0, 0, 240, 80));
    }

    private static double ContentTop(ScrollViewer scroll) => ((Element)scroll.Content!).Bounds.Y;

    // The content top sits exactly one offset above its resting place, so the bar and the view agree.
    private static void AssertContentMatchesOffset(ScrollViewer scroll, double restingTop) =>
        Assert.AreEqual(restingTop - scroll.VerticalOffset, ContentTop(scroll), 0.5);

    private static void EnsureGdi()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
    }
}
