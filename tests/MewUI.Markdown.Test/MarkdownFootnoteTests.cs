using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;

namespace Aprillz.MewUI.Markdown.Test;

[TestClass]
[DoNotParallelize]
public sealed class MarkdownFootnoteTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void ReferencesUseFootnoteOrderAndBacklinks()
    {
        const string source = "Alpha[^note] repeat[^note].\n\n[^note]: First paragraph.\n\n    Second **paragraph**.";

        ParsedMarkdown document = MarkdownParser.ParseDocument(source, new MarkdownOptions());

        Assert.AreEqual(2, document.Blocks.Count);
        MarkdownBlock paragraph = document.Blocks[0];
        MarkdownSpan[] references = paragraph.Spans.Where(static span => span.Anchor != null).ToArray();
        Assert.AreEqual(2, references.Length);
        Assert.AreEqual("1", references[0].Text);
        Assert.AreEqual("#fn:1", references[0].Url);
        Assert.AreEqual("fnref:1", references[0].Anchor);
        Assert.AreEqual("fnref:2", references[1].Anchor);
        Assert.AreEqual(source.IndexOf("[^note]", StringComparison.Ordinal), references[0].SourceStart);
        Assert.AreEqual("[^note]".Length, references[0].SourceLength);
        Assert.AreEqual(0.75, references[0].HtmlStyle!.Value.FontSizeScale, 1e-9);
        Assert.AreEqual(0.35, references[0].HtmlStyle!.Value.BaselineOffsetScale, 1e-9);

        MarkdownBlock group = document.Blocks[1];
        Assert.AreEqual(MarkdownBlockKind.FootnoteGroup, group.Kind);
        MarkdownBlock footnote = group.Children.Single();
        Assert.AreEqual("1.", footnote.Marker);
        Assert.AreEqual("fn:1", footnote.Anchor);
        MarkdownSpan[] backlinks = DescendantSpans(footnote)
            .Where(static span => span.Url?.StartsWith("#fnref:", StringComparison.Ordinal) == true)
            .ToArray();
        CollectionAssert.AreEqual(new[] { "#fnref:1", "#fnref:2" }, backlinks.Select(static span => span.Url).ToArray());
        Assert.IsTrue(backlinks.All(static span => span.SourceStart == -1 && span.SourceLength == 0));
        Assert.IsTrue(DescendantSpans(footnote).Any(static span => span.Text == "paragraph" && span.Bold));

        Assert.AreEqual(0, document.AnchorBlocks["fnref:1"]);
        Assert.AreEqual(0, document.AnchorBlocks["fnref:2"]);
        Assert.AreEqual(1, document.AnchorBlocks["fn:1"]);
    }

    [TestMethod]
    public void FootnotesCanBeDisabledAndUnresolvedReferencesStayLiteralWhenEnabled()
    {
        const string source = "Known[^note] and missing[^missing].\n\n[^note]: Note.";

        ParsedMarkdown enabled = MarkdownParser.ParseDocument(source, new MarkdownOptions());
        ParsedMarkdown disabled = MarkdownParser.ParseDocument(source, new MarkdownOptions { UseFootnotes = false });

        Assert.IsTrue(enabled.Blocks.Any(static block => block.Kind == MarkdownBlockKind.FootnoteGroup));
        StringAssert.Contains(string.Concat(enabled.Blocks[0].Spans.Select(static span => span.Text)), "[^missing]");
        Assert.IsFalse(disabled.Blocks.Any(static block => block.Kind == MarkdownBlockKind.FootnoteGroup));
        StringAssert.Contains(string.Concat(disabled.Blocks.SelectMany(DescendantSpans).Select(static span => span.Text)), "^note");
    }

    [TestMethod]
    public void MixedFootnotesPreserveMarkdigReferenceIndices()
    {
        const string source = "A[^long] B[^short] C[^long].\n\n[^short]: Short.\n[^long]: Long.";

        ParsedMarkdown document = MarkdownParser.ParseDocument(source, new MarkdownOptions());
        MarkdownSpan[] references = document.Blocks[0].Spans.Where(static span => span.Anchor != null).ToArray();

        CollectionAssert.AreEqual(new[] { "#fn:1", "#fn:2", "#fn:1" }, references.Select(static span => span.Url).ToArray());
        CollectionAssert.AreEqual(new[] { "fnref:1", "fnref:3", "fnref:2" }, references.Select(static span => span.Anchor).ToArray());
        MarkdownBlock[] footnotes = document.Blocks.Single(static block => block.Kind == MarkdownBlockKind.FootnoteGroup).Children.ToArray();
        CollectionAssert.AreEqual(new[] { "1.", "2." }, footnotes.Select(static block => block.Marker).ToArray());
        CollectionAssert.AreEqual(new[] { "#fnref:1", "#fnref:2" },
            DescendantSpans(footnotes[0]).Where(static span => span.Url != null).Select(static span => span.Url).ToArray());
        CollectionAssert.AreEqual(new[] { "#fnref:3" },
            DescendantSpans(footnotes[1]).Where(static span => span.Url != null).Select(static span => span.Url).ToArray());
    }

    [TestMethod]
    public void FootnoteGroupRendersAndSelectionOmitsDefinitionMarkup()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
        using var presenter = new MarkdownPresenter
        {
            Markdown = "Text[^note].\n\n[^note]: Footnote body."
        };

        presenter.Measure(new Size(400, 300));
        presenter.SelectAll();

        Assert.IsNotNull(presenter.DocumentRoot);
        StringAssert.Contains(presenter.SelectedText, "Text1.");
        StringAssert.Contains(presenter.SelectedText, "Footnote body.");
        Assert.IsFalse(presenter.SelectedText.Contains("[^note]:", StringComparison.Ordinal));
    }

    [TestMethod]
    public void FootnoteReferenceComposesWithHtmlFormatting()
    {
        const string source = "<span color='red'>Styled[^note]</span>\n\n[^note]: Note.";
        var options = new MarkdownOptions { UseHtmlFormatting = true };

        MarkdownSpan reference = MarkdownParser.ParseDocument(source, options).Blocks[0].Spans
            .Single(static span => span.Anchor != null);

        Assert.AreEqual(Color.FromRgb(255, 0, 0), reference.HtmlStyle?.Foreground);
        Assert.AreEqual(0.75, reference.HtmlStyle!.Value.FontSizeScale, 1e-9);
        Assert.AreEqual(0.35, reference.HtmlStyle!.Value.BaselineOffsetScale, 1e-9);
    }

    [TestMethod]
    public void LargeDocumentFootnoteJumpsStayVirtualizedAndFast()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
        string source = "Opening reference[^tail].\n\n" +
            MarkdownVirtualizationTests.VariedDocument(3000) +
            "[^tail]: Footnote at the end.";
        using var viewer = new MarkdownViewer { Markdown = source };
        Layout(viewer);
        MarkdownBlockHost host = viewer.BlockHost!;

        var forward = System.Diagnostics.Stopwatch.StartNew();
        Assert.IsTrue(viewer.NavigateToAnchor("fn:1"));
        Layout(viewer);
        Layout(viewer);
        forward.Stop();

        int target = host.BlockCount - 1;
        Assert.IsTrue(host.IsMeasured(target));
        Assert.IsFalse(host.IsMeasured(host.BlockCount / 2));
        Assert.IsTrue(forward.ElapsedMilliseconds < 500, $"forward jump took {forward.ElapsedMilliseconds} ms");

        var backward = System.Diagnostics.Stopwatch.StartNew();
        Assert.IsTrue(viewer.NavigateToAnchor("fnref:1"));
        Layout(viewer);
        Layout(viewer);
        backward.Stop();

        int measured = Enumerable.Range(0, host.BlockCount).Count(host.IsMeasured);
        TestContext.WriteLine(
            $"blocks={host.BlockCount}, measured={measured}, forward={forward.Elapsed.TotalMilliseconds:F1} ms, back={backward.Elapsed.TotalMilliseconds:F1} ms");
        Assert.IsTrue(host.IsMeasured(0));
        Assert.IsTrue(measured < 300, $"measured {measured} blocks; jumps must not measure the skipped range");
        Assert.IsTrue(backward.ElapsedMilliseconds < 500, $"back jump took {backward.ElapsedMilliseconds} ms");
    }

    private static void Layout(MarkdownViewer viewer)
    {
        viewer.Measure(new Size(420, 300));
        viewer.Arrange(new Rect(0, 0, 420, 300));
    }

    private static IEnumerable<MarkdownSpan> DescendantSpans(MarkdownBlock block)
    {
        foreach (MarkdownSpan span in block.Spans)
        {
            yield return span;
        }
        foreach (MarkdownBlock child in block.Children)
        {
            foreach (MarkdownSpan span in DescendantSpans(child))
            {
                yield return span;
            }
        }
    }
}
