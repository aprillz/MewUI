using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Gdi;
using Markdig.Syntax;

namespace Aprillz.MewUI.Markdown.Test;

[TestClass]
[DoNotParallelize]
public sealed class MarkdownSelectionTests
{
    private const double WIDTH = 400;

    [TestMethod]
    public void DraggingAcrossTwoParagraphsSelectsBothWithALineBreakBetween()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "First paragraph here.\n\nSecond paragraph here." });
        var paragraphs = Paragraphs(presenter);
        int changes = 0;
        presenter.SelectionChanged += () => changes++;

        Assert.IsTrue(presenter.BeginSelectionAt(CharacterPoint(paragraphs[0], 6)));
        presenter.ExtendSelectionTo(CharacterPoint(paragraphs[1], 6));

        Assert.AreEqual("paragraph here.\nSecond", presenter.SelectedText);
        Assert.IsTrue(presenter.HasSelection);
        Assert.IsTrue(changes >= 2);
    }

    [TestMethod]
    public void BackwardDragProducesTheSameTextAsForward()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "alpha beta\n\ngamma delta" });
        var paragraphs = Paragraphs(presenter);

        presenter.BeginSelectionAt(CharacterPoint(paragraphs[1], 5));
        presenter.ExtendSelectionTo(CharacterPoint(paragraphs[0], 6));

        Assert.AreEqual("beta\ngamma", presenter.SelectedText);
    }

    [TestMethod]
    public void SelectAllInTheViewerIncludesUnrealizedBlocks()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = MarkdownVirtualizationTests.VariedDocument(900) };
        viewer.Measure(new Size(WIDTH, 300));
        viewer.Arrange(new Rect(0, 0, WIDTH, 300));
        Assert.IsTrue(viewer.BlockHost!.RealizedCount < 900);

        viewer.SelectAll();

        StringAssert.Contains(viewer.SelectedText, "Section 0");
        StringAssert.Contains(viewer.SelectedText, "Short line 897.");
        StringAssert.Contains(viewer.SelectedText, "var value892 = 1;");
        Assert.IsTrue(viewer.SelectedText.Length > 20_000);
    }

    [TestMethod]
    public void SelectionSurvivesRealizationOfNewBlocks()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer { Markdown = MarkdownVirtualizationTests.VariedDocument(900) };
        viewer.Measure(new Size(WIDTH, 300));
        viewer.Arrange(new Rect(0, 0, WIDTH, 300));
        viewer.SelectAll();
        var host = viewer.BlockHost!;
        var scroll = (ScrollViewer)viewer.DocumentRoot!;

        scroll.SetScrollOffsets(0, host.GetBlockTop(600));
        viewer.Measure(new Size(WIDTH, 300));
        viewer.Arrange(new Rect(0, 0, WIDTH, 300));

        var realized = Descendants(host).OfType<MarkdownParagraph>().Where(paragraph => paragraph.TextUnit >= 0).ToArray();
        Assert.IsTrue(realized.Length > 0);
        Assert.IsTrue(realized.All(paragraph => paragraph.IsFullySelected), "blocks realized after SelectAll must show the selection");
    }

    [TestMethod]
    public void TableCellsJoinWithTabsAndRowsWithNewLines()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "| a | b |\n| --- | --- |\n| c | d |" });

        presenter.SelectAll();

        Assert.AreEqual("a\tb\nc\td", presenter.SelectedText);
    }

    [TestMethod]
    public void DoubleClickSelectsAWordAndTripleClickTheBlock()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "one two three" });
        var paragraph = Paragraphs(presenter)[0];

        presenter.BeginSelectionAt(CharacterPoint(paragraph, 5), clickCount: 2);
        Assert.AreEqual("two", presenter.SelectedText);

        presenter.BeginSelectionAt(CharacterPoint(paragraph, 5), clickCount: 3);
        Assert.AreEqual("one two three", presenter.SelectedText);
    }

    [TestMethod]
    public void ThemeChangeKeepsTheSelection()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "keep me\n\nand me" });
        presenter.SelectAll();

        presenter.MarkdownTheme = new MarkdownTheme { BlockSpacing = 20 };
        Layout(presenter);

        Assert.AreEqual("keep me\nand me", presenter.SelectedText);
        Assert.IsTrue(Paragraphs(presenter).All(paragraph => paragraph.IsFullySelected));
    }

    [TestMethod]
    public void ReplacingTheDocumentClearsTheSelection()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "old text" });
        presenter.SelectAll();
        Assert.IsTrue(presenter.HasSelection);

        presenter.Markdown = "new text";
        Layout(presenter);

        Assert.IsFalse(presenter.HasSelection);
        Assert.AreEqual(string.Empty, presenter.SelectedText);
    }

    [TestMethod]
    public void CodeBlocksTakePartInTheSelection()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "intro\n\n```csharp\nvar x = 1;\nvar y = 2;\n```\n\noutro" });

        presenter.SelectAll();

        Assert.AreEqual("intro\nvar x = 1;\nvar y = 2;\noutro", presenter.SelectedText);
    }

    [TestMethod]
    public void CustomRendererElementsAreSkipped()
    {
        EnsureGdi();
        var renderers = new MarkdownRenderers().RegisterBlock<ParagraphBlock>((block, context) =>
            context.GetSource(block).StartsWith("custom") ? new Border { Height = 20 } : null);
        using var presenter = Layout(new MarkdownPresenter { Markdown = "first\n\ncustom block\n\nlast", Renderers = renderers });

        presenter.SelectAll();

        Assert.AreEqual("first\ncustom block\nlast", presenter.SelectedText, "copied text comes from the model, so the skipped block is still in it");
        var paragraphs = Paragraphs(presenter);
        Assert.HasCount(2, paragraphs);
        Assert.IsTrue(paragraphs.All(paragraph => paragraph.IsFullySelected));
    }

    [TestMethod]
    public void DisablingSelectionClearsAndIgnoresInput()
    {
        EnsureGdi();
        using var presenter = Layout(new MarkdownPresenter { Markdown = "some text" });
        presenter.SelectAll();
        Assert.IsTrue(presenter.HasSelection);

        presenter.IsSelectionEnabled = false;

        Assert.IsFalse(presenter.HasSelection);
        presenter.SelectAll();
        Assert.IsFalse(presenter.HasSelection);
    }

    private static Point CharacterPoint(MarkdownParagraph paragraph, int offset)
    {
        var layout = paragraph.GetLayout(paragraph.Bounds.Width);
        var bounds = new List<Rect>();
        layout.GetRangeBounds(offset, 1, bounds);
        var glyph = bounds[0];
        return new Point(paragraph.Bounds.X + glyph.X + 0.5, paragraph.Bounds.Y + glyph.Y + glyph.Height / 2);
    }

    private static MarkdownParagraph[] Paragraphs(MarkdownPresenter presenter) =>
        Descendants(presenter.DocumentRoot!).OfType<MarkdownParagraph>().Where(paragraph => paragraph.TextUnit >= 0).ToArray();

    private static MarkdownPresenter Layout(MarkdownPresenter presenter)
    {
        presenter.Measure(new Size(WIDTH, double.PositiveInfinity));
        presenter.Arrange(new Rect(0, 0, WIDTH, presenter.DesiredSize.Height));
        return presenter;
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
}
