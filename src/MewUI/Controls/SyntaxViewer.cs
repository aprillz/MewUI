using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>Read-only, virtualized text surface for syntax and diagnostic extensions.</summary>
public sealed partial class SyntaxViewer : Control, IVisualTreeHost, ITextViewHost
{
    static SyntaxViewer() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<SyntaxViewer>(DefaultStyles.CreateSyntaxViewerStyle);

    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<SyntaxViewer>(nameof(Text), string.Empty,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, value) => self.ReplaceDocument(value));

    public static readonly MewProperty<bool> WrapProperty =
        MewProperty<bool>.Register<SyntaxViewer>(nameof(Wrap), false,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.ResetView());

    public static readonly MewProperty<int> TabSizeProperty =
        MewProperty<int>.Register<SyntaxViewer>(nameof(TabSize), 4,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.ResetView());

    public static readonly MewProperty<Color?> SelectionForegroundProperty =
        MewProperty<Color?>.Register<SyntaxViewer>(nameof(SelectionForeground), null,
            MewPropertyOptions.AffectsRender);

    private StringTextDocument _document = new(string.Empty);
    private TextViewLayout? _view;
    private IGraphicsFactory? _viewFactory;
    private Rect _contentBounds;
    private const int ANCHOR_PIN_PASSES = 4;

    private double _verticalOffset;
    private double _horizontalOffset;
    // Vertical scrolling is anchored to a document position; the pixel offset is derived from it
    // so estimated-height corrections move the scroll bar, never the content. See MultiLineTextBox.
    private int _scrollAnchorOffset;
    private double _scrollAnchorDelta;
    // A scroll moved the pixel offset; the row it lands on is read in the next layout pass.
    private bool _scrollAnchorStale;
    private int _anchor;
    private int _caret;
    private long _documentVersion;
    private bool _dragSelecting;
    private readonly ScrollBar _verticalScrollBar;
    private readonly ScrollBar _horizontalScrollBar;
    private ContextMenu? _defaultContextMenu;

    public SyntaxViewer()
    {
        Cursor = CursorType.IBeam;
        Extensions = new TextViewExtensionPipeline();
        _layers = new TextViewLayerStack(CreateBuiltInLayer);
        _verticalScrollBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _horizontalScrollBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };
        _verticalScrollBar.Parent = this;
        _horizontalScrollBar.Parent = this;
        _verticalScrollBar.ValueChanged += value => SetVerticalOffset(value);
        _horizontalScrollBar.ValueChanged += value => SetHorizontalOffset(value);
        Commands.Register(StandardCommands.Copy, this,
            static viewer => viewer.Copy(),
            static viewer => viewer.SelectionLength > 0);
        Commands.Register(StandardCommands.SelectAll, this,
            static viewer => viewer.SelectAll(),
            static viewer => viewer._document.TextLength > 0);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Color the selected glyphs are painted in. Null keeps the colors they already have, so a
    /// colorized document stays readable through a selection.
    /// </summary>
    public Color? SelectionForeground
    {
        get => GetValue(SelectionForegroundProperty);
        set => SetValue(SelectionForegroundProperty, value);
    }

    public bool Wrap
    {
        get => GetValue(WrapProperty);
        set => SetValue(WrapProperty, value);
    }

    /// <summary>Tab width in space characters.</summary>
    public int TabSize
    {
        get => GetValue(TabSizeProperty);
        set => SetValue(TabSizeProperty, value);
    }

    /// <summary>Document whose text the view presents. Replaced whole when <see cref="Text"/> changes.</summary>
    public IReadOnlyTextDocument Document => _document;
    public TextViewExtensionPipeline Extensions { get; }

    /// <summary>Raised after the document was replaced by a <see cref="Text"/> change.</summary>
    public event Action<ITextViewHost>? DocumentChanged;

    public int SelectionStart => Math.Min(_anchor, _caret);
    public int SelectionLength => Math.Abs(_caret - _anchor);
    public string SelectedText => SelectionLength == 0
        ? string.Empty
        : _document.GetText(SelectionStart, SelectionLength);
    public double VerticalOffset => _verticalOffset;
    public double HorizontalOffset => _horizontalOffset;
    public IClipboardService? ClipboardService { get; set; }
    internal int MaterializedLineCount => _view?.MaterializedLines.Count ?? 0;
    internal bool IsVerticalScrollBarVisible => _verticalScrollBar.IsVisible;
    internal bool IsHorizontalScrollBarVisible => _horizontalScrollBar.IsVisible;

    public void Select(int start, int length)
    {
        if (start < 0 || length < 0 || start > _document.TextLength - length)
            throw new ArgumentOutOfRangeException(nameof(start));
        _anchor = start;
        _caret = start + length;
        EnsureSelectionVisible();
        InvalidateVisual();
    }

    public void SelectAll() => Select(0, _document.TextLength);

    public void Copy()
    {
        var clipboard = ClipboardService ?? (Application.IsRunning ? Application.Current.PlatformServices.Clipboard : null);
        if (SelectionLength > 0 && clipboard is not null)
        {
            clipboard.TrySetText(SelectedText);
        }
    }

    /// <summary>Re-runs registered classifiers, generators, projections, and layers.</summary>
    public void InvalidateTextView()
    {
        // Rebuild instead of reset: extensions re-run against unchanged text, so the reader
        // must stay where they were reading. Only document or metric changes reset scrolling.
        Extensions.Revision++;
        RebuildView();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double width = double.IsPositiveInfinity(availableSize.Width) ? 320 : availableSize.Width;
        double lineHeight = Math.Max(16, FontSize * 1.4);
        double height = double.IsPositiveInfinity(availableSize.Height)
            ? Math.Min(480, Math.Max(3, _document.LineCount) * lineHeight + Padding.VerticalThickness)
            : availableSize.Height;
        return new Size(Math.Max(40, width), Math.Max(lineHeight, height));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);
        _contentBounds = GetContentBounds();
        UpdateViewport();
        ArrangeScrollBars();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        DrawBackgroundAndBorder(
            context,
            GetSnappedBorderBounds(Bounds),
            Background,
            BorderBrush,
            BorderThickness,
            CornerRadius);
        _contentBounds = GetContentBounds();
        if (_view is null) return;

        context.Save();
        try
        {
            context.SetClip(LayoutRounding.MakeClipRect(_contentBounds, GetDpi() / 96.0));
            // Every anchor paints its extensions first and its own content after. The viewer draws
            // no caret, so that anchor holds extensions only; keeping it preserves the slot.
            var text = context.Text;
            _layers.Draw(text, _contentBounds);
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

    private void EnsureView()
    {
        var factory = GetGraphicsFactory();
        if (_view is not null && ReferenceEquals(_viewFactory, factory)) return;
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
            GetDpi());
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
    public IReadOnlyList<TextLineLayout> VisibleTextLines
        => _view?.MaterializedLines ?? Array.Empty<TextLineLayout>();

    /// <inheritdoc/>
    public Rect TextViewportBounds => _contentBounds;

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
    public double ExtentHeight
    {
        get
        {
            EnsureView();
            return _view?.ExtentHeight ?? 0;
        }
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
        if (_view is null || _contentBounds.IsEmpty) return;
        if (Wrap) _horizontalOffset = 0;
        // A scroll moved the pixel offset without standing any lines up; the row it landed on is
        // read here, before the anchor below resolves the offset back from it.
        if (_scrollAnchorStale)
        {
            _scrollAnchorStale = false;
            CaptureScrollAnchor();
        }
        // Pin the scroll anchor: materialization replaces estimated heights with measured ones,
        // and the derived pixel offset follows the anchor row so the content never drifts under a
        // stationary viewport. Same scheme as MultiLineTextBox.
        for (int pass = 0; pass < ANCHOR_PIN_PASSES; pass++)
        {
            _view.SetViewport(new TextViewport(
                _contentBounds.Width,
                _contentBounds.Height,
                _horizontalOffset,
                _verticalOffset));
            bool settled = ApplyDerivedVerticalOffset(GetScrollAnchorDocumentY() + _scrollAnchorDelta);
            SetHorizontalOffset(_horizontalOffset, false);
            if (settled)
            {
                break;
            }
        }
        UpdateScrollBarRanges();
    }

    /// <summary>Applies the pixel offset derived from the anchor; true when it did not move.</summary>
    private bool ApplyDerivedVerticalOffset(double value)
    {
        double maximum = Math.Max(0, (_view?.ExtentHeight ?? 0) - _contentBounds.Height);
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

    /// <summary>Re-anchors to the row at the top of the viewport at the current pixel offset.</summary>
    private void CaptureScrollAnchor()
    {
        if (_view is null || _contentBounds.IsEmpty)
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

    /// <summary>
    /// Document Y of the anchor's visual row. Without wrapping this is a metrics-tree read; a caret
    /// query would re-cut a virtualized line's slice and fight the horizontal axis over it.
    /// </summary>
    private double GetScrollAnchorDocumentY()
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
            _horizontalScrollBar.Maximum = Math.Max(0, _view.ExtentWidth - _contentBounds.Width);
            _horizontalScrollBar.ViewportSize = _contentBounds.Width;
            _horizontalScrollBar.Value = _horizontalOffset;
        }
    }

    private void SetVerticalOffset(double value, bool invalidate = true)
    {
        double maximum = _verticalScrollBar.IsVisible
            ? _verticalScrollBar.Maximum
            : Math.Max(0, (_view?.ExtentHeight ?? 0) - _contentBounds.Height);
        value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, maximum);
        if (Math.Abs(_verticalOffset - value) < 0.001)
        {
            return;
        }
        _verticalOffset = value;
        // Standing the lines up is the layout pass's, so the anchor is captured there too.
        _scrollAnchorStale = true;
        UpdateScrollBarRanges();
        ScrollOffsetChanged?.Invoke(this);
        if (invalidate)
        {
            InvalidateArrange();
        }
    }

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

    private void SetHorizontalOffset(double value, bool invalidate = true)
    {
        double maximum = _horizontalScrollBar.IsVisible ? _horizontalScrollBar.Maximum : Math.Max(0, value);
        value = Wrap ? 0 : Math.Clamp(double.IsFinite(value) ? value : 0, 0, maximum);
        if (Math.Abs(_horizontalOffset - value) < 0.001)
        {
            return;
        }
        _horizontalOffset = value;
        UpdateScrollBarRanges();
        ScrollOffsetChanged?.Invoke(this);
        if (invalidate)
        {
            UpdateViewport();
            InvalidateVisual();
        }
    }

    private Rect GetContentBounds()
    {
        double border = GetBorderVisualInset();
        return LayoutRounding.SnapViewportRectToPixels(
            GetSnappedBorderBounds(Bounds).Deflate(new Thickness(border)).Deflate(Padding),
            GetDpi() / 96.0);
    }

    private Point GetLineOrigin(TextLineLayout line)
    {
        double documentY = line.VisualLines.Count == 0 ? 0 : line.VisualLines[0].Bounds.Y;
        return new Point(
            // On a whole device pixel, for the reason given in MultiLineTextBox.GetTextOriginX.
            LayoutRounding.RoundToPixel(_contentBounds.X - _horizontalOffset, GetDpi() / 96.0),
            _contentBounds.Y + documentY - _verticalOffset);
    }

    private TextDrawOptions CreateDrawOptions(TextLineLayout line)
    {
        TextPaintSpan[] paint = TextSelectionPresentation.TryCreateSpan(
            line,
            new TextRange(SelectionStart, SelectionLength),
            Theme.Palette.SelectionText,
            Theme.Palette.SelectionBackground,
            out var span)
            // Recoloring the glyphs re-segments the runs on every drag frame, so the default keeps
            // their colors and only SelectionForeground opts into the cost.
            ? [span with { Foreground = SelectionForeground }]
            : [];
        return new TextDrawOptions(Foreground, paint, Owner: line);
    }

    /// <summary>
    /// The viewer's own drawing as layer entries. It paints no caret and no line background, so
    /// those anchors hold nothing but still occupy their slot, keeping them insertable.
    /// </summary>
    private ITextViewLayer CreateBuiltInLayer(TextViewLayerAnchor anchor) => anchor switch
    {
        TextViewLayerAnchor.Selection => new BuiltInLayer(this, DrawSelection),
        TextViewLayerAnchor.Text => new BuiltInLayer(this, DrawGlyphs),
        _ => new BuiltInLayer(this, null)
    };

    private void DrawSelection(ITextRenderContext text)
    {
        foreach (var line in _view!.MaterializedLines)
        {
            var options = CreateDrawOptions(line);
            if (!options.PaintSpans.IsEmpty)
            {
                line.DrawBackground(text, GetLineOrigin(line), in options);
            }
        }
    }

    private void DrawGlyphs(ITextRenderContext text)
    {
        foreach (var line in _view!.MaterializedLines)
        {
            var options = CreateDrawOptions(line);
            line.DrawForeground(text, GetLineOrigin(line), in options);
        }
    }

    private sealed class BuiltInLayer(SyntaxViewer owner, Action<ITextRenderContext>? draw) : ITextViewLayer
    {
        public void Draw(ITextRenderContext context, Rect viewportBounds)
        {
            if (draw is not null && owner._view is not null)
            {
                draw(context);
            }
        }
    }

    /// <inheritdoc/>
    public TextViewLayerStack Layers => _layers;

    /// <inheritdoc/>
    public void InsertLayer(ITextViewLayer layer, TextViewLayerAnchor anchor, TextLayerPosition position)
        => _layers.Insert(layer, anchor, position);

    /// <inheritdoc/>
    public void InvalidateLayer(TextViewLayerAnchor anchor) => InvalidateVisual();

    private readonly TextViewLayerStack _layers;

    private void ReplaceDocument(string value)
    {
        _document = new StringTextDocument(value, ++_documentVersion);
        _anchor = Math.Clamp(_anchor, 0, _document.TextLength);
        _caret = Math.Clamp(_caret, 0, _document.TextLength);
        _scrollAnchorOffset = Math.Clamp(_scrollAnchorOffset, 0, _document.TextLength);
        ResetView();
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

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        // Classifier paint spans are cached per materialized line; theme-dependent
        // classifiers must re-run against the new theme without losing scroll position.
        Extensions.Revision++;
        RebuildView();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled || !IsEffectivelyEnabled) return;
        if (e.Button == MouseButton.Right && ContextMenu == null)
        {
            var menu = _defaultContextMenu ??= new ContextMenu();
            TextContextMenu.Show(menu, this, e.Position,
                StandardCommands.Copy,
                StandardCommands.SelectAll);
            e.Handled = true;
            return;
        }
        if (e.Button != MouseButton.Left) return;
        SetCaretFromPoint(e.Position, e.ShiftKey);
        _dragSelecting = true;
        if (FindVisualRoot() is Window window) window.CaptureMouse(this);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragSelecting || !IsMouseCaptured || !e.LeftButton) return;
        SetCaretFromPoint(e.Position, true);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButton.Left) return;
        _dragSelecting = false;
        if (FindVisualRoot() is Window window) window.ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || e.Delta.Y == 0 || _view is null) return;
        double maximum = Math.Max(0, _view.ExtentHeight - _contentBounds.Height);
        SetVerticalOffset(
            Math.Clamp(
                _verticalOffset - e.Delta.Y * Theme.Metrics.ScrollWheelStep,
                0,
                maximum));
        e.Handled = true;
    }

    private void SetCaretFromPoint(Point point, bool extend)
    {
        EnsureView();
        if (_view is null) return;
        var hit = _view.HitTest(new Point(point.X - _contentBounds.X, point.Y - _contentBounds.Y));
        _caret = hit.DocumentOffset;
        if (!extend) _anchor = _caret;
        EnsureSelectionVisible();
        InvalidateVisual();
    }

    private void EnsureSelectionVisible()
    {
        EnsureView();
        if (_view is null || _contentBounds.IsEmpty) return;
        var caret = _view.GetCaretBounds(_caret);
        double vertical = _verticalOffset;
        if (caret.Y < vertical) vertical = caret.Y;
        else if (caret.Bottom > vertical + _contentBounds.Height)
            vertical = caret.Bottom - _contentBounds.Height;
        SetVerticalOffset(vertical, false);
        UpdateViewport();
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        if (property.Id == FontFamilyProperty.Id || property.Id == FontSizeProperty.Id || property.Id == FontWeightProperty.Id)
            ResetView();
        base.OnMewPropertyChanged(property);
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ResetView();
    }

    protected override void OnDispose()
    {
        _view?.Dispose();
        _verticalScrollBar.Dispose();
        _horizontalScrollBar.Dispose();
        base.OnDispose();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => visitor(_verticalScrollBar) && visitor(_horizontalScrollBar);
}
