using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using Markdig.Syntax.Inlines;

namespace Aprillz.MewUI.Markdown.Test;

[TestClass]
public sealed class MarkdownHtmlTests
{
    private static readonly MarkdownOptions _html = new() { UseHtmlFormatting = true };

    [TestMethod]
    public void FormattingRemainsOptIn()
    {
        const string source = "<b>literal</b> <sup>2</sup>";

        MarkdownBlock block = MarkdownParser.Parse(source, new MarkdownOptions()).Single();

        Assert.AreEqual(source, Text(block));
        Assert.IsTrue(block.Spans.All(static span => span.HtmlStyle is null));
    }

    [TestMethod]
    public void CommentsRemainLiteralByDefaultAndAreHiddenWhenEnabled()
    {
        const string inline = "before <!-- hidden --> after";
        const string block = "<!-- first\nsecond -->";

        Assert.AreEqual(inline, Text(MarkdownParser.Parse(inline, new MarkdownOptions()).Single()));
        Assert.AreEqual("before  after", Text(MarkdownParser.Parse(inline, _html).Single()));
        Assert.AreEqual(block, Text(MarkdownParser.Parse(block, new MarkdownOptions()).Single()).TrimEnd());
        Assert.AreEqual(0, MarkdownParser.Parse(block, _html).Count);
    }

    [TestMethod]
    public void UnclosedCommentRemainsLiteralAndCustomBlockRendererCanObserveComments()
    {
        const string unclosed = "<!-- not closed";
        const string comment = "<!-- hidden -->";

        Assert.AreEqual(unclosed, Text(MarkdownParser.Parse(unclosed, _html).Single()).TrimEnd());
        using var presenter = new MarkdownPresenter { Markdown = comment, Options = _html };
        ParsedMarkdown hidden = presenter.Document;
        presenter.Renderers = new MarkdownRenderers().RegisterBlock<Markdig.Syntax.HtmlBlock>((_, _) => null);
        ParsedMarkdown preserved = presenter.Document;

        Assert.AreEqual(0, hidden.Blocks.Count);
        Assert.AreNotSame(hidden, preserved);
        Assert.AreEqual(1, preserved.Blocks.Count);
        Assert.IsInstanceOfType<Markdig.Syntax.HtmlBlock>(preserved.Blocks.Single().Node);
    }

    [TestMethod]
    public void InlineFormattingCrossesMarkdownNodesAndRestores()
    {
        MarkdownBlock block = MarkdownParser.Parse(
            "<b>bold *both*</b> plain <sup>x<sub>y</sub>z</sup> end", _html).Single();

        Assert.AreEqual("bold both plain xyz end", Text(block));
        MarkdownSpan both = block.Spans.Single(static span => span.Text == "both");
        Assert.IsTrue(both.Italic);
        Assert.AreEqual(FontWeight.Bold, both.HtmlStyle?.FontWeight);
        MarkdownSpan x = block.Spans.Single(static span => span.Text == "x");
        MarkdownSpan y = block.Spans.Single(static span => span.Text == "y");
        MarkdownSpan end = block.Spans.Single(static span => span.Text == " end");
        Assert.AreEqual(0.75, x.HtmlStyle!.Value.FontSizeScale, 1e-9);
        Assert.AreEqual(0.35, x.HtmlStyle!.Value.BaselineOffsetScale, 1e-9);
        Assert.AreEqual(0.5625, y.HtmlStyle!.Value.FontSizeScale, 1e-9);
        Assert.AreEqual(0.20, y.HtmlStyle!.Value.BaselineOffsetScale, 1e-9);
        Assert.IsNull(end.HtmlStyle);
    }

    [TestMethod]
    public void AttributesAndEntitiesProduceNativeStyles()
    {
        MarkdownBlock block = MarkdownParser.Parse(
            "<span font='Georgia' size='20px' weight='600' color='aqua' background='#80112233' underline>" +
            "&lt;text&gt;</span><br>tail", _html).Single();

        Assert.AreEqual("<text>\ntail", Text(block));
        MarkdownHtmlStyle style = block.Spans[0].HtmlStyle!.Value;
        Assert.AreEqual("Georgia", style.FontFamily);
        Assert.AreEqual(20, style.FontSize);
        Assert.AreEqual(FontWeight.SemiBold, style.FontWeight);
        Assert.AreEqual(Color.FromRgb(0, 255, 255), style.Foreground);
        Assert.AreEqual(Color.FromArgb(0x80112233), style.Background);
        Assert.AreEqual(TextDecoration.Underline, style.Decoration);
    }

    [TestMethod]
    public void SupportedHtmlBlockIsFormattedTransactionally()
    {
        const string source = "<b>\nfirst\n<sup>2</sup>\n</b>";

        MarkdownBlock block = MarkdownParser.Parse(source, _html).Single();

        Assert.AreEqual("first\n2", Text(block).Trim());
        Assert.IsTrue(block.Spans.Any(static span => span.HtmlStyle?.FontWeight == FontWeight.Bold));
        Assert.IsTrue(block.Spans.Any(static span => span.HtmlStyle?.BaselineOffsetScale > 0));
    }

    [TestMethod]
    public void UnsupportedHtmlBlockFallsBackAsAWhole()
    {
        const string source = "<div><b>text</b><script>alert(1)</script></div>";

        MarkdownBlock block = MarkdownParser.Parse(source, _html).Single();

        Assert.AreEqual(source, Text(block).TrimEnd());
        Assert.IsTrue(block.Spans.All(static span => span.HtmlStyle is null));
    }

    [TestMethod]
    public void InlineRendererKeepsHtmlNodesForCustomHandling()
    {
        IReadOnlyList<MarkdownBlock> blocks = MarkdownParser.ParseDocument(
            "<b>value</b>", _html, new MarkdownMappingProfile(PreserveHtmlInlineNodes: true)).Blocks;

        Assert.AreEqual("<b>value</b>", Text(blocks.Single()));
        Assert.IsTrue(blocks.Single().Spans.Any(static span => span.Node is HtmlInline));
    }

    [TestMethod]
    public void RendererChangesReparseOnlyWhenHtmlNodeMappingChanges()
    {
        using var presenter = new MarkdownPresenter { Markdown = "<b>value</b>", Options = _html };
        ParsedMarkdown formatted = presenter.Document;
        var unrelated = new MarkdownRenderers().RegisterInline<CodeInline>((_, _) => null);

        presenter.Renderers = unrelated;
        ParsedMarkdown stillFormatted = presenter.Document;
        presenter.Renderers = new MarkdownRenderers().RegisterInline<HtmlInline>((_, _) => null);
        ParsedMarkdown preserved = presenter.Document;

        Assert.AreSame(formatted, stillFormatted);
        Assert.AreNotSame(stillFormatted, preserved);
        Assert.AreEqual("value", Text(stillFormatted.Blocks.Single()));
        Assert.AreEqual("<b>value</b>", Text(preserved.Blocks.Single()));
    }

    [TestMethod]
    public void ParserLimitsAndEventAttributesRemainLiteral()
    {
        var parser = new MarkdownHtmlParser();
        for (int depth = 0; depth < 128; depth++)
        {
            Assert.IsTrue(parser.TryApplyTag("<b>", out _));
        }

        Assert.IsFalse(parser.TryApplyTag("<b>", out _));
        Assert.IsFalse(new MarkdownHtmlParser().TryApplyTag("<span onclick='run()'>", out _));
        Assert.IsFalse(new MarkdownHtmlParser().TryApplyTag("<span " + new string('x', 4096) + ">", out _));
    }

    [TestMethod]
    public void BlockParserPreservesUnknownEntitiesAndRejectsExecutableTags()
    {
        Assert.IsTrue(MarkdownHtmlParser.TryParseBlock("<b>&mew; &#9731;</b>", out MarkdownHtmlBlock block));
        Assert.AreEqual("&mew; ☃", block.Text);
        Assert.IsTrue(MarkdownHtmlParser.TryParseBlock(
            "<b>before<!-- hidden -->after</b>", out MarkdownHtmlBlock commented));
        Assert.AreEqual("beforeafter", commented.Text);
        Assert.IsFalse(MarkdownHtmlParser.TryParseBlock("<b>safe</b><script>run()</script>", out _));
        Assert.IsTrue(MarkdownHtmlParser.TryParseBlock("<!-- comment -->", out MarkdownHtmlBlock comment));
        Assert.AreEqual(string.Empty, comment.Text);
    }

    [TestMethod]
    public void SelectionContainsContentButNotConsumedTags()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter
        {
            Markdown = "before <b>bold</b><br>after",
            Options = _html
        };
        presenter.Measure(new Size(320, 120));

        presenter.SelectAll();

        Assert.AreEqual("before bold\nafter", presenter.SelectedText);
    }

    [TestMethod]
    public void ResolvedScriptStyleUsesHeadingSizeAndOwnerOffset()
    {
        EnsureGdi();
        MarkdownSpan span = MarkdownParser.Parse("<sup>x</sup>", _html).Single().Spans.Single();
        using var paragraph = new MarkdownParagraph([span], new MarkdownTheme(), _ => { })
        {
            FontFamily = "Segoe UI",
            FontSize = 16,
            FontScale = 2
        };

        var layout = Assert.IsInstanceOfType<ManagedTextLayout>(paragraph.GetLayout(300));
        GeometryStyleRun run = layout.Snapshot.Runs.Single();

        Assert.AreEqual(24, run.Style.FontSize, 1e-9);
        Assert.AreEqual(11.2, run.Style.BaselineOffset, 1e-9);
        Assert.AreEqual("x", paragraph.Text);
    }

    private static string Text(MarkdownBlock block) => string.Concat(block.Spans.Select(static span => span.Text));

    private static void EnsureGdi()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
    }
}
