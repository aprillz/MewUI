using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// sub and sup shrink the size to 0.75 of the parent and shift the baseline by +0.35 (sup) or -0.20 (sub)
/// of the parent's size before the shrink. Relative parts follow the owner's font size when the layout
/// resolves them, absolute sizes stay fixed.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MarkupTextScriptTests
{
    private const string FAMILY = "Segoe UI";

    [TestMethod]
    [DataRow("<sup>x</sup>", 16.0, 12.0, 5.6)]
    [DataRow("<sup><sup>x</sup></sup>", 16.0, 9.0, 9.8)]
    [DataRow("<sup><sub>x</sub></sup>", 16.0, 9.0, 3.2)]
    [DataRow("<sub>x</sub>", 16.0, 12.0, -3.2)]
    [DataRow("<span size='20'><sup>x</sup></span>", 16.0, 15.0, 7.0)]
    [DataRow("<sup><span size='20'>x</span></sup>", 16.0, 20.0, 5.6)]
    [DataRow("<sup>x</sup>", 24.0, 18.0, 8.4)]
    [DataRow("<sup><sup>x</sup></sup>", 24.0, 13.5, 14.7)]
    [DataRow("<span size='20'><sup>x</sup></span>", 24.0, 15.0, 7.0)]
    [DataRow("<sup><span size='20'>x</span></sup>", 24.0, 20.0, 8.4)]
    [DataRow("<sup><span size='20'><sub>x</sub></span></sup>", 16.0, 15.0, 1.6)]
    [DataRow("<sup><span size='20'><sub>x</sub></span></sup>", 24.0, 15.0, 4.4)]
    [DataRow("<sup><big>x</big></sup>", 16.0, 14.4, 5.6)]
    [DataRow("<big><sup>x</sup></big>", 16.0, 14.4, 6.72)]
    [DataRow("<SUP>x</SUP>", 16.0, 12.0, 5.6)]
    public void ScriptResolvesSizeAndShiftAgainstTheOwner(string markup, double ownerSize, double fontSize, double offset)
    {
        var run = Resolve(markup, new TextRunStyle(FAMILY, ownerSize)).Last();

        Assert.AreEqual("x", MarkupTextParser.Parse(markup).Text);
        Assert.AreEqual(fontSize, run.Style.FontSize, 1e-9);
        Assert.AreEqual(offset, run.Style.BaselineOffset, 1e-9);
    }

    [TestMethod]
    public void OwnerOffsetIsAddedOnce()
    {
        var owner = new TextRunStyle(FAMILY, 16) { BaselineOffset = 2 };

        var runs = Resolve("a<sup>x</sup>", owner);

        Assert.HasCount(1, runs);
        Assert.AreEqual(7.6, runs[0].Style.BaselineOffset, 1e-9);
    }

    [TestMethod]
    public void ClosingRestoresTheWholeParentStyle()
    {
        var document = MarkupTextParser.Parse("<b>a<sup>2<sub>3</sub>4</sup>b</b>c");

        Assert.AreEqual("a234bc", document.Text);
        Assert.AreEqual(document.Spans[0].Style, document.Spans[4].Style);
        Assert.AreEqual(document.Spans[1].Style, document.Spans[3].Style);
        Assert.AreEqual(MarkupTextStyle.Empty, document.Spans[5].Style);

        var runs = Resolve("<b>a<sup>2<sub>3</sub>4</sup>b</b>c", new TextRunStyle(FAMILY, 16));
        Assert.AreEqual(0, runs.Single(run => run.Start == 4).Style.BaselineOffset);
        Assert.AreEqual(FontWeight.Bold, runs.Single(run => run.Start == 1).Style.Weight);
        Assert.IsFalse(runs.Any(run => run.Start == 5), "the text after every closing tag kept a style");
    }

    [TestMethod]
    public void LineBreakKeepsTheScriptAndTextKeepsOnlyContent()
    {
        var runs = Resolve("<sup>한<br/>🙂</sup>끝", new TextRunStyle(FAMILY, 16));

        Assert.AreEqual("한\n🙂끝", MarkupTextParser.Parse("<sup>한<br/>🙂</sup>끝").Text);
        Assert.HasCount(1, runs);
        Assert.AreEqual(new TextRange(0, 4), new TextRange(runs[0].Start, runs[0].Length));
        Assert.AreEqual(5.6, runs[0].Style.BaselineOffset, 1e-9);
    }

    [TestMethod]
    public void ScriptsCombineWithColorAndDecoration()
    {
        const string MARKUP = "<u>x<sup><span color='red'>2</span></sup></u>";
        var document = MarkupTextParser.Parse(MARKUP);
        var paint = new List<TextPaintSpan>();
        document.AppendPaintSpans(paint);

        var runs = Resolve(MARKUP, new TextRunStyle(FAMILY, 16));

        Assert.AreEqual(TextDecoration.Underline, runs[^1].Style.Decoration);
        Assert.AreEqual(5.6, runs[^1].Style.BaselineOffset, 1e-9);
        Assert.AreEqual(new TextRange(1, 1), paint.Single().Range);
        Assert.AreEqual(Color.FromRgb(255, 0, 0), paint.Single().Foreground);
    }

    [TestMethod]
    public void UnclosedCrossedAndSelfClosingScriptsFollowTheExistingPolicy()
    {
        var unclosed = Resolve("a<sup>bc", new TextRunStyle(FAMILY, 16));
        Assert.AreEqual(new TextRange(1, 2), new TextRange(unclosed.Single().Start, unclosed.Single().Length));

        var crossed = MarkupTextParser.Parse("<sup>a<b>b</sup>c</b>");
        Assert.AreEqual("abc</b>", crossed.Text);
        Assert.AreEqual(MarkupTextStyle.Empty, crossed.Spans[^1].Style);

        var selfClosing = MarkupTextParser.Parse("<sup/>x");
        Assert.AreEqual("x", selfClosing.Text);
        Assert.AreEqual(MarkupTextStyle.Empty, selfClosing.Spans.Single().Style);
    }

    [TestMethod]
    public void NestingLimitLeavesTheExcessTagsLiteralAndTheStyleFinite()
    {
        string markup = string.Concat(Enumerable.Repeat("<sup>", 130)) + "x";

        var document = MarkupTextParser.Parse(markup);
        var run = Resolve(markup, new TextRunStyle(FAMILY, 16)).Last();

        Assert.AreEqual("<sup><sup>x", document.Text);
        Assert.IsTrue(double.IsFinite(run.Style.FontSize) && run.Style.FontSize > 0);
        Assert.IsTrue(double.IsFinite(run.Style.BaselineOffset));
    }

    [TestMethod]
    public void ShiftBeyondTheOffsetLimitLeavesTheScriptTagLiteral()
    {
        // 1.5e308 is a finite size, but a superscript shift of 0.35 times it exceeds the engine's offset limit.
        string hugeSize = "15" + new string('0', 307);

        var document = MarkupTextParser.Parse($"<span size='{hugeSize}'><sup>x</sup></span>");

        Assert.AreEqual("<sup>x</sup>", document.Text);
        Assert.IsTrue(document.Spans.All(span => span.Style.BaselineOffset == 0));
    }

    [TestMethod]
    public void ScriptsReachTheLayoutAndFollowTheOwnerFontSize()
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
            var markup = new MarkupTextBlock { FontFamily = FAMILY, FontSize = 16, Markup = "x<sup>2</sup>" };
            var plain = new TextBlock { FontFamily = FAMILY, FontSize = 16, Text = "x2" };
            using var markupWindow = HeadlessWindow.Create(400, 200);
            markupWindow.Content = markup;
            markupWindow.PerformLayout();
            using var plainWindow = HeadlessWindow.Create(400, 200);
            plainWindow.Content = plain;
            plainWindow.PerformLayout();

            Assert.AreEqual("x2", markup.Text);
            Assert.IsGreaterThan(plain.DesiredSize.Height, markup.DesiredSize.Height,
                "the raised script did not reach the layout");

            double smallHeight = markup.DesiredSize.Height;
            markup.FontSize = 24;
            markupWindow.PerformLayout();
            Assert.IsGreaterThan(smallHeight, markup.DesiredSize.Height);
            Assert.AreEqual("x<sup>2</sup>", markup.Markup);

            var deep = new MarkupTextBlock
            {
                FontFamily = FAMILY,
                FontSize = 16,
                Markup = string.Concat(Enumerable.Repeat("<sup>", 128)) + "x"
            };
            using var deepWindow = HeadlessWindow.Create(400, 200);
            deepWindow.Content = deep;
            deepWindow.PerformLayout();
            Assert.IsTrue(double.IsFinite(deep.DesiredSize.Height), "deep nesting produced a non-finite layout");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    private static List<GeometryStyleRun> Resolve(string markup, TextRunStyle owner)
    {
        var runs = new List<GeometryStyleRun>();
        MarkupTextParser.Parse(markup).AppendGeometryRuns(owner, new TextMarkupOptions("Cascadia Mono"), runs);
        return runs;
    }
}
