using Aprillz.MewUI.Markdown;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;

[assembly: DoNotParallelize]

namespace Aprillz.MewUI.Markdown.Test;

[TestClass]
public sealed class MarkdownParserTests
{
    [TestMethod]
    public void CodeBlocksPreserveIndentedAndFencedContents()
    {
        var blocks = MarkdownParser.Parse("    first\n    second\n\n```csharp\nvar value = 1;\n```", new MarkdownOptions());

        Assert.AreEqual("Code", blocks[0].Kind.ToString());
        Assert.AreEqual("first\nsecond", Text(blocks[0]));
        Assert.AreEqual("Code", blocks[1].Kind.ToString());
        Assert.AreEqual("csharp", blocks[1].Info);
        StringAssert.Contains(Text(blocks[1]), "var value = 1;");
    }

    [TestMethod]
    public void InlineStylesKeepUnicodeAndEmojiText()
    {
        var blocks = MarkdownParser.Parse("안녕 🌍 **굵게 _기울임_** ~~취소~~ ++inserted++ ==marked== `code`", new MarkdownOptions());
        var spans = blocks[0].Spans;

        StringAssert.Contains(Text(blocks[0]), "안녕 🌍");
        Assert.IsTrue(spans.Any(span => span.Text == "굵게 " && span.Bold && !span.Italic));
        Assert.IsTrue(spans.Any(span => span.Text == "기울임" && span.Bold && span.Italic));
        Assert.IsTrue(spans.Any(span => span.Text == "취소" && span.Strike && !span.Bold));
        Assert.IsTrue(spans.Any(span => span.Text == "inserted" && span.Inserted));
        Assert.IsTrue(spans.Any(span => span.Text == "marked" && span.Marked));
        Assert.IsTrue(spans.Any(span => span.Text == "code" && span.Code));
    }

    [TestMethod]
    public void LinksExposeDestinationAndSourceRange()
    {
        const string source = "before [go](https://example.test " + "\"title\") after";
        var span = MarkdownParser.Parse(source, new MarkdownOptions())[0].Spans.Single(item => item.Url is not null);

        Assert.AreEqual("go", span.Text);
        Assert.AreEqual("https://example.test", span.Url);
        Assert.AreEqual("title", span.Title);
        Assert.AreEqual(source.IndexOf("[go]", StringComparison.Ordinal), span.SourceStart);
        Assert.AreEqual("[go](https://example.test \"title\")".Length, span.SourceLength);
    }

    [TestMethod]
    public void HtmlIsLiteralText()
    {
        const string source = "<b>literal</b> <span>markup</span>";
        var block = MarkdownParser.Parse(source, new MarkdownOptions())[0];

        Assert.AreEqual(source, Text(block));
        Assert.IsFalse(block.Spans.Any(span => span.Bold));
    }

    [TestMethod]
    public void HtmlBlocksRemainLiteralParagraphs()
    {
        const string source = "<script>alert('not executed')</script>\n\n<iframe src=\"https://example.test\"></iframe>";
        var blocks = MarkdownParser.Parse(source, new MarkdownOptions());

        Assert.IsNotEmpty(blocks);
        Assert.IsTrue(blocks.All(block => block.Kind.ToString() == "Paragraph"));
        string renderedText = string.Concat(blocks.Select(Text));
        StringAssert.Contains(renderedText, "<script>");
        StringAssert.Contains(renderedText, "<iframe");
    }

    [TestMethod]
    public void OrderedListRetainsStartAndNestedItems()
    {
        var blocks = MarkdownParser.Parse("3. third\n4. fourth\n   - nested", new MarkdownOptions());
        var list = blocks.Single(block => block.Kind.ToString() == "List");

        Assert.AreEqual(3, list.Level);
        Assert.AreEqual("3.", list.Children[0].Marker);
        Assert.IsGreaterThanOrEqualTo(2, list.Children.Count);
        Assert.IsTrue(list.Children.Any(child => child.Kind.ToString() == "ListItem" && child.Children.Count > 0));
    }

    [TestMethod]
    public void PipeTablesTasksAndStrikethroughProduceStructuredBlocks()
    {
        var blocks = MarkdownParser.Parse("| Name | Done |\n| :--- | ---: |\n| one | - [x] yes |", new MarkdownOptions());
        var table = blocks.Single(block => block.Kind.ToString() == "Table");

        Assert.IsTrue(table.Children.Any(child => child.Kind.ToString() == "TableRow"));
        Assert.IsTrue(table.Children.SelectMany(row => row.Children).Any(cell => cell.Kind.ToString() == "TableCell"));
        Assert.IsTrue(table.Children.SelectMany(row => row.Children).SelectMany(cell => DescendantSpans(cell)).Any(span => span.Text.Contains("yes", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void OptionsDisableExtensionsAndControlSoftBreaks()
    {
        var options = new MarkdownOptions() with
        {
            UsePipeTables = false,
            UseTaskLists = false,
            UseAutoLinks = false,
            UseStrikethrough = false,
            UseInserted = false,
            UseMarked = false,
            SoftBreakAsNewLine = true
        };
        var blocks = MarkdownParser.Parse("www.example.test\n~~no~~ ++inserted++ ==marked==\n- [x] task\n| a | b |\n| - | - |", options);

        Assert.IsFalse(blocks.Any(block => block.Kind.ToString() == "Table"));
        Assert.IsFalse(blocks.SelectMany(block => block.Spans).Any(span => span.Strike));
        Assert.IsFalse(blocks.SelectMany(DescendantSpans).Any(span => span.Inserted || span.Marked));
        Assert.IsTrue(string.Concat(blocks.SelectMany(DescendantSpans).Select(span => span.Text)).Contains("[x]", StringComparison.Ordinal));
        StringAssert.Contains(Text(blocks[0]), "\n");
    }

    [TestMethod]
    public void ListsPreserveLooseStateAndTaskState()
    {
        var tight = MarkdownParser.Parse("- one\n- two", new MarkdownOptions()).Single();
        var loose = MarkdownParser.Parse("- first paragraph\n\n  second paragraph\n\n- next", new MarkdownOptions()).Single();
        var tasks = MarkdownParser.Parse("- [ ] open\n- [x] done", new MarkdownOptions()).Single();

        Assert.IsFalse(tight.Loose);
        Assert.IsTrue(loose.Loose);
        var taskSpans = tasks.Children.SelectMany(DescendantSpans)
            .Where(span => span.TaskChecked.HasValue).ToArray();
        Assert.HasCount(2, taskSpans);
        Assert.IsFalse(taskSpans[0].TaskChecked);
        Assert.IsTrue(taskSpans[1].TaskChecked);
    }

    [TestMethod]
    public void UnknownConstructsRemainLiteral()
    {
        const string source = "::: unknown\ncontent\n:::";
        var block = MarkdownParser.Parse(source, new MarkdownOptions())[0];

        StringAssert.Contains(Text(block), "::: unknown");
        StringAssert.Contains(Text(block), "content");
    }

    [TestMethod]
    public void UnclosedFencePreservesTrailingLine()
    {
        var block = MarkdownParser.Parse("```text\ntrailing", new MarkdownOptions())[0];

        Assert.AreEqual("Code", block.Kind.ToString());
        StringAssert.Contains(Text(block), "trailing");
    }

    [TestMethod]
    public void EmptyAltImageAndAdjacentStyledLinkFragmentsArePreserved()
    {
        var blocks = MarkdownParser.Parse("![](empty.png) [**a**_b_](target)", new MarkdownOptions());
        var spans = blocks.SelectMany(DescendantSpans).ToArray();
        var image = spans.Single(span => span.Image);
        var links = spans.Where(span => span.Url is not null && !span.Image).ToArray();

        Assert.AreEqual(string.Empty, image.Text);
        Assert.HasCount(2, links);
        Assert.IsTrue(links.All(span => span.Url == "target"));
        Assert.IsTrue(links.All(span => span.SourceLength == "[**a**_b_](target)".Length));
    }

    [TestMethod]
    public void TableAlignmentAndHeadingAnchorsAreMapped()
    {
        var blocks = MarkdownParser.Parse("# Heading\n\n| Left | Center | Right |\n| :--- | :---: | ---: |\n| a | b | c |", new MarkdownOptions());
        var heading = blocks.Single(block => block.Kind.ToString() == "Heading");
        var table = blocks.Single(block => block.Kind.ToString() == "Table");
        var cells = table.Children[0].Children.ToArray();

        Assert.AreEqual("heading", heading.Anchor);
        Assert.HasCount(3, cells);
        Assert.AreEqual("Left", cells[0].Alignment.ToString());
        Assert.AreEqual("Center", cells[1].Alignment.ToString());
        Assert.AreEqual("Right", cells[2].Alignment.ToString());
    }

    private static string Text(MarkdownBlock block)
    {
        return string.Concat(block.Spans.Select(span => span.Text));
    }

    private static IEnumerable<MarkdownSpan> DescendantSpans(MarkdownBlock block)
    {
        foreach (var span in block.Spans)
        {
            yield return span;
        }
        foreach (var child in block.Children)
        {
            foreach (var span in DescendantSpans(child))
            {
                yield return span;
            }
        }
    }
}

[TestClass]
[DoNotParallelize]
public sealed class MarkdownParagraphTests
{
    [TestMethod]
    public void NarrowLayoutHitTestsLinksAcrossWrappedLines()
    {
        RequireWindows();
        var factory = EnsureGdi();
        {
            var spans = new[]
            {
                new MarkdownSpan("prefix "),
                new MarkdownSpan("first link", Url: "https://one"),
                new MarkdownSpan(" filler text that wraps "),
                new MarkdownSpan("second link", Url: "https://two")
            };
            using var paragraph = new MarkdownParagraph(spans, new MarkdownTheme(), _ => { })
            {
                FontSize = 16,
                Foreground = Color.FromArgb(255, 20, 20, 20)
            };
            paragraph.Measure(new Size(120, 200));
            paragraph.Arrange(new Rect(0, 0, 120, 200));
            var layout = paragraph.GetLayout(120);
            var bounds = new List<Rect>();
            layout.GetRangeBounds("prefix ".Length, "first link".Length, bounds);
            Assert.IsNotEmpty(bounds);
            Assert.IsGreaterThanOrEqualTo(0, paragraph.HitLink(Center(bounds[0])));

            bounds.Clear();
            layout.GetRangeBounds("prefix first link filler text that wraps ".Length, "second link".Length, bounds);
            Assert.IsNotEmpty(bounds);
            Assert.AreEqual(1, paragraph.HitLink(Center(bounds[^1])));
            Assert.AreEqual(-1, paragraph.HitLink(new Point(119, 199)));
        }
    }

    [TestMethod]
    public void KeyboardNavigationActivatesNextLinkAndDisabledParagraphDoesNotActivate()
    {
        RequireWindows();
        var factory = EnsureGdi();
        {
            var activated = new List<string>();
            using var paragraph = new MarkdownParagraph(
                [new MarkdownSpan("one", Url: "https://one"), new MarkdownSpan(" and "), new MarkdownSpan("two", Url: "https://two")],
                new MarkdownTheme(), span => activated.Add(span.Url!));
            paragraph.GetLayout(200);
            var tab = new KeyEventArgs(Key.Tab, 0);
            paragraph.HandleKey(tab);
            var enter = new KeyEventArgs(Key.Enter, 0);
            paragraph.HandleKey(enter);
            CollectionAssert.AreEqual(new[] { "https://two" }, activated);

            paragraph.IsEnabled = false;
            paragraph.HandleKey(new KeyEventArgs(Key.Enter, 0));
            Assert.HasCount(1, activated);
        }
    }

    [TestMethod]
    public void RenderedParagraphProducesVisiblePixelsAndReleasesTextCache()
    {
        RequireWindows();
        var factory = EnsureGdi();
        int initialCacheCount = factory.TextEngine.ManagedCache.Count;
        {
            using var paragraph = new MarkdownParagraph([new MarkdownSpan("한글 🌍 **link**", Bold: true)], new MarkdownTheme(), _ => { })
            {
                Foreground = Color.FromArgb(255, 25, 25, 25),
                FontSize = 16
            };
            paragraph.Measure(new Size(200, 50));
            paragraph.Arrange(new Rect(0, 0, 200, 50));
            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(200, 50, 1));
            using (var context = factory.CreateContext(surface))
            {
                context.BeginFrame(surface);
                paragraph.Render(context);
                context.EndFrame();
            }

            var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
            Assert.IsTrue(pixels.ToArray().Chunk(4).Any(pixel => pixel[3] > 16));
            Assert.IsGreaterThan(initialCacheCount, factory.TextEngine.ManagedCache.Count);
            paragraph.Dispose();
        }
        Assert.AreEqual(initialCacheCount, factory.TextEngine.ManagedCache.Count);
    }

    [TestMethod]
    public void ImageSpanUsesInlineLayoutAndKeepsSurroundingTextInTheParagraph()
    {
        RequireWindows();
        EnsureGdi();
        var resolver = new ImmediateResolver(new TestVectorImageSource(new Size(80, 20)));
        using var paragraph = new MarkdownParagraph(
            [new MarkdownSpan("before "), new MarkdownSpan("alt", Url: "demo:image", Image: true), new MarkdownSpan(" after")],
            new MarkdownTheme(),
            _ => { },
            null,
            resolver)
        {
            FontSize = 16
        };

        paragraph.Measure(new Size(500, 100));
        var layout = paragraph.GetLayout(500);

        using var fallback = new MarkdownParagraph(
            [new MarkdownSpan("before alt after")],
            new MarkdownTheme(),
            _ => { })
        {
            FontSize = 16
        };
        fallback.Measure(new Size(500, 100));

        Assert.IsGreaterThan(fallback.DesiredSize.Width + 30, layout.MeasuredSize.Width);
        Assert.IsGreaterThanOrEqualTo(fallback.DesiredSize.Height, layout.MeasuredSize.Height);
    }

    [TestMethod]
    public void InlineImageFitsWithinParagraphWidth()
    {
        RequireWindows();
        EnsureGdi();
        using var paragraph = new MarkdownParagraph(
            [new MarkdownSpan("wide", Url: "demo:image", Image: true)],
            new MarkdownTheme(),
            _ => { },
            null,
            new ImmediateResolver(new TestVectorImageSource(new Size(1600, 120))));

        paragraph.Measure(new Size(240, 200));

        Assert.IsLessThanOrEqualTo(240, paragraph.DesiredSize.Width);
    }

    [TestMethod]
    public void DelayedInlineImageInvalidatesLayoutAndReleasesLeaseOnDispose()
    {
        RequireWindows();
        EnsureGdi();
        var pending = new TaskCompletionSource<MarkdownImageLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int releases = 0;
        var context = new PumpSynchronizationContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            using var paragraph = new MarkdownParagraph(
                [new MarkdownSpan("before "), new MarkdownSpan("delayed", Url: "demo:image", Image: true), new MarkdownSpan(" after")],
                new MarkdownTheme(),
                _ => { },
                null,
                new PendingInlineResolver(pending));
            paragraph.Measure(new Size(500, 100));
            double fallbackWidth = paragraph.DesiredSize.Width;

            pending.SetResult(new MarkdownImageLease(new TestVectorImageSource(new Size(80, 20)), () => releases++));
            Assert.IsTrue(SpinWait.SpinUntil(() => context.Count > 0, TimeSpan.FromSeconds(2)));
            context.Drain();
            paragraph.Measure(new Size(500, 100));

            Assert.IsGreaterThan(fallbackWidth + 30, paragraph.DesiredSize.Width);
            paragraph.Dispose();
            Assert.AreEqual(1, releases);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static Point Center(Rect rectangle) =>
        new(rectangle.X + Math.Max(1, rectangle.Width * 0.5), rectangle.Y + Math.Max(1, rectangle.Height * 0.5));

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
    }

    private static GdiGraphicsFactory EnsureGdi()
    {
        GdiBackend.Register();
        return (GdiGraphicsFactory)Application.DefaultGraphicsFactory;
    }

    private sealed class ImmediateResolver(IImageSource source) : IMarkdownImageResolver
    {
        public ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken)
            => new(new MarkdownImageLease(source));
    }

    private sealed class TestVectorImageSource(Size size) : IVectorImageSource
    {
        public Size IntrinsicSize => size;

        public IImage CreateImage(IGraphicsFactory factory) => throw new InvalidOperationException();

        public void Render(IGraphicsContext context, Rect destRect) { }
    }

    private sealed class PendingInlineResolver(TaskCompletionSource<MarkdownImageLease?> pending) : IMarkdownImageResolver
    {
        public ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken)
            => new(pending.Task);
    }

    private sealed class PumpSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int Count
        {
            get
            {
                lock (_queue)
                {
                    return _queue.Count;
                }
            }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
            {
                _queue.Enqueue((d, state));
            }
        }

        public void Drain()
        {
            while (true)
            {
                (SendOrPostCallback Callback, object? State) work;
                lock (_queue)
                {
                    if (_queue.Count == 0)
                    {
                        return;
                    }
                    work = _queue.Dequeue();
                }
                work.Callback(work.State);
            }
        }
    }
}

[TestClass]
[DoNotParallelize]
public sealed class MarkdownPresenterTests
{
    [TestMethod]
    public void ViewerForwardsPaddingToOwnedScrollViewer()
    {
        EnsureGdi();
        using var viewer = new MarkdownViewer
        {
            Markdown = "content",
            Padding = new Thickness(12, 8, 6, 4)
        };

        viewer.Measure(new Size(320, 200));

        var scroll = Assert.IsInstanceOfType<ScrollViewer>(viewer.DocumentRoot);
        Assert.AreEqual(viewer.Padding, scroll.Padding);
    }

    [TestMethod]
    public void BaseUriKeepsParsedDocumentButOptionsReparseIt()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter { Markdown = "[link](page)" };
        presenter.Measure(new Size(240, 100));
        presenter.Arrange(new Rect(0, 0, 240, presenter.DesiredSize.Height));
        presenter.Arrange(new Rect(0, 0, 240, 100));
        var parsed = presenter.Document;

        presenter.BaseUri = new Uri("https://example.test/docs/");
        Assert.AreSame(parsed, presenter.Document);
        presenter.Options = new MarkdownOptions { UseAutoLinks = false };
        Assert.AreNotSame(parsed, presenter.Document);
    }

    [TestMethod]
    public void ImageWithoutResolverDoesNotPerformIo()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter { Markdown = "![alt](image.png)" };
        presenter.Measure(new Size(240, 100));
        Assert.IsNull(presenter.ImageResolver);
        Assert.IsNotNull(presenter.Document);
    }

    [TestMethod]
    public void CodeFactoryReceivesNormalizedLanguageAndDoesNotReparseDocument()
    {
        EnsureGdi();
        string? receivedText = null;
        string? receivedLanguage = null;
        using var presenter = new MarkdownPresenter
        {
            Markdown = "```CSharp extra\nvalue\n```",
            CodeBlockFactory = (text, language) =>
            {
                receivedText = text;
                receivedLanguage = language;
                return new TextBlock { Text = text };
            }
        };
        presenter.Measure(new Size(240, 100));
        presenter.Arrange(new Rect(0, 0, 240, 100));
        var factory = (GdiGraphicsFactory)Application.DefaultGraphicsFactory;
        using (var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(240, 100, 1)))
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            presenter.Render(context);
            context.EndFrame();
        }
        var parsed = presenter.Document;
        presenter.Markdown = presenter.Markdown;
        presenter.Measure(new Size(240, 100));

        Assert.AreSame(parsed, presenter.Document);
        Assert.AreEqual("value", receivedText);
        Assert.AreEqual("csharp", receivedLanguage);
        presenter.CodeBlockFactory = (_, _) => new TextBlock { Text = "replacement" };
        Assert.AreSame(parsed, presenter.Document);
    }

    [TestMethod]
    public void CodeFactoryRejectsAttachedElement()
    {
        EnsureGdi();
        var host = new StackPanel();
        var attached = new TextBlock();
        host.Add(attached);
        using var presenter = new MarkdownPresenter
        {
            Markdown = "```text\nvalue\n```",
            CodeBlockFactory = (_, _) => attached
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => presenter.Measure(new Size(240, 100)));
    }

    [TestMethod]
    public void CodeCopyButtonIsAFlatNonMeasuringOverlay()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter { Markdown = "```text\nvalue\n```" };

        presenter.Measure(new Size(240, 100));

        var button = Descendants(presenter.DocumentRoot!).OfType<Button>()
            .Single(element => element.Content is PathShape);
        var icon = (PathShape)button.Content!;
        var overlay = Assert.IsInstanceOfType<Canvas>(button.Parent);
        Assert.AreEqual(BuiltInStyles.FlatButton, button.StyleName);
        Assert.AreEqual(20, button.Width);
        Assert.AreEqual(20, button.Height);
        Assert.AreEqual(20, button.MinWidth);
        Assert.AreEqual(20, button.MinHeight);
        Assert.AreEqual(Thickness.Zero, button.Padding);
        Assert.AreEqual(new Rect(0, 0, 22, 22), icon.ViewBox);
        Assert.AreEqual(HorizontalAlignment.Center, icon.HorizontalAlignment);
        Assert.AreEqual(VerticalAlignment.Center, icon.VerticalAlignment);
        Assert.AreEqual(Size.Empty, overlay.DesiredSize);
        Assert.AreEqual(4, Canvas.GetTop(button));
        Assert.AreEqual(4, Canvas.GetRight(button));
        Assert.IsTrue(button.Transitions!.Any(transition => transition.Property == UIElement.OpacityProperty));
        Assert.IsTrue(button.Triggers!.Any(trigger => trigger.Property == UIElement.IsMouseOverProperty));
        var language = Descendants(overlay).OfType<TextBlock>().Single(text => text.Text == "text");
        Color lightDisabledText = new Palette(ThemeManager.DefaultLightSeed,
            ThemeManager.DefaultAccentColor ?? ThemeManager.DefaultAccent.GetAccentColor(false)).DisabledText;
        Color darkDisabledText = new Palette(ThemeManager.DefaultDarkSeed,
            ThemeManager.DefaultAccentColor ?? ThemeManager.DefaultAccent.GetAccentColor(true)).DisabledText;
        Assert.IsTrue(language.Foreground == lightDisabledText || language.Foreground == darkDisabledText);
    }

    [TestMethod]
    public void TaskListsUseReadOnlyCheckboxesAndHideBulletMarkers()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter { Markdown = "- [ ] open\n- [x] done" };

        presenter.Measure(new Size(320, 160));
        presenter.Arrange(new Rect(0, 0, 320, presenter.DesiredSize.Height));

        var checkBoxes = Descendants(presenter.DocumentRoot!).OfType<CheckBox>().ToArray();
        Assert.HasCount(2, checkBoxes);
        Assert.IsFalse(checkBoxes[0].IsChecked);
        Assert.IsTrue(checkBoxes[1].IsChecked);
        Assert.IsTrue(checkBoxes.All(checkBox => !checkBox.IsEnabled && !checkBox.IsHitTestVisible));

        foreach (var checkBox in checkBoxes)
        {
            var paragraph = Descendants(checkBox.Parent!).OfType<MarkdownParagraph>().Single();
            double visualCenter = paragraph.GetFirstLineVisualCenter(paragraph.Bounds.Width);
            double expectedY = paragraph.Bounds.Y + Math.Max(0, visualCenter - checkBox.Bounds.Height * 0.5);
            Assert.AreEqual(expectedY, checkBox.Bounds.Y, 0.01);
        }

        var list = Descendants(presenter.DocumentRoot!).OfType<Grid>()
            .Single(grid => grid.ColumnDefinitions.Count == 2 && Descendants(grid).OfType<CheckBox>().Count() == 2);
        Assert.IsTrue(list.Children.OfType<TextBlock>().All(marker => marker.Text == string.Empty));
    }

    [TestMethod]
    public void ListUsesSharedMarkerColumnAlignedToFirstContentLine()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter
        {
            Markdown = "1. First paragraph.\n\n   > Nested quote.\n\n       nested_code();\n\n2. Second item."
        };
        presenter.Measure(new Size(480, 400));

        var grids = Descendants(presenter.DocumentRoot!).OfType<Grid>().ToArray();
        var list = grids.Single(grid => grid.ColumnDefinitions.Count == 2 &&
            Descendants(grid).OfType<TextBlock>().Count(text => text.Text is "1." or "2.") == 2);
        var markers = Descendants(list).OfType<TextBlock>().Where(text => text.Text is "1." or "2.").ToArray();

        Assert.AreEqual(Thickness.Zero, list.Margin);
        Assert.AreEqual(32d, list.ColumnDefinitions[0].Width.Value);
        Assert.IsTrue(list.ColumnDefinitions[0].Width.IsAbsolute);
        Assert.HasCount(2, markers);
        Assert.IsTrue(markers.All(marker => marker.HorizontalAlignment == HorizontalAlignment.Right));
        Assert.IsTrue(markers.All(marker => marker.VerticalAlignment == VerticalAlignment.Top));
        Assert.AreEqual(0, Grid.GetRow(markers[0]));
        Assert.AreEqual(1, Grid.GetRow(markers[1]));
    }

    [TestMethod]
    public void NestedListsUseOneFixedHangingIndentPerLevel()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter
        {
            Markdown = "- parent\n  - child\n    - grandchild"
        };
        presenter.Measure(new Size(480, 240));
        presenter.Arrange(new Rect(0, 0, 480, presenter.DesiredSize.Height));

        var lists = Descendants(presenter.DocumentRoot!).OfType<Grid>()
            .Where(grid => grid.ColumnDefinitions.Count == 2 &&
                grid.ColumnDefinitions[0].Width.IsAbsolute &&
                grid.ColumnDefinitions[0].Width.Value == 32)
            .ToArray();

        Assert.HasCount(3, lists);
        Assert.IsTrue(lists.All(list => list.Margin == Thickness.Zero));

        var paragraphs = Descendants(presenter.DocumentRoot!).OfType<MarkdownParagraph>()
            .ToDictionary(paragraph => paragraph.Text);
        Assert.AreEqual(32d, paragraphs["child"].Bounds.X - paragraphs["parent"].Bounds.X, 0.01);
        Assert.AreEqual(32d, paragraphs["grandchild"].Bounds.X - paragraphs["child"].Bounds.X, 0.01);
    }

    [TestMethod]
    public void ListIndentComesFromMarkdownTheme()
    {
        EnsureGdi();
        using var presenter = new MarkdownPresenter
        {
            Markdown = "- parent\n  - child",
            MarkdownTheme = new MarkdownTheme { ListIndent = 24 }
        };
        presenter.Measure(new Size(320, 160));
        presenter.Arrange(new Rect(0, 0, 320, presenter.DesiredSize.Height));

        var paragraphs = Descendants(presenter.DocumentRoot!).OfType<MarkdownParagraph>()
            .ToDictionary(paragraph => paragraph.Text);
        Assert.AreEqual(24d, paragraphs["child"].Bounds.X - paragraphs["parent"].Bounds.X, 0.01);
    }

    [TestMethod]
    public void ImageLeaseReleasesExactlyOnce()
    {
        int releases = 0;
        using var lease = new MarkdownImageLease(new StubImageSource(), () => releases++);
        lease.Dispose();
        lease.Dispose();
        Assert.AreEqual(1, releases);
    }

    [TestMethod]
    public async Task ImageCompletionAfterDisposeReleasesLateLeaseOnce()
    {
        EnsureGdi();
        var pending = new TaskCompletionSource<MarkdownImageLease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = new PendingResolver(pending);
        var image = new MarkdownImage(new MarkdownSpan("alt", Url: "image.png", Image: true), null, resolver);
        image.Measure(new Size(200, 100));
        image.Dispose();

        pending.SetResult(new MarkdownImageLease(new StubImageSource(), () => released.TrySetResult(true)));
        await released.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsTrue(resolver.WasCalled);
    }

    private static GdiGraphicsFactory EnsureGdi()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }
        GdiBackend.Register();
        return (GdiGraphicsFactory)Application.DefaultGraphicsFactory;
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

    private sealed class StubImageSource : IImageSource
    {
        public IImage CreateImage(IGraphicsFactory factory) => throw new InvalidOperationException("Resolver must not be called.");
    }

    private sealed class PendingResolver(TaskCompletionSource<MarkdownImageLease?> pending) : IMarkdownImageResolver
    {
        public bool WasCalled { get; private set; }

        public ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return new ValueTask<MarkdownImageLease?>(pending.Task);
        }
    }

}
