using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class MarkupTextBlockTests
{
    [TestMethod]
    public void MarkupProducesDecodedReadOnlyText()
    {
        var block = new MarkupTextBlock
        {
            Markup = "A &lt;b&gt; <b>bold</b><br/>line &#x1F642;"
        };

        Assert.AreEqual("A <b> bold\nline 🙂", block.Text);
        Assert.IsTrue(MarkupTextBlock.TextProperty.IsReadOnly);
    }

    [TestMethod]
    public void SettingMarkupReplacesTextAndStyles()
    {
        var block = new MarkupTextBlock { Markup = "<b>first</b>" };

        block.Markup = "second";

        Assert.AreEqual("second", block.Text);
        var document = MarkupTextParser.Parse(block.Markup);
        Assert.HasCount(1, document.Spans);
        Assert.AreEqual(MarkupTextStyle.Empty, document.Spans[0].Style);
    }

    [TestMethod]
    public void MarkupBindingReparsesEachValue()
    {
        var source = new ObservableValue<string>("<b>first</b>");
        var block = new MarkupTextBlock();
        block.SetBinding(MarkupTextBlock.MarkupProperty, source);

        source.Value = "second &amp; <i>third</i>";

        Assert.AreEqual("second & third", block.Text);
    }

    [TestMethod]
    public void NestedTagsRestoreTheOuterStyle()
    {
        var document = MarkupTextParser.Parse("<b>A<i>B</i>C</b>D");

        Assert.AreEqual("ABCD", document.Text);
        Assert.HasCount(4, document.Spans);
        Assert.AreEqual(FontWeight.Bold, document.Spans[0].Style.FontWeight);
        Assert.IsTrue(document.Spans[1].Style.Italic);
        Assert.AreEqual(FontWeight.Bold, document.Spans[2].Style.FontWeight);
        Assert.AreEqual(MarkupTextStyle.Empty, document.Spans[3].Style);
    }

    [TestMethod]
    public void TagAliasesAndUnclosedTagsAreSupported()
    {
        var document = MarkupTextParser.Parse(
            "<strong>A</strong><em>B</em><del>C</del><tt>D</tt><small>E</small><u>F");

        Assert.AreEqual("ABCDEF", document.Text);
        Assert.AreEqual(FontWeight.Bold, document.Spans[0].Style.FontWeight);
        Assert.IsTrue(document.Spans[1].Style.Italic);
        Assert.AreEqual(TextDecoration.Strikethrough, document.Spans[2].Style.Decoration);
        Assert.IsTrue(document.Spans[3].Style.UsesMonospaceFont);
        Assert.AreEqual(1.0 / 1.2, document.Spans[4].Style.FontSizeScale, 0.0001);
        Assert.AreEqual(TextDecoration.Underline, document.Spans[5].Style.Decoration);
    }

    [TestMethod]
    public void CrossedClosingTagPopsThroughTheMatchingTag()
    {
        var document = MarkupTextParser.Parse("<b>A<i>B</b>C</i>");

        Assert.AreEqual("ABC</i>", document.Text);
        Assert.AreEqual(FontWeight.Bold, document.Spans[0].Style.FontWeight);
        Assert.IsTrue(document.Spans[1].Style.Italic);
        Assert.AreEqual(MarkupTextStyle.Empty, document.Spans[^1].Style);
    }

    [TestMethod]
    public void UnknownAndMalformedMarkupIsPreserved()
    {
        const string SOURCE = "<unknown x='1'>text</unknown> <b broken='x'";

        var document = MarkupTextParser.Parse(SOURCE);

        Assert.AreEqual(SOURCE, document.Text);
    }

    [TestMethod]
    public void MalformedPrefixDoesNotConsumeTheNextValidTag()
    {
        var document = MarkupTextParser.Parse("1 < 2 <b>bold</b>");

        Assert.AreEqual("1 < 2 bold", document.Text);
        Assert.AreEqual(FontWeight.Bold, document.Spans[^1].Style.FontWeight);
    }

    [TestMethod]
    public void DecodedEntityIsNotParsedAsMarkup()
    {
        var document = MarkupTextParser.Parse("&lt;b&gt;text&lt;/b&gt; &unknown;");

        Assert.AreEqual("<b>text</b> &unknown;", document.Text);
        Assert.IsTrue(document.Spans.All(static span => span.Style == MarkupTextStyle.Empty));
    }

    [TestMethod]
    public void CodeAndRelativeSizeRemainResolvableAgainstTheOwner()
    {
        var document = MarkupTextParser.Parse("<code>A<big>B</big></code><span size='2x'>C</span><span size='18px'>D</span>");

        Assert.IsTrue(document.Spans[0].Style.UsesMonospaceFont);
        Assert.AreEqual(1.2, document.Spans[1].Style.FontSizeScale, 0.0001);
        Assert.AreEqual(2.0, document.Spans[2].Style.FontSizeScale, 0.0001);
        Assert.AreEqual(18.0, document.Spans[3].Style.FontSize);
    }

    [TestMethod]
    public void SpanAttributesCombineAndCanRemoveInheritedDecoration()
    {
        var document = MarkupTextParser.Parse(
            "<u><span font='Arial' weight='600' underline='false' strikethrough color='red' background='#80402010'>x</span></u>");

        var style = document.Spans.Single().Style;
        Assert.AreEqual("Arial", style.FontFamily);
        Assert.AreEqual(FontWeight.SemiBold, style.FontWeight);
        Assert.AreEqual(TextDecoration.Strikethrough, style.Decoration);
        Assert.AreEqual(Color.FromRgb(255, 0, 0), style.Foreground);
        Assert.AreEqual(Color.FromArgb(0x80402010), style.Background);
    }

    [TestMethod]
    public void ColorFormatsAreFixedAndInvalidNestedColorInherits()
    {
        var document = MarkupTextParser.Parse(
            "<span color='#112233'>A<span color='invalid'>B</span></span>" +
            "<span color='#80112233'>C</span><span color='rgb(4, 5, 6)'>D</span><span color='AQUA'>E</span>");

        Assert.AreEqual("AB", document.Text[..2]);
        Assert.AreEqual(Color.FromRgb(0x11, 0x22, 0x33), document.Spans[0].Style.Foreground);
        Assert.AreEqual(2, document.Spans[0].Length);
        Assert.AreEqual(Color.FromArgb(0x80112233), document.Spans[1].Style.Foreground);
        Assert.AreEqual(Color.FromRgb(4, 5, 6), document.Spans[2].Style.Foreground);
        Assert.AreEqual(Color.FromRgb(0, 255, 255), document.Spans[3].Style.Foreground);
    }

    [TestMethod]
    public void FluentMethodsPreserveTheConcreteType()
    {
        var options = new TextMarkupOptions("Cascadia Mono");

        var block = new MarkupTextBlock().Markup("<code>x</code>").Options(options);

        Assert.AreEqual("x", block.Text);
        Assert.AreSame(options, block.Options);
    }

    [TestMethod]
    public void DocumentProducesGeometryAndPaintRanges()
    {
        var document = MarkupTextParser.Parse(
            "A<code><span size='2x' color='#102030' background='#80405060'>B</span></code>C");
        var geometry = new List<GeometryStyleRun>();
        var paint = new List<TextPaintSpan>();
        var defaultStyle = new TextRunStyle("Segoe UI", 10);

        document.AppendGeometryRuns(defaultStyle, new TextMarkupOptions("Cascadia Mono"), geometry);
        document.AppendPaintSpans(paint);

        Assert.HasCount(1, geometry);
        Assert.AreEqual(new TextRange(1, 1), new TextRange(geometry[0].Start, geometry[0].Length));
        Assert.AreEqual("Cascadia Mono", geometry[0].Style.FontFamily);
        Assert.AreEqual(20.0, geometry[0].Style.FontSize);
        Assert.HasCount(1, paint);
        Assert.AreEqual(new TextRange(1, 1), paint[0].Range);
        Assert.AreEqual(Color.FromRgb(0x10, 0x20, 0x30), paint[0].Foreground);
        Assert.AreEqual(Color.FromArgb(0x80405060), paint[0].Background);
    }

    [TestMethod]
    public void StyledRunsReachTheTextLayoutEngine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var markup = new MarkupTextBlock
            {
                FontFamily = "Consolas",
                FontSize = 16,
                Markup = "normal <span size='40'>large</span>"
            };
            var plain = new TextBlock
            {
                FontFamily = "Consolas",
                FontSize = 16,
                Text = markup.Text
            };

            using var markupWindow = HeadlessWindow.Create(500, 200);
            markupWindow.Content = markup;
            markupWindow.PerformLayout();
            using var plainWindow = HeadlessWindow.Create(500, 200);
            plainWindow.Content = plain;
            plainWindow.PerformLayout();

            Assert.IsGreaterThan(plain.DesiredSize.Height, markup.DesiredSize.Height);
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }
}
