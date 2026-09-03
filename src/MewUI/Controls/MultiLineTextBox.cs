using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;
using Aprillz.MewUI.Text.Editing;
using System.Globalization;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Multi-line editor built on the extensible text view engine.
/// It does not use the legacy Controls.Text formatter, view, or measurement caches.
/// </summary>
public sealed partial class MultiLineTextBox : TextBase, IVisualTreeHost, ITextViewHost
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<MultiLineTextBox>(DefaultStyles.CreateMultiLineTextBoxStyle);

    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<MultiLineTextBox>(nameof(Text), string.Empty,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, value) => self.ApplyExternalTextCore(value));

    public static readonly MewProperty<bool> WrapProperty =
        MewProperty<bool>.Register<MultiLineTextBox>(nameof(Wrap), true,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, value) => self.OnWrapChanged(value));

    public static readonly MewProperty<bool> SizeToDocumentProperty =
        MewProperty<bool>.Register<MultiLineTextBox>(nameof(SizeToDocument), false,
            MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<int> TabSizeProperty =
        MewProperty<int>.Register<MultiLineTextBox>(nameof(TabSize), 4,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.ResetView());

    // Arrange snaps the viewport to the pixel grid, moving its width by less than one DIP; a
    // measure that re-cut lines over that drift would re-wrap on every frame.
    private const double WRAP_WIDTH_TOLERANCE = 1.0;

    // Guard against an offset that oscillates instead of settling. Measured convergence is 2-3
    // passes and does not grow with the document, since each pass measures the lines it lands on.
    private const int SCROLL_SETTLE_PASSES = 8;
    private const int ANCHOR_PIN_PASSES = 4;

    // Keeps the caret fully visible at the end of the longest line, whose width is the whole extent.
    private const double CARET_SLACK = 2;
    private const double DRAG_EDGE_DIP = 8;

    private readonly ScrollBar _verticalScrollBar;
    private readonly ScrollBar _horizontalScrollBar;
    private TextViewLayout? _view;
    private IGraphicsFactory? _viewFactory;
    private Rect _contentBounds;
    private double _verticalOffset;
    private double _horizontalOffset;
    // Vertical scrolling is anchored to a document position: estimated line heights move as lines
    // materialize, and a pixel offset over them would let the content drift under a stationary
    // viewport. The pixel offset is re-derived from the anchor every time the viewport is applied;
    // pixel-space operations (wheel, scroll bar, caret tracking) re-capture the anchor at their
    // target. The delta is the anchor row's distance above the viewport top.
    private int _scrollAnchorOffset;
    private double _scrollAnchorDelta;
    // A scroll moved the pixel offset; the row it lands on is read in the next layout pass.
    private bool _scrollAnchorStale;
    private readonly TextViewLayerStack _layers;
    private IGraphicsContext? _graphics;
    private double _preferredCaretX = double.NaN;
    private bool _dragSelecting;
    // True while UpdateScrollBarRanges mirrors the offsets into the bars.
    private bool _syncingScrollBars;

    static MultiLineTextBox()
    {
        FocusableProperty.OverrideDefaultValue<MultiLineTextBox>(true);
    }

    public MultiLineTextBox()
        : this(new EditableTextDocument())
    {
    }

    public MultiLineTextBox(EditableTextDocument document)
        : base(document)
    {
        Extensions = new TextViewExtensionPipeline();
        _layers = new TextViewLayerStack(CreateBuiltInLayer);
        _document.Changed += OnDocumentChanged;
        _editor.StateChanged += OnEditorStateChanged;

        _verticalScrollBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _horizontalScrollBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };
        _verticalScrollBar.Parent = this;
        _horizontalScrollBar.Parent = this;
        _verticalScrollBar.ValueChanged += value =>
        {
            if (!_syncingScrollBars) SetVerticalOffset(value);
        };
        _horizontalScrollBar.ValueChanged += value =>
        {
            if (!_syncingScrollBars) SetHorizontalOffset(value);
        };
    }

    public string Text
    {
        get => GetTextSnapshot();
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    private protected override MewProperty<string>? TextSyncProperty => TextProperty;

    public string SelectedText => GetSelectedDocumentText();

    private protected override bool SupportsClipboardCopy => true;

    private protected override string? GetClipboardCopyText() => SelectedText;

    public bool Wrap
    {
        get => GetValue(WrapProperty);
        set => SetValue(WrapProperty, value);
    }

    /// <summary>
    /// Whether the desired size is the document's extent, clamped to the offered space, instead of
    /// the offered space itself. Off by default: reporting the offered space is what an editor
    /// filling a pane wants, and it costs no extent lookup.
    /// </summary>
    public bool SizeToDocument
    {
        get => GetValue(SizeToDocumentProperty);
        set => SetValue(SizeToDocumentProperty, value);
    }

    /// <summary>Tab width in space characters.</summary>
    public int TabSize
    {
        get => GetValue(TabSizeProperty);
        set => SetValue(TabSizeProperty, value);
    }

    public double HorizontalOffset => _horizontalOffset;
    public double VerticalOffset => _verticalOffset;
    /// <summary>Backing document. Assigning a new one keeps the view and extension registrations while caret, selection, scroll, and undo history reset.</summary>
    public EditableTextDocument Document
    {
        get => _document;
        set => ReplaceDocument(value);
    }

    IReadOnlyTextDocument ITextViewHost.Document => _document;
    public TextViewExtensionPipeline Extensions { get; }

    /// <summary>Raised after the document content changed or the document was replaced.</summary>
    public event Action<ITextViewHost>? DocumentChanged;
    public IReadOnlyList<TextLineLayout> VisibleTextLines
        => _view?.MaterializedLines ?? Array.Empty<TextLineLayout>();
    public Rect TextViewportBounds => _contentBounds;

    internal int MaterializedLineCount => _view?.MaterializedLines.Count ?? 0;
    internal int MaterializedCharacterCount
        => _view?.MaterializedLines.Sum(static line => line.LogicalLine.Length) ?? 0;
    internal int MaterializedVisualLineCount
        => _view?.MaterializedLines.Sum(static line => line.VisualLines.Count) ?? 0;
    internal bool IsVerticalScrollBarVisible => _verticalScrollBar.IsVisible;
    internal (double Value, double Maximum) VerticalScrollBarRange
        => (_verticalScrollBar.Value, _verticalScrollBar.Maximum);
    internal bool IsHorizontalScrollBarVisible => _horizontalScrollBar.IsVisible;

    public event Action? EditingStateChanged;
    public event Action<bool>? WrapChanged;

    /// <summary>Re-runs registered classifiers, generators, projections, and layers.</summary>
    public void InvalidateTextView()
    {
        // Rebuild instead of reset: extensions re-run against unchanged text, so the reader
        // must stay where they were reading. Only document or metric changes reset scrolling.
        Extensions.Revision++;
        RebuildView();
    }

    public override Rect GetCharRectInWindow(int charIndex)
    {
        EnsureView();
        if (_view is null)
        {
            return Rect.Empty;
        }
        var caret = _view.GetCaretBounds(Math.Clamp(charIndex, 0, _document.TextLength));
        return new Rect(
            GetTextOriginX() + caret.X,
            _contentBounds.Y + caret.Y - _verticalOffset,
            caret.Width,
            caret.Height);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double lineHeight = Math.Max(16, FontSize * 1.4);
        if (SizeToDocument && TryMeasureDocument(availableSize, out var documentSize))
        {
            return new Size(Math.Max(40, documentSize.Width), Math.Max(lineHeight, documentSize.Height));
        }

        double width = double.IsPositiveInfinity(availableSize.Width) ? 240 : availableSize.Width;
        double height = double.IsPositiveInfinity(availableSize.Height)
            ? Math.Min(400, Math.Max(3, _document.LineCount) * lineHeight + Padding.VerticalThickness)
            : availableSize.Height;
        return new Size(Math.Max(40, width), Math.Max(lineHeight, height));
    }

    /// <summary>
    /// Document extent at the offered width, or false when no view can be built yet.
    /// </summary>
    private bool TryMeasureDocument(Size availableSize, out Size documentSize)
    {
        documentSize = default;
        EnsureView();
        if (_view is null)
        {
            return false;
        }

        double chromeWidth = Padding.HorizontalThickness + (BorderThickness * 2);
        double chromeHeight = Padding.VerticalThickness + (BorderThickness * 2);

        double offeredWidth = double.IsPositiveInfinity(availableSize.Width)
            ? _view.Viewport.Width
            : Math.Max(0, availableSize.Width - chromeWidth);
        ApplyMeasureWrapWidth(offeredWidth);

        // Lines outside the viewport carry estimated sizes, and an extent with estimates in it
        // drifts as arrange materializes them, flickering a scroll bar in over a control sized to
        // the stale reading. Growing the viewport to the extent stands every line up, so the
        // reported extent is measured, not estimated. A document already taller than the offer is
        // exempt: its height gets clamped to the offer anyway, and standing all of it up would
        // defeat virtualization.
        double offeredHeight = double.IsPositiveInfinity(availableSize.Height)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Height - chromeHeight);
        for (int pass = 0; pass < SCROLL_SETTLE_PASSES; pass++)
        {
            var viewport = _view.Viewport;
            double targetHeight = _view.ExtentHeight;
            if (targetHeight > offeredHeight + WRAP_WIDTH_TOLERANCE
                || Math.Abs(viewport.Height - targetHeight) <= WRAP_WIDTH_TOLERANCE)
            {
                break;
            }
            _view.SetViewport(viewport with { Height = targetHeight });
        }

        // Clamped to the offer, which is what keeps a virtualized document from oscillating: past
        // the offer the answer is the offer itself. The tolerance is slack for sub-DIP measurement
        // drift: a line whose refined width lands exactly on the reported size would otherwise
        // re-wrap in arrange and flicker a scroll bar in.
        documentSize = new Size(
            Math.Min(Math.Ceiling(_view.ExtentWidth) + chromeWidth + WRAP_WIDTH_TOLERANCE, availableSize.Width),
            Math.Min(Math.Ceiling(_view.ExtentHeight) + chromeHeight + WRAP_WIDTH_TOLERANCE, availableSize.Height));
        return true;
    }

    /// <summary>
    /// Points the view's wrap width at <paramref name="width"/>, tolerating the sub-DIP drift the
    /// arrange pass introduces by snapping its viewport to the pixel grid.
    /// </summary>
    private void ApplyMeasureWrapWidth(double width)
    {
        var viewport = _view!.Viewport;
        if (Math.Abs(viewport.Width - width) > WRAP_WIDTH_TOLERANCE)
        {
            _view.SetViewport(viewport with { Width = width });
        }
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        _contentBounds = GetEditorContentBounds();
        UpdateViewport();
        ArrangeScrollBars();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, BorderThickness, CornerRadius);
        _contentBounds = GetEditorContentBounds();

        context.Save();
        try
        {
            context.SetClip(LayoutRounding.MakeClipRect(_contentBounds, GetDpi() / 96.0));
            if (_document.TextLength == 0 && !string.IsNullOrEmpty(Placeholder) && !IsFocused)
            {
                DrawPlaceholder(context);
            }
            else
            {
                DrawDocument(context);
            }
        }
        finally
        {
            context.Restore();
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (_verticalScrollBar.IsVisible)
        {
            _verticalScrollBar.Render(context);
        }
        if (_horizontalScrollBar.IsVisible)
        {
            _horizontalScrollBar.Render(context);
        }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }
        if (_verticalScrollBar.IsVisible && _verticalScrollBar.Bounds.Contains(point))
        {
            return _verticalScrollBar;
        }
        if (_horizontalScrollBar.IsVisible && _horizontalScrollBar.Bounds.Contains(point))
        {
            return _horizontalScrollBar;
        }
        return Bounds.Contains(point) ? this : null;
    }

    private void DrawDocument(IGraphicsContext context)
    {
        if (_view is null)
        {
            return;
        }
        // A layer inserted below an anchor paints under that anchor's content, and the four
        // built-ins are entries like any other, so the order alone decides the result.
        _graphics = context;
        _layers.Draw(context.Text, _contentBounds);
    }

    private ITextViewLayer CreateBuiltInLayer(TextViewLayerAnchor anchor) => anchor switch
    {
        TextViewLayerAnchor.Background => new BuiltInLayer(this, DrawLineBackgrounds),
        TextViewLayerAnchor.Selection => new BuiltInLayer(this, DrawSelection),
        TextViewLayerAnchor.Text => new BuiltInLayer(this, DrawGlyphs),
        _ => new BuiltInLayer(this, DrawCaret)
    };

    private void DrawLineBackgrounds(ITextRenderContext text)
    {
        foreach (var line in _view!.MaterializedLines)
        {
            var options = new TextDrawOptions(Foreground, CreateCompositionSpans(line), Owner: line);
            line.DrawBackground(text, GetLineOrigin(line), in options);
        }
    }

    private void DrawSelection(ITextRenderContext text)
    {
        var selection = _editor.Selection;
        foreach (var line in _view!.MaterializedLines)
        {
            var spans = CreateSelectionSpans(line, selection);
            if (spans.Length == 0)
            {
                continue;
            }
            var options = new TextDrawOptions(Foreground, spans, Owner: line);
            line.DrawBackground(text, GetLineOrigin(line), in options);
        }
    }

    private void DrawGlyphs(ITextRenderContext text)
    {
        var selection = _editor.Selection;
        foreach (var line in _view!.MaterializedLines)
        {
            var options = new TextDrawOptions(Foreground, CreateGlyphSpans(line, selection), Owner: line);
            line.DrawForeground(text, GetLineOrigin(line), in options);
        }
        DrawCompositionUnderlines(_graphics!, _contentBounds.Right);
    }

    /// <summary>
    /// Composition spans plus the selection recolor. Recoloring re-segments the runs on every drag
    /// frame, so it happens only where <see cref="TextBase.SelectionForeground"/> asks for it.
    /// </summary>
    private TextPaintSpan[] CreateGlyphSpans(TextLineLayout line, TextRange selection)
    {
        var composition = CreateCompositionSpans(line);
        if (SelectionForeground is not Color foreground ||
            !TextSelectionPresentation.TryCreateSpan(
                line, selection, foreground, default, out var span))
        {
            return composition;
        }
        var spans = new TextPaintSpan[composition.Length + 1];
        spans[0] = span with { Background = null };
        composition.CopyTo(spans, 1);
        return spans;
    }

    /// <inheritdoc/>
    private protected override void InvalidateCaret() => InvalidateLayer(TextViewLayerAnchor.Caret);

    private void DrawCaret(ITextRenderContext text)
    {
        if (!IsFocused || !CaretVisible)
        {
            return;
        }
        var caret = GetCharRectInWindow(_editor.CaretPosition);
        _graphics!.FillRectangle(
            new Rect(caret.X, caret.Y, 1, Math.Max(1, caret.Height)), Foreground);
    }

    /// <summary>
    /// One of the host's own drawing passes as a layer entry. It draws nothing before the view
    /// exists, which is the same guard the single draw method used to carry.
    /// </summary>
    private sealed class BuiltInLayer(MultiLineTextBox owner, Action<ITextRenderContext> draw) : ITextViewLayer
    {
        public void Draw(ITextRenderContext context, Rect viewportBounds)
        {
            if (owner._view is not null)
            {
                draw(context);
            }
        }
    }

    private Point GetLineOrigin(TextLineLayout line)
    {
        double documentY = line.VisualLines.Count == 0 ? 0 : line.VisualLines[0].Bounds.Y;
        return new Point(
            GetTextOriginX(),
            _contentBounds.Y + documentY - _verticalOffset);
    }

    /// <summary>
    /// Left edge the text is drawn from, on a whole device pixel. The engine lays a line out on the
    /// pixel grid and every backend draws a run at a whole pixel, so an origin between pixels lets
    /// each run round on its own and the glyphs shift whenever an inline object splits a line. The
    /// scroll offset itself keeps its exact value: caret tracking over virtualized lines converges
    /// on it, and quantizing it there strands the caret outside the viewport.
    /// </summary>
    private double GetTextOriginX()
        => LayoutRounding.RoundToPixel(_contentBounds.X - _horizontalOffset, GetDpi() / 96.0);

    private TextPaintSpan[] CreateSelectionSpans(TextLineLayout line, TextRange selection)
    {
        if (!TextSelectionPresentation.TryCreateSpan(
                line,
                selection,
                Theme.Palette.SelectionText,
                Theme.Palette.SelectionBackground,
                out var selectionSpan))
        {
            return [];
        }

        // This pass paints the background; the recolor belongs to the glyph pass, which is the
        // only one that reads a span foreground.
        return [selectionSpan with { Foreground = null }];
    }

    private TextPaintSpan[] CreateCompositionSpans(TextLineLayout line)
    {
        var spans = new List<TextPaintSpan>(1);
        int lineStart = line.LogicalLine.Offset;
        int lineEnd = lineStart + line.LogicalLine.Length;
        if (_editor.IsComposing)
        {
            int compositionEnd = _compositionStart + _compositionLength;
            int start = Math.Max(_compositionStart, lineStart);
            int end = Math.Min(compositionEnd, lineEnd);
            if (end > start)
            {
                spans.Add(new TextPaintSpan(
                    new TextRange(start - lineStart, end - start),
                    Decoration: TextDecoration.Underline));
            }
        }
        return spans.ToArray();
    }

    private void DrawPlaceholder(IGraphicsContext context)
    {
        var request = CreateTextRequest(Placeholder, TextWrapping.NoWrap, _contentBounds.Width);
        var layout = GetGraphicsFactory().TextEngine.GetOrCreateLayout(request, TextLayoutCachePolicy.Owner, this);
        var options = new TextDrawOptions(Theme.Palette.PlaceholderText, Owner: this);
        context.Text.Draw(layout, _contentBounds.Position, in options);
    }

    private void EnsureView()
    {
        var factory = GetGraphicsFactory();
        if (_view is not null && ReferenceEquals(_viewFactory, factory))
        {
            return;
        }
        _view?.Dispose();
        _viewFactory = factory;
        _view = new TextViewLayout(
            factory.TextEngine,
            _document,
            GetTextRunStyle(),
            new TextParagraphStyle
            {
                Wrapping = Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TabSize = TabSize,
                Culture = System.Globalization.CultureInfo.CurrentUICulture
            },
            Extensions,
            dpi: GetDpi());
        _view.LineConstructionStarting += (_, firstLine) => LineConstructionStarting?.Invoke(this, firstLine);
        _view.LinesChanged += _ => LinesChanged?.Invoke(this);
    }

    /// <inheritdoc/>
    public event Action<ITextViewHost, int>? LineConstructionStarting;

    /// <inheritdoc/>
    public event Action<ITextViewHost>? LinesChanged;

    /// <inheritdoc/>
    public void InvalidateTextRange(int offset, int length)
    {
        EnsureView();
        _view?.InvalidateRange(offset, length);
        InvalidateVisual();
    }

    /// <inheritdoc/>
    public double ExtentHeight
    {
        get
        {
            EnsureView();
            return _view?.ExtentHeight ?? 0;
        }
    }

    /// <inheritdoc/>
    public double ExtentWidth
    {
        get
        {
            EnsureView();
            return _view?.ExtentWidth ?? 0;
        }
    }

    /// <inheritdoc/>
    public TextLineLayout? GetLineLayout(int documentOffset)
    {
        EnsureView();
        return _view?.GetLineLayout(documentOffset);
    }

    /// <inheritdoc/>
    public ITextLineExtent? GetLineExtent(int documentOffset)
    {
        EnsureView();
        return _view?.GetLineExtent(documentOffset);
    }

    /// <inheritdoc/>
    public double DefaultLineHeight
    {
        get
        {
            EnsureView();
            return _view?.DefaultLineHeight ?? 0;
        }
    }

    /// <inheritdoc/>
    public double DefaultBaseline
    {
        get
        {
            EnsureView();
            return _view?.DefaultBaseline ?? 0;
        }
    }

    /// <inheritdoc/>
    public int FindLineByY(double documentY)
    {
        EnsureView();
        return _view?.FindLineByY(documentY) ?? 0;
    }

    /// <inheritdoc/>
    public double GetLineY(int lineNumber)
    {
        EnsureView();
        return _view?.GetLineY(lineNumber) ?? 0;
    }

    private void UpdateViewport()
    {
        EnsureView();
        if (_view is null || _contentBounds.Width <= 0 || _contentBounds.Height <= 0)
        {
            return;
        }
        // A scroll moved the pixel offset without standing any lines up; the row it landed on is
        // read here, before the anchor below resolves the offset back from it.
        if (_scrollAnchorStale)
        {
            _scrollAnchorStale = false;
            CaptureAnchor();
        }
        // Pin the anchor: materializing the viewport may replace estimated heights above it with
        // measured ones, which moves the anchor's document Y; the derived offset follows until the
        // anchor row no longer moves. The viewport is applied before the anchor is read so slice
        // virtualization sees real dimensions; when the offset settles the applied viewport is
        // already the derived one.
        for (int pass = 0; pass < ANCHOR_PIN_PASSES; pass++)
        {
            _view.SetViewport(new TextViewport(
                _contentBounds.Width,
                _contentBounds.Height,
                _horizontalOffset,
                _verticalOffset));
            bool settled = ApplyDerivedVerticalOffset(GetAnchorDocumentY() + _scrollAnchorDelta);
            SetHorizontalOffset(_horizontalOffset, false);
            if (settled)
            {
                break;
            }
        }
        UpdateScrollBarRanges();
    }

    /// <summary>
    /// Document Y of the anchor's visual row, in the same coordinate system the renderer draws
    /// with. Without wrapping this is a pure metrics-tree read; a caret query would re-cut a
    /// virtualized line's slice and fight the horizontal axis over it.
    /// </summary>
    private double GetAnchorDocumentY()
    {
        if (_view is null)
        {
            return 0;
        }
        if (!Wrap)
        {
            int lineNumber = _document.LineCount == 0
                ? 0
                : _document.GetLineByOffset(Math.Clamp(_scrollAnchorOffset, 0, _document.TextLength)).LineNumber;
            return _view.GetLineY(lineNumber);
        }
        return _view.GetCaretBounds(_scrollAnchorOffset).Y;
    }

    /// <summary>
    /// Applies the pixel offset derived from the anchor. Returns true when it did not move, i.e.
    /// the anchor is pinned. Never re-captures the anchor.
    /// </summary>
    private bool ApplyDerivedVerticalOffset(double value)
    {
        double extent = _view?.ExtentHeight ?? 0;
        // Near the document end both the anchor Y and the extent carry the same estimated heights
        // above the viewport, so this clamp compares measured quantities and cannot drift content.
        double maximum = Math.Max(0, extent - _contentBounds.Height);
        value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, maximum);
        if (Math.Abs(_verticalOffset - value) < 0.001)
        {
            return true;
        }
        _verticalOffset = value;
        UpdateScrollBarRanges();
        ScrollOffsetChanged?.Invoke(this);
        return false;
    }

    /// <summary>
    /// Re-anchors to the row at the top of the viewport, materializing at the current pixel offset
    /// first. The one place estimates decide content, which is why only pixel-space jumps call it.
    /// </summary>
    private void CaptureAnchor()
    {
        if (_view is null || _contentBounds.Width <= 0 || _contentBounds.Height <= 0)
        {
            _scrollAnchorOffset = 0;
            _scrollAnchorDelta = _verticalOffset;
            return;
        }
        _view.SetViewport(new TextViewport(
            _contentBounds.Width,
            _contentBounds.Height,
            _horizontalOffset,
            _verticalOffset));
        var hit = _view.HitTest(new Point(0, 0));
        _scrollAnchorOffset = hit.DocumentOffset;
        _scrollAnchorDelta = _verticalOffset - _view.GetCaretBounds(hit.DocumentOffset).Y;
    }

    private void ArrangeScrollBars()
    {
        if (_view is null)
        {
            return;
        }
        double thickness = Theme.Metrics.ScrollBarHitThickness;
        double extentHeight = _view.ExtentHeight;
        double extentWidth = _view.ExtentWidth;
        bool vertical = extentHeight > _contentBounds.Height + 0.5;
        bool horizontal = !Wrap && extentWidth > _contentBounds.Width + 0.5;
        _verticalScrollBar.IsVisible = vertical;
        _horizontalScrollBar.IsVisible = horizontal;

        UpdateScrollBarRanges();
        if (vertical)
        {
            _verticalScrollBar.Arrange(new Rect(Bounds.Right - thickness, Bounds.Y, thickness, Bounds.Height));
        }
        else
        {
            _verticalScrollBar.Arrange(Rect.Empty);
        }
        if (horizontal)
        {
            _horizontalScrollBar.Arrange(new Rect(Bounds.X, Bounds.Bottom - thickness, Bounds.Width, thickness));
        }
        else
        {
            _horizontalScrollBar.Arrange(Rect.Empty);
        }
    }

    /// <summary>
    /// Aligns the scroll bars with the current extent. Separate from arranging them because
    /// materializing lines replaces estimated heights with measured ones between arranges, and a
    /// thumb ranged against the estimate sits away from where the viewport actually is.
    /// </summary>
    private void UpdateScrollBarRanges()
    {
        if (_view is null)
        {
            return;
        }
        // Shrinking Maximum below the bar's standing Value coerces it and fires ValueChanged,
        // which would re-enter SetVertical/HorizontalOffset and clobber a freshly set offset.
        // The bars only mirror state here; the offsets are already authoritative.
        _syncingScrollBars = true;
        try
        {
            if (_verticalScrollBar.IsVisible)
            {
                _verticalScrollBar.Minimum = 0;
                _verticalScrollBar.Maximum = Math.Max(0, _view.ExtentHeight - _contentBounds.Height);
                _verticalScrollBar.ViewportSize = _contentBounds.Height;
                _verticalScrollBar.Value = _verticalOffset;
            }
            if (_horizontalScrollBar.IsVisible)
            {
                _horizontalScrollBar.Minimum = 0;
                _horizontalScrollBar.Maximum = Math.Max(0, _view.ExtentWidth - _contentBounds.Width + CARET_SLACK);
                _horizontalScrollBar.ViewportSize = _contentBounds.Width;
                _horizontalScrollBar.Value = _horizontalOffset;
            }
        }
        finally
        {
            _syncingScrollBars = false;
        }
    }

    private TextLayoutRequest CreateTextRequest(string text, TextWrapping wrapping, double width)
        => new()
        {
            Text = text.AsMemory(),
            Dpi = GetDpi(),
            DefaultStyle = GetTextRunStyle(),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = width,
                Wrapping = wrapping,
                TabSize = TabSize,
                Culture = System.Globalization.CultureInfo.CurrentUICulture
            }
        };

    private Rect GetEditorContentBounds()
    {
        var snapped = GetSnappedBorderBounds(Bounds);
        double border = GetBorderVisualInset();
        return LayoutRounding.SnapViewportRectToPixels(
            snapped.Deflate(new Thickness(border)).Deflate(Padding),
            GetDpi() / 96.0);
    }

    private void OnDocumentChanged(TextChange change)
    {
        _view?.Invalidate(change);
        DocumentChanged?.Invoke(this);
    }

    private void OnEditorStateChanged()
    {
        _preferredCaretX = double.NaN;
        EditingStateChanged?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }
        if (e.PrimaryKey && HandlePrimaryKey(e))
        {
            e.Handled = true;
            EnsureCaretVisible();
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                _editor.MoveLogical(LogicalDirection.Backward, e.ShiftKey, e.ControlKey);
                break;
            case Key.Right:
                _editor.MoveLogical(LogicalDirection.Forward, e.ShiftKey, e.ControlKey);
                break;
            case Key.Up:
                MoveCaretVertical(-1, e.ShiftKey);
                break;
            case Key.Down:
                MoveCaretVertical(1, e.ShiftKey);
                break;
            // Ctrl is left alone: it is the tab-switching chord, and a focused editor must not
            // swallow it on the way to the tab control.
            case Key.PageUp when !e.ControlKey:
                MoveCaretPage(-1, e.ShiftKey);
                break;
            case Key.PageDown when !e.ControlKey:
                MoveCaretPage(1, e.ShiftKey);
                break;
            case Key.Home:
                MoveToLineEdge(true, e.ShiftKey);
                break;
            case Key.End:
                MoveToLineEdge(false, e.ShiftKey);
                break;
            case Key.Backspace when !IsReadOnly:
                _editor.Backspace(e.ControlKey);
                break;
            case Key.Delete when !IsReadOnly:
                _editor.Delete(e.ControlKey);
                break;
            case Key.Enter when !IsReadOnly:
                InsertText("\n");
                _suppressNewLineInput = true;
                break;
            case Key.Tab when !IsReadOnly && AcceptTab:
                InsertText("\t");
                _suppressTabInput = true;
                break;
            default:
                return;
        }
        e.Handled = true;
        EnsureCaretVisible();
    }

    private void MoveToLineEdge(bool start, bool extend)
    {
        var line = _document.GetLineByOffset(_editor.CaretPosition);
        _editor.SetCaret(start ? line.Offset : line.Offset + line.Length, extend);
    }

    private void MoveCaretVertical(int direction, bool extend)
    {
        EnsureView();
        if (_view is null)
        {
            return;
        }
        var caret = _view.GetCaretBounds(_editor.CaretPosition);
        if (double.IsNaN(_preferredCaretX))
        {
            _preferredCaretX = caret.X;
        }
        double preferredCaretX = _preferredCaretX;
        int sourceLine = _document.GetLineByOffset(_editor.CaretPosition).LineNumber;
        var hit = _view.HitTest(new Point(
            preferredCaretX - _horizontalOffset,
            caret.Y - _verticalOffset + caret.Height / 2 + direction * Math.Max(1, caret.Height)));
        int target = hit.DocumentOffset;
        double targetVisualY = caret.Y + direction * Math.Max(1, caret.Height);
        if (target > 0 && _view.GetCaretBounds(target).Y > targetVisualY + 0.5)
        {
            // A soft-wrap boundary has one document offset but two visual affinities.
            // The editor stores offsets only, so choose the preceding grapheme when a
            // hit at the end of the target row resolves to the following visual row.
            target = _editor.GetPreviousCaretPosition(target);
        }
        _editor.SetCaret(target, extend);
        if (hit.LineNumber == sourceLine)
        {
            _preferredCaretX = preferredCaretX;
        }
    }

    /// <summary>Moves the caret one viewport, clamped to the document, and follows it with the view.</summary>
    private void MoveCaretPage(int direction, bool extend)
    {
        EnsureView();
        if (_view is null || _contentBounds.Height <= 0)
        {
            return;
        }
        var caret = _view.GetCaretBounds(_editor.CaretPosition);
        if (double.IsNaN(_preferredCaretX))
        {
            _preferredCaretX = caret.X;
        }
        double preferredCaretX = _preferredCaretX;
        double caretScreenY = caret.Y - _verticalOffset;
        double targetY = caret.Y + direction * _contentBounds.Height;
        if (targetY < 0)
        {
            // The caret leads the scroll: a page that overshoots the document still lands on its
            // first line, even though the view itself has nowhere left to go.
            SetVerticalOffset(0, false);
            _editor.SetCaret(0, extend);
        }
        else if (targetY >= _view.ExtentHeight)
        {
            _editor.SetCaret(_document.TextLength, extend);
        }
        else
        {
            SetVerticalOffset(targetY - caretScreenY, false);
            UpdateViewport();
            var hit = _view.HitTest(new Point(
                preferredCaretX - _horizontalOffset,
                targetY - _verticalOffset + caret.Height / 2));
            _editor.SetCaret(hit.DocumentOffset, extend);
            // A viewport is rarely a whole number of rows, so anchor the scroll to the row the page
            // landed on. Otherwise the caret creeps down the screen by the remainder each press.
            SetVerticalOffset(_view.GetCaretBounds(hit.DocumentOffset).Y - caretScreenY, false);
        }
        UpdateViewport();
        _preferredCaretX = preferredCaretX;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled)
        {
            return;
        }
        Focus();
        SetCaretFromPoint(e.Position, e.ShiftKey);
        _dragSelecting = true;
        if (FindVisualRoot() is Window window)
        {
            window.CaptureMouse(this);
        }
        e.Handled = true;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled) return;
        SetCaretFromPoint(e.Position, false);
        _editor.SelectWordAt(_editor.CaretPosition);
        EnsureCaretVisible();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragSelecting || !IsMouseCaptured || !e.LeftButton)
        {
            return;
        }
        AutoScroll(e.Position);
        SetCaretFromPoint(e.Position, true);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButton.Left)
        {
            _dragSelecting = false;
            if (FindVisualRoot() is Window window)
            {
                window.ReleaseMouseCapture();
            }
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!e.Handled && e.Delta.Y != 0)
        {
            SetVerticalOffset(_verticalOffset - e.Delta.Y * Theme.Metrics.ScrollWheelStep);
            e.Handled = true;
        }
    }

    private void SetCaretFromPoint(Point point, bool extend)
    {
        EnsureView();
        if (_view is null)
        {
            return;
        }
        var hit = _view.HitTest(new Point(point.X - _contentBounds.X, point.Y - _contentBounds.Y));
        _editor.SetCaret(hit.DocumentOffset, extend);
        EnsureCaretVisible();
    }

    private void AutoScroll(Point point)
    {
        if (point.Y < _contentBounds.Y)
        {
            SetVerticalOffset(_verticalOffset + point.Y - _contentBounds.Y);
        }
        else if (point.Y > _contentBounds.Bottom)
        {
            SetVerticalOffset(_verticalOffset + point.Y - _contentBounds.Bottom);
        }
        if (Wrap)
        {
            return;
        }
        if (point.X < _contentBounds.X + DRAG_EDGE_DIP)
        {
            SetHorizontalOffset(_horizontalOffset + point.X - (_contentBounds.X + DRAG_EDGE_DIP));
        }
        else if (point.X > _contentBounds.Right - DRAG_EDGE_DIP)
        {
            SetHorizontalOffset(_horizontalOffset + point.X - (_contentBounds.Right - DRAG_EDGE_DIP));
        }
    }

    private protected override void EnsureCaretVisible()
    {
        if (_contentBounds.IsEmpty)
        {
            return;
        }
        EnsureView();
        if (_view is null)
        {
            return;
        }
        // Scrolling materializes lines, which replaces estimated metrics with measured ones and so
        // moves both the caret and the scroll limit. A single pass stops short of the document edge
        // whenever the estimate was low, on either axis.
        for (int pass = 0; pass < SCROLL_SETTLE_PASSES; pass++)
        {
            var caret = _view.GetCaretBounds(_editor.CaretPosition);
            double vertical = _verticalOffset;
            double horizontal = _horizontalOffset;
            if (caret.Y < vertical) vertical = caret.Y;
            else if (caret.Bottom > vertical + _contentBounds.Height) vertical = caret.Bottom - _contentBounds.Height;
            if (!Wrap)
            {
                if (caret.X < horizontal) horizontal = caret.X;
                else if (caret.Right > horizontal + _contentBounds.Width - CARET_SLACK)
                {
                    horizontal = caret.Right - _contentBounds.Width + CARET_SLACK;
                }
            }
            double settledVertical = _verticalOffset;
            double settledHorizontal = _horizontalOffset;
            SetVerticalOffset(vertical, false);
            SetHorizontalOffset(horizontal, false);
            UpdateViewport();
            if (Math.Abs(_verticalOffset - settledVertical) < 0.001 &&
                Math.Abs(_horizontalOffset - settledHorizontal) < 0.001)
            {
                break;
            }
        }
        InvalidateVisual();
    }

    /// <summary>
    /// Replaces a document range the way a program does rather than a user: the caret rides along
    /// with the surrounding text, and neither <see cref="TextBase.IsReadOnly"/> nor
    /// <see cref="EditableRegions"/> is consulted. The change stays undoable.
    /// </summary>
    public void ReplaceRange(int start, int length, string? text)
        => _editor.ReplaceRange(start, length, text);

    /// <summary>
    /// Replaces a document range the way a user types over it: the caret lands at the end of the
    /// inserted text, undo returns to where the caret was, and <see cref="EditableRegions"/> is
    /// honored. <see cref="ReplaceRange"/> is the programmatic counterpart.
    /// </summary>
    public void EnterText(int start, int length, string? text)
        => _editor.EnterText(start, length, text);

    /// <summary>Consulted before every edit. Null leaves the document fully editable.</summary>
    public IEditableRegionProvider? EditableRegions
    {
        get => _editor.EditableRegions;
        set => _editor.EditableRegions = value;
    }

    /// <summary>Raised after typed or composed text reached the document, once per commit.</summary>
    public event Action<string>? TextCommitted
    {
        add => _editor.TextCommitted += value;
        remove => _editor.TextCommitted -= value;
    }

    /// <inheritdoc/>
    public TextViewLayerStack Layers => _layers;

    /// <inheritdoc/>
    public void InsertLayer(ITextViewLayer layer, TextViewLayerAnchor anchor, TextLayerPosition position)
        => _layers.Insert(layer, anchor, position);

    /// <inheritdoc/>
    public void InvalidateLayer(TextViewLayerAnchor anchor) => InvalidateVisual();

    /// <inheritdoc/>
    public Point ScrollOffset => new(_horizontalOffset, _verticalOffset);

    /// <inheritdoc/>
    public event Action<ITextViewHost>? ScrollOffsetChanged;

    /// <inheritdoc/>
    public void MakeVisible(Rect documentRect)
    {
        if (_contentBounds.IsEmpty)
        {
            return;
        }
        EnsureView();
        SetVerticalOffset(
            TextViewScrolling.ResolveOffset(
                _verticalOffset, _contentBounds.Height, documentRect.Y, documentRect.Height),
            false);
        if (!Wrap)
        {
            SetHorizontalOffset(
                TextViewScrolling.ResolveOffset(
                    _horizontalOffset, _contentBounds.Width, documentRect.X, documentRect.Width),
                false);
        }
        UpdateViewport();
        InvalidateVisual();
    }

    private void SetVerticalOffset(double value, bool invalidate = true)
    {
        // Against the extent the last layout measured. Arrange clamps again once the lines for the
        // new offset are up, which is what settles a scroll into estimated territory.
        double extent = _view?.ExtentHeight ?? 0;
        double maximum = Math.Max(0, extent - _contentBounds.Height);
        value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, maximum);
        if (Math.Abs(_verticalOffset - value) < 0.001) return;
        _verticalOffset = value;
        // Standing the lines up is the layout pass's, so the anchor is captured there too. A caller
        // that scrolls and reads the viewport in the same breath asks for a layout first.
        _scrollAnchorStale = true;
        UpdateScrollBarRanges();
        ScrollOffsetChanged?.Invoke(this);
        if (invalidate) InvalidateArrange();
    }

    private void SetHorizontalOffset(double value, bool invalidate = true)
    {
        // The scroll bar's limit is only refreshed on arrange, and materializing a slice grows the
        // extent mid-keystroke, so the limit has to come from the view itself.
        double extent = _view?.ExtentWidth ?? 0;
        double maximum = Math.Max(0, extent - _contentBounds.Width + CARET_SLACK);
        value = Wrap ? 0 : Math.Clamp(double.IsFinite(value) ? value : 0, 0, maximum);
        if (Math.Abs(_horizontalOffset - value) < 0.001) return;
        _horizontalOffset = value;
        UpdateScrollBarRanges();
        ScrollOffsetChanged?.Invoke(this);
        if (invalidate) InvalidateVisual();
    }

    private void ReplaceDocument(EditableTextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (ReferenceEquals(document, _document))
        {
            return;
        }
        _document.Changed -= OnDocumentChanged;
        _editor.StateChanged -= OnEditorStateChanged;
        ReplaceDocumentCore(document);
        _document.Changed += OnDocumentChanged;
        _editor.StateChanged += OnEditorStateChanged;
        _preferredCaretX = double.NaN;
        _verticalOffset = 0;
        _scrollAnchorOffset = 0;
        _scrollAnchorDelta = 0;
        ResetView();
        InvalidateMeasure();
        InvalidateVisual();
        DocumentChanged?.Invoke(this);
    }

    private void ResetView()
    {
        _horizontalOffset = 0;
        RebuildView();
    }

    private void RebuildView()
    {
        _view?.Dispose();
        _view = null;
        _viewFactory = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnWrapChanged(bool value)
    {
        ResetView();
        WrapChanged?.Invoke(value);
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        if (property.Id == FontFamilyProperty.Id ||
            property.Id == FontSizeProperty.Id ||
            property.Id == FontWeightProperty.Id)
        {
            ResetView();
        }
        base.OnMewPropertyChanged(property);
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ResetView();
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        // Classifier paint spans are cached per materialized line; theme-dependent
        // classifiers must re-run against the new theme without losing scroll position.
        Extensions.Revision++;
        RebuildView();
    }

    protected override void OnDispose()
    {
        _view?.Dispose();
        _document.Changed -= OnDocumentChanged;
        _editor.StateChanged -= OnEditorStateChanged;
        _verticalScrollBar.Dispose();
        _horizontalScrollBar.Dispose();
        base.OnDispose();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => visitor(_verticalScrollBar) && visitor(_horizontalScrollBar);
}
