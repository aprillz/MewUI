using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class TextBlockInlineTests
{
    [TestMethod]
    public void TextReportsTheConcatenatedRunText()
    {
        var block = new TextBlock();
        block.Inlines.Add(new Run("Normal "));
        block.Inlines.Add(new Run("bold") { FontWeight = FontWeight.Bold });
        block.Inlines.Add(new Run(" tail"));

        Assert.AreEqual("Normal bold tail", block.Text);
    }

    [TestMethod]
    public void SettingTextClearsRuns()
    {
        var block = new TextBlock();
        block.Inlines.Add(new Run("styled") { Italic = true });

        block.Text = "plain";

        Assert.AreEqual("plain", block.Text);
        Assert.IsEmpty(block.Inlines);
    }

    [TestMethod]
    public void EditingARunUpdatesTheFlattenedText()
    {
        var block = new TextBlock();
        var run = new Run("before");
        block.Inlines.Add(run);

        run.Text = "after";

        Assert.AreEqual("after", block.Text);
    }

    [TestMethod]
    public void RunInheritsUnsetValuesFromTheOwner()
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
            var block = new TextBlock { FontFamily = "Consolas", FontSize = 20 };
            var inherited = new Run("plain");
            var overridden = new Run("big") { FontSize = 40 };
            block.Inlines.Add(inherited);
            block.Inlines.Add(overridden);

            using var window = HeadlessWindow.Create(400, 200);
            window.Content = block;
            window.PerformLayout();

            // The larger run must raise the measured height above a uniform 20pt line.
            var uniform = new TextBlock { FontFamily = "Consolas", FontSize = 20, Text = "plainbig" };
            using var reference = HeadlessWindow.Create(400, 200);
            reference.Content = uniform;
            reference.PerformLayout();

            Assert.IsGreaterThan(uniform.DesiredSize.Height, block.DesiredSize.Height,
                "The 40pt run did not affect layout, so run styles are not reaching the engine.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void FluentInlinesReplaceTheExistingRuns()
    {
        var block = new TextBlock().Inlines(new Run("first"));

        block.Inlines(
            new Run("Normal "),
            new Run("bold").Bold(),
            new Run(" and ").Italic(),
            new Run("marked").Underline().Strikethrough());

        Assert.AreEqual("Normal bold and marked", block.Text);
        Assert.HasCount(4, block.Inlines);
        Assert.AreEqual(FontWeight.Bold, block.Inlines[1].FontWeight);
        Assert.IsTrue(block.Inlines[2].Italic);
        Assert.AreEqual(
            Aprillz.MewUI.Text.TextDecoration.Underline | Aprillz.MewUI.Text.TextDecoration.Strikethrough,
            block.Inlines[3].Decoration);
    }

    [TestMethod]
    public void ForegroundOnlyRunDoesNotSplitGeometry()
    {
        var block = new ProbeTextBlock { FontSize = 12 };
        block.Inlines.Add(new Run("colored") { Foreground = Color.FromRgb(255, 0, 0) });

        Assert.IsEmpty(block.CollectGeometryRuns(), "A color-only run must not create a geometry style run.");
        Assert.HasCount(1, block.CollectPaintSpans());
    }

    private sealed class ProbeTextBlock : TextBlock
    {
        public List<Aprillz.MewUI.Text.GeometryStyleRun> CollectGeometryRuns()
        {
            var output = new List<Aprillz.MewUI.Text.GeometryStyleRun>();
            var style = new Aprillz.MewUI.Text.TextRunStyle(FontFamily, FontSize, FontWeight);
            OnGetTextGeometryRuns(in style, output);
            return output;
        }

        public List<Aprillz.MewUI.Text.TextPaintSpan> CollectPaintSpans()
        {
            var output = new List<Aprillz.MewUI.Text.TextPaintSpan>();
            OnGetTextPaintSpans(output);
            return output;
        }
    }
}
