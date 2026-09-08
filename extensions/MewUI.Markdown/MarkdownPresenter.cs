using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Markdown;

/// <summary>Displays Markdown without owning a scroll viewport; properties must be changed on the UI thread.</summary>
public class MarkdownPresenter : Control, ISubtreeInvalidationHost, ILogicalTreeHost
{
    private const double LIST_MARKER_SPACING = 8;

    /// <summary>Identifies the document source property.</summary>
    public static readonly MewProperty<string> MarkdownProperty = MewProperty<string>.Register<MarkdownPresenter>(
        nameof(Markdown), string.Empty, MewPropertyOptions.AffectsLayout, static (self, _, _) => self.InvalidateDocument(true));
    /// <summary>Identifies the immutable parsing options property.</summary>
    public static readonly MewProperty<MarkdownOptions> OptionsProperty = MewProperty<MarkdownOptions>.Register<MarkdownPresenter>(
        nameof(Options), new MarkdownOptions(), MewPropertyOptions.AffectsLayout, static (self, _, _) => self.InvalidateDocument(true));
    /// <summary>Identifies the document theme property.</summary>
    public static readonly MewProperty<MarkdownTheme> MarkdownThemeProperty = MewProperty<MarkdownTheme>.Register<MarkdownPresenter>(
        nameof(MarkdownTheme), new MarkdownTheme(), MewPropertyOptions.AffectsLayout, static (self, _, _) => self.InvalidateDocument(false));
    /// <summary>Identifies the URI resolution base property.</summary>
    public static readonly MewProperty<Uri?> BaseUriProperty = MewProperty<Uri?>.Register<MarkdownPresenter>(
        nameof(BaseUri), null, MewPropertyOptions.AffectsLayout, static (self, _, _) => self.InvalidateDocument(false));
    /// <summary>Identifies the host-authorized image resolver property.</summary>
    public static readonly MewProperty<IMarkdownImageResolver?> ImageResolverProperty = MewProperty<IMarkdownImageResolver?>.Register<MarkdownPresenter>(
        nameof(ImageResolver), null, MewPropertyOptions.AffectsLayout, static (self, _, _) => self.InvalidateDocument(false));
    /// <summary>Identifies the optional code block presentation factory.</summary>
    public static readonly MewProperty<Func<string, string?, FrameworkElement?>?> CodeBlockFactoryProperty = MewProperty<Func<string, string?, FrameworkElement?>?>.Register<MarkdownPresenter>(
        nameof(CodeBlockFactory), null, MewPropertyOptions.AffectsLayout, static (self, _, _) => self.InvalidateDocument(false));
    /// <summary>Identifies the custom renderer registry property.</summary>
    public static readonly MewProperty<MarkdownRenderers?> RenderersProperty = MewProperty<MarkdownRenderers?>.Register<MarkdownPresenter>(
        nameof(Renderers), null, MewPropertyOptions.AffectsLayout, static (self, _, _) => self.InvalidateDocument(false));
    /// <summary>Identifies the background parse delay property.</summary>
    public static readonly MewProperty<TimeSpan> ParseDelayProperty = MewProperty<TimeSpan>.Register<MarkdownPresenter>(
        nameof(ParseDelay), TimeSpan.Zero);

    private readonly bool _scrollable;
    private ScrollViewer? _scroll;
    private FrameworkElement? _blocks;
    private MarkdownBlockHost? _host;
    private ParsedMarkdown? _document;
    private MarkdownRenderContext? _context;
    private readonly Dictionary<string, FrameworkElement> _anchors = new(StringComparer.OrdinalIgnoreCase);
    private (int Index, double Within)? _carriedAnchor;
    private CancellationTokenSource? _parseCancellation;
    private int _parseRevision;
    private bool _parsePending;
    private bool _disposed;

    /// <summary>Creates a presenter with no internal scroll viewer.</summary>
    public MarkdownPresenter() { }

    private protected MarkdownPresenter(bool scrollable) => _scrollable = scrollable;

    /// <summary>Gets or sets the Markdown source; equal values preserve the current tree.</summary>
    public string Markdown { get => GetValue(MarkdownProperty); set => SetValue(MarkdownProperty, value ?? string.Empty); }
    /// <summary>Gets or sets immutable parsing options.</summary>
    public MarkdownOptions Options { get => GetValue(OptionsProperty); set { ArgumentNullException.ThrowIfNull(value); SetValue(OptionsProperty, value); } }
    /// <summary>Gets or sets document metrics and color overrides without reparsing.</summary>
    public MarkdownTheme MarkdownTheme { get => GetValue(MarkdownThemeProperty); set { ArgumentNullException.ThrowIfNull(value); SetValue(MarkdownThemeProperty, value); } }
    /// <summary>Gets or sets the base URI used for relative resources without authorizing access.</summary>
    public Uri? BaseUri { get => GetValue(BaseUriProperty); set => SetValue(BaseUriProperty, value); }
    /// <summary>Gets or sets the image resolver; null displays alternative text without I/O.</summary>
    public IMarkdownImageResolver? ImageResolver { get => GetValue(ImageResolverProperty); set => SetValue(ImageResolverProperty, value); }
    /// <summary>Gets or sets a UI-thread code factory receiving text and language; returned unattached elements transfer ownership to the presenter, and null uses plain text.</summary>
    public Func<string, string?, FrameworkElement?>? CodeBlockFactory { get => GetValue(CodeBlockFactoryProperty); set => SetValue(CodeBlockFactoryProperty, value); }
    /// <summary>Gets or sets custom renderers consulted before the default presentation; assign a configured instance.</summary>
    public MarkdownRenderers? Renderers { get => GetValue(RenderersProperty); set => SetValue(RenderersProperty, value); }
    /// <summary>Gets or sets the debounce delay after which source changes parse on a worker thread; zero parses synchronously on the UI thread.</summary>
    public TimeSpan ParseDelay { get => GetValue(ParseDelayProperty); set => SetValue(ParseDelayProperty, value < TimeSpan.Zero ? TimeSpan.Zero : value); }
    /// <summary>Requests host handling of a link; no external navigation occurs automatically.</summary>
    public event Action<MarkdownLinkRequestedEventArgs>? LinkRequested;
    /// <summary>Reports a failed background parse; the previous document stays displayed.</summary>
    public event Action<Exception>? ParseFailed;

    internal ParsedMarkdown Document => _document ??= MarkdownParser.ParseDocument(Markdown, Options);
    internal Element? DocumentRoot => _scroll ?? (Element?)_blocks;
    internal MarkdownBlockHost? BlockHost => _host;
    internal bool IsParsePending => _parsePending;

    private void InvalidateDocument(bool parse)
    {
        if (parse)
        {
            if (ParseDelay > TimeSpan.Zero && !_disposed)
            {
                // The previous tree stays visible until the newest revision arrives.
                StartBackgroundParse();
                return;
            }
            CancelBackgroundParse();
            _document = null;
            // A different document starts at the top; re-rendering the same one keeps its position.
            _scroll?.SetScrollOffsets(0, 0);
            _carriedAnchor = null;
        }
        else if (_host != null && _scroll != null)
        {
            // The rebuilt tree puts the same block back at the viewport top.
            int index = _host.IndexAt(_scroll.VerticalOffset);
            _carriedAnchor = (index, _scroll.VerticalOffset - _host.GetBlockTop(index));
        }
        ClearBlocks();
        InvalidateMeasure();
    }

    private void CancelBackgroundParse()
    {
        _parseCancellation?.Cancel();
        _parseCancellation?.Dispose();
        _parseCancellation = null;
        _parsePending = false;
    }

    private void StartBackgroundParse()
    {
        CancelBackgroundParse();
        var cancellation = new CancellationTokenSource();
        _parseCancellation = cancellation;
        _parsePending = true;
        int revision = ++_parseRevision;
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        _ = ParseAsync(Markdown, Options, ParseDelay, revision, cancellation.Token, dispatcher, SynchronizationContext.Current);
    }

    private async Task ParseAsync(string markdown, MarkdownOptions options, TimeSpan delay, int revision,
        CancellationToken cancellation, IDispatcher? dispatcher, SynchronizationContext? synchronization)
    {
        ParsedMarkdown? parsed = null;
        Exception? failure = null;
        try
        {
            await Task.Delay(delay, cancellation).ConfigureAwait(false);
            parsed = MarkdownParser.ParseDocument(markdown, options);
            cancellation.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception error)
        {
            failure = error;
        }

        void Apply() => ApplyParse(revision, parsed, failure);
        if (dispatcher != null)
        {
            dispatcher.BeginInvoke(Apply);
        }
        else if (synchronization != null)
        {
            synchronization.Post(_ => Apply(), null);
        }
        else
        {
            Apply();
        }
    }

    private void ApplyParse(int revision, ParsedMarkdown? parsed, Exception? failure)
    {
        if (_disposed || revision != _parseRevision)
        {
            return;
        }
        _parsePending = false;
        if (failure != null)
        {
            ParseFailed?.Invoke(failure);
            return;
        }
        _document = parsed;
        _scroll?.SetScrollOffsets(0, 0);
        ClearBlocks();
        InvalidateMeasure();
    }

    private void ClearBlocks()
    {
        var previous = _blocks;
        _blocks = null;
        _host = null;
        _context = null;
        _anchors.Clear();
        if (previous != null)
        {
            if (_scroll != null)
            {
                _scroll.Content = null;
            }
            else
            {
                DetachChild(previous);
            }
            DisposeTree(previous);
        }
    }

    internal static void DisposeTree(Element element)
    {
        if (element is MarkdownImage image)
        {
            image.Dispose();
            return;
        }
        if (element is IVisualTreeHost host)
        {
            var children = new List<Element>();
            host.VisitChildren(child => { children.Add(child); return true; });
            foreach (var child in children)
            {
                DisposeTree(child);
            }
        }
        (element as IDisposable)?.Dispose();
    }

    private void EnsureTree()
    {
        if (_blocks != null || _disposed)
        {
            return;
        }
        ParsedMarkdown? document = _parsePending ? _document : Document;
        if (document == null)
        {
            return;
        }
        _context = new MarkdownRenderContext(this, document);
        if (_scrollable)
        {
            if (_scroll == null)
            {
                _scroll = new ScrollViewer();
                _scroll.SetBinding(PaddingProperty, this, PaddingProperty);
                AttachChild(_scroll);
            }
            _host = new MarkdownBlockHost(document.Blocks, MarkdownTheme.BlockSpacing, RenderBlock);
            if (_carriedAnchor is (int carriedIndex, double carriedWithin))
            {
                _host.SetInitialAnchor(carriedIndex, carriedWithin);
                _carriedAnchor = null;
            }
            _blocks = _host;
            _scroll.Content = _blocks;
        }
        else
        {
            _blocks = RenderBlocks(document.Blocks);
            AttachChild(_blocks);
        }
    }

    internal StackPanel RenderBlocks(IReadOnlyList<MarkdownBlock> blocks, double? spacing = null)
    {
        var panel = new StackPanel { Spacing = Math.Max(0, spacing ?? MarkdownTheme.BlockSpacing) };
        try
        {
            foreach (var block in blocks)
            {
                panel.Add(RenderBlock(block));
            }
            return panel;
        }
        catch
        {
            DisposeTree(panel);
            throw;
        }
    }

    private FrameworkElement RenderBlock(MarkdownBlock block)
    {
        if (block.Node != null && Renderers is MarkdownRenderers renderers && _context != null)
        {
            var custom = renderers.RenderBlock(block.Node, _context);
            if (custom != null)
            {
                if (custom.Parent != null || custom.LogicalParent != null)
                {
                    throw new InvalidOperationException("A block renderer must return an unattached element.");
                }
                return custom;
            }
        }
        switch (block.Kind)
        {
            case MarkdownBlockKind.Paragraph:
            case MarkdownBlockKind.Heading:
                return RenderParagraph(block);
            case MarkdownBlockKind.Code:
                return RenderCode(block);
            case MarkdownBlockKind.Rule:
                return new Border { Height = 1 }.WithTheme((theme, border) => border.Background = theme.Palette.ControlBorder);
            case MarkdownBlockKind.Quote:
                return new Border { Padding = new Thickness(12, 4), NonUniformBorderThickness = new(4,0,0,0),
                    Child = RenderBlocks(block.Children) }
                    .WithTheme((theme, border) => {
                        border.BorderBrush = theme.Palette.ControlBorder;
                        border.Background = theme.Palette.ButtonFace;
                    });
            case MarkdownBlockKind.List:
                return RenderList(block);
            case MarkdownBlockKind.Table:
                return RenderTable(block);
            case MarkdownBlockKind.DefinitionList:
                return RenderDefinitionList(block);
            default:
                return RenderBlocks(block.Children);
        }
    }

    private FrameworkElement RenderCode(MarkdownBlock block)
    {
        string text = string.Concat(block.Spans.Select(span => span.Text));
        string? language = block.Info?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        var custom = CodeBlockFactory?.Invoke(text, language);
        if (custom != null && (custom.Parent != null || custom.LogicalParent != null))
        {
            throw new InvalidOperationException("The code block factory must return an unattached element.");
        }
        var copyIcon = new PathShape
        {
            Data = PathGeometry.Parse("M7 7 L19 7 L19 19 L7 19 Z M5 17 L3 17 L3 3 L17 3 L17 5"),
            ViewBox = new Rect(0, 0, 22, 22),
            Stretch = Stretch.Uniform,
            Width = 16,
            Height = 16,
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var copy = new Button
        {
            Content = copyIcon,
            Width = 20,
            Height = 20,
            MinWidth = 20,
            MinHeight = 20,
            Padding = Thickness.Zero,
            StyleName = BuiltInStyles.FlatButton,
            ToolTip = new TextBlock { Text = "Copy" },
            Transitions = [Transition.Create(UIElement.OpacityProperty, 150)],
            Triggers =
            [
                ElementTrigger.When(UIElement.IsMouseOverProperty, false,
                    Setter.Create(UIElement.OpacityProperty, 0.5))
            ]
        };
        copyIcon.Bind(Shape.StrokeProperty, copy, TextElement.ForegroundProperty,
            static (Color color) => (Brush)new SolidColorBrush(color));
        copy.Click += () =>
        {
            if (Application.IsRunning)
            {
                Application.Current.PlatformServices.Clipboard?.TrySetText(text);
            }
        };
        var content = new ScrollViewer
        {
            Padding = new Thickness(8, language == null ? 8 : 28, 8, 8),
            HorizontalScroll = ScrollMode.Auto, VerticalScroll = ScrollMode.Disabled,
            Content = custom ?? new TextBlock { Text = text, FontFamily = MarkdownTheme.CodeFontFamily }
        };
        var overlay = new MarkdownOverlayCanvas();
        if (language != null)
        {
            var languageLabel = new TextBlock
            {
                Text = language,
                FontFamily = MarkdownTheme.CodeFontFamily,
                FontSize = Math.Max(10, FontSize * 0.75),
                IsHitTestVisible = false
            }.WithTheme((theme, label) => label.Foreground = theme.Palette.DisabledText);
            Canvas.SetLeft(languageLabel, 8);
            Canvas.SetTop(languageLabel, 5);
            overlay.Add(languageLabel);
        }
        Canvas.SetTop(copy, 4);
        Canvas.SetRight(copy, 4);
        overlay.Add(copy);
        var panel = new Grid();
        panel.Add(content);
        panel.Add(overlay);
        return new Border { Child = panel }
            .WithTheme((theme, border) => {
                border.Background = MarkdownTheme.CodeBackground ?? theme.Palette.ControlBackground;
                border.CornerRadius = theme.Metrics.ControlCornerRadius;
            });
    }

    private sealed class MarkdownOverlayCanvas : Canvas
    {
        protected override UIElement? OnHitTest(Point point)
        {
            var hit = base.OnHitTest(point);
            return ReferenceEquals(hit, this) ? null : hit;
        }
    }

    /// <summary>Creates a body paragraph for inline content, resolving registered inline renderers.</summary>
    internal FrameworkElement CreateParagraph(IReadOnlyList<MarkdownSpan> spans, bool heading = false, int level = 0, TextAlignment alignment = TextAlignment.Left)
    {
        IInlineTextObject?[]? objects = null;
        if (Renderers is MarkdownRenderers renderers && renderers.HasInlineRenderers && _context != null)
        {
            for (int index = 0; index < spans.Count; index++)
            {
                MarkdownSpan span = spans[index];
                if (span.Node == null || span.Image)
                {
                    continue;
                }
                IInlineTextObject? inlineObject = renderers.RenderInline(span.Node, _context);
                if (inlineObject == null)
                {
                    continue;
                }
                if (objects == null)
                {
                    objects = new IInlineTextObject?[spans.Count];
                    spans = spans.ToArray();
                }
                objects[index] = inlineObject;
                if (span.Text.Length == 0)
                {
                    // The object needs at least one column to occupy.
                    ((MarkdownSpan[])spans)[index] = span with { Text = ((char)0xFFFC).ToString() };
                }
            }
        }
        return new MarkdownParagraph(spans, MarkdownTheme, ActivateLink, BaseUri, ImageResolver, objects)
        {
            Heading = heading,
            FontScale = heading ? Math.Max(1.05, 2.0 - (level - 1) * 0.18) : 1,
            Alignment = alignment
        };
    }

    private FrameworkElement RenderParagraph(MarkdownBlock block)
    {
        MarkdownSpan? taskSpan = block.Spans.FirstOrDefault(span => span.TaskChecked.HasValue);
        IReadOnlyList<MarkdownSpan> contentSpans = taskSpan == null
            ? block.Spans
            : block.Spans.Where(span => !span.TaskChecked.HasValue).ToArray();

        FrameworkElement result = CreateParagraph(contentSpans, block.Kind == MarkdownBlockKind.Heading, block.Level, block.Alignment);
        if (taskSpan != null)
        {
            var task = new CheckBox
            {
                IsChecked = taskSpan.TaskChecked,
                IsEnabled = false,
                IsHitTestVisible = false,
                Width = 14,
                Height = 14,
                MinWidth = 14,
                MinHeight = 14,
                Padding = Thickness.Zero,
                VerticalAlignment = VerticalAlignment.Top
            };
            result = new MarkdownTaskPanel(task, result);
        }
        if (!string.IsNullOrEmpty(block.Anchor))
        {
            // Overwrite: a virtualized heading is re-created each time it is realized.
            _anchors[block.Anchor] = result;
        }
        return result;
    }

    private sealed class MarkdownTaskPanel : Panel
    {
        private const double BOX_SIZE = 14;
        private const double SPACING = 6;

        private readonly CheckBox _task;
        private readonly FrameworkElement _content;

        internal MarkdownTaskPanel(CheckBox task, FrameworkElement content)
        {
            _task = task;
            _content = content;
            Add(task);
            Add(content);
        }

        protected override Size MeasureContent(Size availableSize)
        {
            double contentWidth = double.IsFinite(availableSize.Width)
                ? Math.Max(0, availableSize.Width - BOX_SIZE - SPACING)
                : double.PositiveInfinity;
            _content.Measure(new Size(contentWidth, availableSize.Height));
            _task.Measure(new Size(BOX_SIZE, BOX_SIZE));
            return new Size(BOX_SIZE + SPACING + _content.DesiredSize.Width,
                Math.Max(BOX_SIZE, _content.DesiredSize.Height));
        }

        protected override void ArrangeContent(Rect bounds)
        {
            double contentX = bounds.X + BOX_SIZE + SPACING;
            double contentWidth = Math.Max(0, bounds.Width - BOX_SIZE - SPACING);
            _content.Arrange(new Rect(contentX, bounds.Y, contentWidth, bounds.Height));

            double firstLineCenter = _content is MarkdownParagraph paragraph
                ? paragraph.GetFirstLineVisualCenter(contentWidth)
                : Math.Min(bounds.Height, _content.DesiredSize.Height) * 0.5;
            double taskY = bounds.Y + Math.Max(0, firstLineCenter - BOX_SIZE * 0.5);
            _task.Arrange(new Rect(bounds.X, taskY, BOX_SIZE, BOX_SIZE));
        }
    }

    private FrameworkElement RenderList(MarkdownBlock block)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Math.Max(0, MarkdownTheme.ListIndent) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        for (int rowIndex = 0; rowIndex < block.Children.Count; rowIndex++)
        {
            var item = block.Children[rowIndex];
            bool taskItem = item.Children.FirstOrDefault()?.Spans.Any(span => span.TaskChecked.HasValue) == true;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var marker = new TextBlock
            {
                Text = taskItem ? string.Empty : string.IsNullOrWhiteSpace(item.Marker) ? "•" : item.Marker,
                Margin = new Thickness(0, 0, LIST_MARKER_SPACING, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                VerticalTextAlignment = TextAlignment.Top
            };
            var content = RenderBlocks(item.Children, block.Loose ? MarkdownTheme.BlockSpacing : 0);
            content.Margin = new Thickness(0, 0, 0, block.Loose ? 4 : 0);
            Grid.SetRow(marker, rowIndex);
            Grid.SetRow(content, rowIndex);
            Grid.SetColumn(content, 1);
            grid.Add(marker);
            grid.Add(content);
        }
        return grid;
    }

    private FrameworkElement RenderTable(MarkdownBlock block)
    {
        var grid = new Grid();
        int columns = block.Children.Count == 0 ? 0 : block.Children.Max(row => row.Children.Count);
        for (int column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }
        for (int rowIndex = 0; rowIndex < block.Children.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = block.Children[rowIndex];
            for (int column = 0; column < row.Children.Count; column++)
            {
                var cell = row.Children[column];
                var content = new ContentControl { Content = RenderBlocks(cell.Children) };
                if (row.Header || cell.Header)
                {
                    content.FontWeight = FontWeight.Bold;
                }
                var border = new Border { Padding = new Thickness(6), CornerRadius = 0,
                    NonUniformBorderThickness = new Thickness(1, 1, 0, 0),
                    BorderBrush = Theme.Palette.ControlBorder, Child = content }
                    .WithTheme((theme, element) => element.BorderBrush = theme.Palette.ControlBorder);
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, column);
                grid.Add(border);
            }
        }
        return new Border { Child = grid, Padding = Thickness.Zero, CornerRadius = 0,
            NonUniformBorderThickness = new Thickness(0, 0, 1, 1) }
            .WithTheme((theme, element) => element.BorderBrush = theme.Palette.ControlBorder);
    }

    private FrameworkElement RenderDefinitionList(MarkdownBlock block)
    {
        var list = new StackPanel { Spacing = Math.Max(0, MarkdownTheme.BlockSpacing) };
        foreach (var item in block.Children)
        {
            var itemPanel = new StackPanel { Spacing = Math.Max(0, MarkdownTheme.BlockSpacing / 2) };
            foreach (var child in item.Children)
            {
                FrameworkElement content;
                if (child.Kind == MarkdownBlockKind.DefinitionTerm)
                {
                    content = RenderParagraph(child);
                    if (content is TextElement term)
                    {
                        term.FontWeight = FontWeight.Bold;
                    }
                }
                else
                {
                    content = new Border
                    {
                        Margin = new Thickness(Math.Max(0, MarkdownTheme.ListIndent), 0, 0, 0),
                        Child = RenderBlock(child)
                    };
                }
                itemPanel.Add(content);
            }
            list.Add(itemPanel);
        }
        return list;
    }

    private bool IsAttached(Element element)
    {
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, this))
            {
                return true;
            }
        }
        return false;
    }

    internal static Uri? ResolveUri(string url, Uri? baseUri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }
        if (baseUri?.IsAbsoluteUri == true && Uri.TryCreate(baseUri, url, out var resolved))
        {
            return resolved;
        }
        return null;
    }

    private void ActivateLink(MarkdownSpan span)
    {
        string url = (span.Image ? span.LinkUrl : span.Url) ?? string.Empty;
        string? title = span.Image ? span.LinkTitle : span.Title;
        if (url.StartsWith('#'))
        {
            NavigateToAnchor(Uri.UnescapeDataString(url[1..]));
        }
        LinkRequested?.Invoke(new MarkdownLinkRequestedEventArgs(
            url, ResolveUri(url, BaseUri), title, span.SourceStart, span.SourceLength));
    }

    /// <summary>Scrolls the owned viewport to a heading anchor; returns false when the anchor is unknown or there is no viewport.</summary>
    internal bool NavigateToAnchor(string anchor)
    {
        if (_scroll == null)
        {
            return false;
        }
        if (_anchors.TryGetValue(anchor, out var target) && IsAttached(target))
        {
            _scroll.SetScrollOffsets(0, _scroll.VerticalOffset + target.Bounds.Y - _scroll.Bounds.Y);
            return true;
        }
        if (_host != null && _document != null && _document.AnchorBlocks.TryGetValue(anchor, out int blockIndex))
        {
            // The heading is not realized yet: land on its top-level block; the host lands it over the next layout passes.
            _host.RequestScrollToBlock(blockIndex);
            return true;
        }
        return false;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        EnsureTree();
        var root = DocumentRoot;
        if (_scrollable)
        {
            root?.Measure(availableSize);
            return root?.DesiredSize ?? Size.Empty;
        }
        root?.Measure(availableSize.Deflate(Padding));
        return (root?.DesiredSize ?? Size.Empty).Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds) =>
        DocumentRoot?.Arrange(_scrollable ? bounds : bounds.Deflate(Padding));

    protected override void RenderSubtree(IGraphicsContext context) => DocumentRoot?.Render(context);
    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => DocumentRoot is not Element root || visitor(root);
    bool ILogicalTreeHost.VisitLogicalChildren(Func<Element, bool> visitor) => DocumentRoot is not Element root || visitor(root);

    protected override void OnDispose()
    {
        _disposed = true;
        CancelBackgroundParse();
        ClearBlocks();
        if (_scroll != null)
        {
            DetachChild(_scroll);
            _scroll.Dispose();
            _scroll = null;
        }
        _document = null;
        LinkRequested = null;
        base.OnDispose();
    }
}
