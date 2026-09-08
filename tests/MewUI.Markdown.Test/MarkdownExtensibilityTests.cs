using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax;

namespace Aprillz.MewUI.Markdown.Test;

[TestClass]
[DoNotParallelize]
public sealed class MarkdownExtensibilityTests
{
    private static readonly MarkdownOptions _mathOptions = new() { ConfigurePipeline = static builder => builder.UseMathematics() };

    [TestMethod]
    public void UnregisteredExtensionNodeDisplaysSourceText()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter { Markdown = "$$\nx^2\n$$", Options = _mathOptions };
        presenter.Measure(new Size(240, 100));

        // Markdig derives MathBlock from FencedCodeBlock, so the source shows in the code presentation.
        Assert.IsTrue(Descendants(presenter.DocumentRoot!).OfType<TextBlock>().Any(text => text.Text.Contains("x^2")));
    }

    [TestMethod]
    public void BlockRendererReplacesExtensionNode()
    {
        EnsureGdi();
        string? source = null;
        var renderers = new MarkdownRenderers().RegisterBlock<MathBlock>((block, context) =>
        {
            source = context.GetSource(block);
            return new Border { Tag = "math" };
        });
        using var presenter = new MarkdownPresenter { Markdown = "before\n\n$$\nx^2\n$$\n\nafter", Options = _mathOptions, Renderers = renderers };
        presenter.Measure(new Size(240, 100));

        Assert.IsTrue(Descendants(presenter.DocumentRoot!).OfType<Border>().Any(border => Equals(border.Tag, "math")));
        StringAssert.Contains(source, "x^2");
        Assert.AreEqual(2, Descendants(presenter.DocumentRoot!).OfType<MarkdownParagraph>().Count());
    }

    [TestMethod]
    public void BlockRendererCanDelegateChildrenToDefaultPipeline()
    {
        EnsureGdi();
        var renderers = new MarkdownRenderers().RegisterBlock<QuoteBlock>((quote, context) =>
            new Border { Tag = "quote", Child = context.RenderBlocks(quote) });
        using var presenter = new MarkdownPresenter { Markdown = "> quoted **text**", Renderers = renderers };
        presenter.Measure(new Size(240, 100));

        var border = Descendants(presenter.DocumentRoot!).OfType<Border>().Single(border => Equals(border.Tag, "quote"));
        var paragraph = Descendants(border).OfType<MarkdownParagraph>().Single();
        Assert.AreEqual("quoted text", paragraph.Text);
    }

    [TestMethod]
    public void BlockRendererReturningNullFallsBackToDefault()
    {
        EnsureGdi();
        var renderers = new MarkdownRenderers().RegisterBlock<ParagraphBlock>((_, _) => null);
        using var presenter = new MarkdownPresenter { Markdown = "plain", Renderers = renderers };
        presenter.Measure(new Size(240, 100));

        Assert.AreEqual("plain", Descendants(presenter.DocumentRoot!).OfType<MarkdownParagraph>().Single().Text);
    }

    [TestMethod]
    public void BlockRendererRejectsAttachedElement()
    {
        EnsureGdi();
        var owner = new StackPanel();
        var attached = new Border();
        owner.Add(attached);
        var renderers = new MarkdownRenderers().RegisterBlock<ParagraphBlock>((_, _) => attached);
        using var presenter = new MarkdownPresenter { Markdown = "plain", Renderers = renderers };

        Assert.ThrowsExactly<InvalidOperationException>(() => presenter.Measure(new Size(240, 100)));
    }

    [TestMethod]
    public void InlineRendererOccupiesTheNodeColumns()
    {
        EnsureGdi();
        var inlineObject = new CountingInlineObject();
        var renderers = new MarkdownRenderers().RegisterInline<MathInline>((_, _) => inlineObject);
        using var presenter = new MarkdownPresenter { Markdown = "left $x$ right", Options = _mathOptions, Renderers = renderers };
        presenter.Measure(new Size(400, 100));
        presenter.Arrange(new Rect(0, 0, 400, 100));

        var paragraph = Descendants(presenter.DocumentRoot!).OfType<MarkdownParagraph>().Single();
        StringAssert.StartsWith(paragraph.Text, "left ");
        Assert.IsTrue(inlineObject.MeasureCount > 0);
        Assert.IsTrue(paragraph.DesiredSize.Width > 0);
    }

    [TestMethod]
    public void ViewerRealizesOnlyBlocksNearTheViewport()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = Paragraphs(2000) };
        Layout(viewer, 400, 300);

        var host = viewer.BlockHost!;
        Assert.AreEqual(2000, host.BlockCount);
        Assert.IsTrue(host.RealizedCount < 100, $"realized {host.RealizedCount}");
        Assert.IsNotNull(host.GetRealized(0));
        Assert.IsNull(host.GetRealized(1999));
        var scroll = (ScrollViewer)viewer.DocumentRoot!;
        Assert.IsTrue(scroll.ViewportHeight < host.DesiredSize.Height);
    }

    [TestMethod]
    public void ViewerMovesTheRealizedWindowWithTheOffset()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = Paragraphs(2000) };
        Layout(viewer, 400, 300);
        var host = viewer.BlockHost!;
        var scroll = (ScrollViewer)viewer.DocumentRoot!;

        scroll.SetScrollOffsets(0, 3000);
        // Stands in for the measure request the host posts through the dispatcher in a running app.
        host.InvalidateMeasure();
        Layout(viewer, 400, 300);

        Assert.AreEqual(3000, scroll.VerticalOffset);
        Assert.IsNull(host.GetRealized(0));
        int firstRealized = Enumerable.Range(0, host.BlockCount).First(index => host.GetRealized(index) != null);
        var element = host.GetRealized(firstRealized)!;
        // Content is arranged at -offset; the first kept block sits at most one viewport above the visible range.
        Assert.IsTrue(element.Bounds.Y >= -300 - element.Bounds.Height && element.Bounds.Y <= 300, $"y={element.Bounds.Y}");
        Assert.IsTrue(host.RealizedCount < 100, $"realized {host.RealizedCount}");
    }

    [TestMethod]
    public void AnchorNavigationReachesAnUnrealizedHeading()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = Paragraphs(1500) + "# Target heading\n\ntail" };
        Layout(viewer, 400, 300);
        var host = viewer.BlockHost!;
        Assert.IsNull(host.GetRealized(1500));

        Assert.IsTrue(viewer.NavigateToAnchor("target-heading"));
        // The owner clamps against its previous extent, so landing can take a second pass (the window's convergence loop runs it).
        Layout(viewer, 400, 300);
        Layout(viewer, 400, 300);

        var scroll = (ScrollViewer)viewer.DocumentRoot!;
        Assert.IsTrue(scroll.VerticalOffset > 0);
        var heading = host.GetRealized(1500);
        Assert.IsNotNull(heading);
        Assert.IsTrue(heading.Bounds.Y >= 0 && heading.Bounds.Y < 300, $"heading y={heading.Bounds.Y}");
    }

    [TestMethod]
    public void BackgroundParseKeepsOnlyTheLatestRevision()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter { ParseDelay = TimeSpan.FromMilliseconds(20) };
        presenter.Markdown = "first";
        presenter.Markdown = "second";
        Assert.IsTrue(presenter.IsParsePending);

        Assert.IsTrue(SpinWait.SpinUntil(() => !presenter.IsParsePending, TimeSpan.FromSeconds(5)));
        Assert.AreEqual("second", presenter.Document.Blocks.Single().Spans.Single().Text);
    }

    [TestMethod]
    public void BackgroundParseFailureKeepsThePreviousDocument()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter { Markdown = "stable" };
        var previous = presenter.Document;
        Exception? failure = null;
        presenter.ParseFailed += error => failure = error;
        presenter.ParseDelay = TimeSpan.FromMilliseconds(1);

        presenter.Options = new MarkdownOptions { ConfigurePipeline = static _ => throw new InvalidOperationException("broken") };
        Assert.IsTrue(SpinWait.SpinUntil(() => !presenter.IsParsePending, TimeSpan.FromSeconds(5)));

        Assert.IsInstanceOfType<InvalidOperationException>(failure);
        Assert.AreSame(previous, presenter.Document);
    }

    private static string Paragraphs(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(index => $"Paragraph {index} with **bold** text.\n\n"));

    private static void Layout(MarkdownViewer viewer, double width, double height)
    {
        viewer.Measure(new Size(width, height));
        viewer.Arrange(new Rect(0, 0, width, height));
    }

    private static IEnumerable<Element> Descendants(Element root)
    {
        yield return root;
        if (root is IVisualTreeHost host)
        {
            var children = new List<Element>();
            host.VisitChildren(child => { children.Add(child); return true; });
            foreach (var child in children)
            {
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void EnsureGdi()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
    }

    private sealed class CountingInlineObject : IInlineTextObject
    {
        public int MeasureCount { get; private set; }

        public InlineMetrics Measure()
        {
            MeasureCount++;
            return new InlineMetrics(24, 12, 12);
        }

        public void Draw(ITextRenderContext context, Point origin) { }
    }
}
