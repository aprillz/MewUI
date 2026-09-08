using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Markdown;

/// <summary>Displays Markdown without owning a scroll viewport; properties must be changed on the UI thread.</summary>
public class MarkdownPresenter : Control, ISubtreeInvalidationHost, ILogicalTreeHost
{
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

    private readonly bool _scrollable;
    private Element? _root;
    private IReadOnlyList<MarkdownBlock>? _document;
    private readonly Dictionary<string, FrameworkElement> _anchors = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private double _savedOffset;

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
    /// <summary>Requests host handling of a link; no external navigation occurs automatically.</summary>
    public event Action<MarkdownLinkRequestedEventArgs>? LinkRequested;

    internal IReadOnlyList<MarkdownBlock> Document => _document ??= MarkdownParser.Parse(Markdown, Options);
    internal Element? DocumentRoot => _root;

    private void InvalidateDocument(bool parse)
    {
        if (parse)
        {
            _document = null;
        }
        if (_root is ScrollViewer scroll)
        {
            _savedOffset = scroll.VerticalOffset;
        }
        ClearTree();
        InvalidateMeasure();
    }

    private void ClearTree()
    {
        var previous = _root;
        _root = null;
        _anchors.Clear();
        if (previous != null)
        {
            DetachChild(previous);
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
        if (_root != null || _disposed)
        {
            return;
        }
        var blocks = RenderBlocks(Document);
        if (_scrollable)
        {
            var scroll = new ScrollViewer { Content = blocks };
            scroll.SetBinding(PaddingProperty, this, PaddingProperty);
            _root = scroll;
        }
        else
        {
            _root = blocks;
        }
        AttachChild(_root);
    }

    private StackPanel RenderBlocks(IReadOnlyList<MarkdownBlock> blocks, double? spacing = null)
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

    private FrameworkElement RenderParagraph(MarkdownBlock block)
    {
        FrameworkElement CreateText(IReadOnlyList<MarkdownSpan> spans) => new MarkdownParagraph(spans, MarkdownTheme, ActivateLink)
        {
            Heading = block.Kind == MarkdownBlockKind.Heading,
            FontScale = block.Kind == MarkdownBlockKind.Heading ? Math.Max(1.05, 2.0 - (block.Level - 1) * 0.18) : 1,
            Alignment = block.Alignment
        };

        MarkdownSpan? taskSpan = block.Spans.FirstOrDefault(span => span.TaskChecked.HasValue);
        IReadOnlyList<MarkdownSpan> contentSpans = taskSpan == null
            ? block.Spans
            : block.Spans.Where(span => !span.TaskChecked.HasValue).ToArray();

        FrameworkElement result;
        if (contentSpans.Any(span => span.Image))
        {
            var panel = new StackPanel { Spacing = Math.Max(0, MarkdownTheme.BlockSpacing) };
            var pending = new List<MarkdownSpan>();
            foreach (var span in contentSpans)
            {
                if (span.Image)
                {
                    if (pending.Count > 0)
                    {
                        panel.Add(CreateText(pending.ToArray()));
                        pending.Clear();
                    }
                    panel.Add(new MarkdownImage(span, BaseUri, ImageResolver));
                }
                else
                {
                    pending.Add(span);
                }
            }
            if (pending.Count > 0)
            {
                panel.Add(CreateText(pending.ToArray()));
            }
            result = panel;
        }
        else
        {
            result = CreateText(contentSpans);
        }
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
            _anchors.TryAdd(block.Anchor, result);
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

            double firstLineHeight = _content is MarkdownParagraph paragraph
                ? paragraph.GetFirstLineHeight(contentWidth)
                : Math.Min(bounds.Height, _content.DesiredSize.Height);
            double taskY = bounds.Y + Math.Max(0, (firstLineHeight - BOX_SIZE) / 2);
            _task.Arrange(new Rect(bounds.X, taskY, BOX_SIZE, BOX_SIZE));
        }
    }

    private FrameworkElement RenderList(MarkdownBlock block)
    {
        var grid = new Grid { Margin = new Thickness(40, 0, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        for (int rowIndex = 0; rowIndex < block.Children.Count; rowIndex++)
        {
            var item = block.Children[rowIndex];
            bool taskItem = item.Children.FirstOrDefault()?.Spans.Any(span => span.TaskChecked.HasValue) == true;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var marker = new TextBlock
            {
                Text = taskItem ? string.Empty : string.IsNullOrWhiteSpace(item.Marker) ? "•" : item.Marker,
                Margin = new Thickness(0, 0, 8, 0),
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
        string url = span.Url ?? string.Empty;
        if (url.StartsWith('#') && _root is ScrollViewer scroll &&
            _anchors.TryGetValue(Uri.UnescapeDataString(url[1..]), out var target))
        {
            scroll.SetScrollOffsets(0, scroll.VerticalOffset + target.Bounds.Y - scroll.Bounds.Y);
        }
        LinkRequested?.Invoke(new MarkdownLinkRequestedEventArgs(url, ResolveUri(url, BaseUri), span.Title, span.SourceStart, span.SourceLength));
    }

    protected override Size MeasureContent(Size availableSize)
    {
        EnsureTree();
        if (_scrollable)
        {
            _root?.Measure(availableSize);
            return _root?.DesiredSize ?? Size.Empty;
        }
        _root?.Measure(availableSize.Deflate(Padding));
        return (_root?.DesiredSize ?? Size.Empty).Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        _root?.Arrange(_scrollable ? bounds : bounds.Deflate(Padding));
        if (_root is ScrollViewer scroll && _savedOffset != 0)
        {
            scroll.SetScrollOffsets(0, _savedOffset);
            _savedOffset = 0;
        }
    }

    protected override void RenderSubtree(IGraphicsContext context) => _root?.Render(context);
    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => _root == null || visitor(_root);
    bool ILogicalTreeHost.VisitLogicalChildren(Func<Element, bool> visitor) => _root == null || visitor(_root);

    protected override void OnDispose()
    {
        _disposed = true;
        ClearTree();
        _document = null;
        LinkRequested = null;
        base.OnDispose();
    }
}
