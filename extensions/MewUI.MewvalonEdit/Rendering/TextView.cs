using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>Where an inserted layer goes relative to its anchor. Mirrors LayerInsertionPosition.</summary>
public enum LayerInsertionPosition
{
    Below,
    Replace,
    Above
}

/// <summary>Rendering-side view of the editor, carrying the extension registrations.</summary>
public sealed class TextView : MewObject, ITextEditorComponent
{
    private readonly TextArea textArea;
    private Action<int>? _constructionStarting;
    private Action? _linesChanged;
    private Action? _scrollOffsetChanged;
    private MouseHoverLogic? _hoverLogic;

    internal TextView(TextArea textArea)
    {
        this.textArea = textArea;
        Services.AddService(this);
        var host = textArea.Editor.Surface;
        // Forwarded rather than exposed directly: the AvalonEdit signatures carry no host argument,
        // and the subscription must survive a document swap, which replaces neither the host nor it.
        host.LineConstructionStarting += (_, firstLine) => _constructionStarting?.Invoke(firstLine);
        host.LinesChanged += _ => _linesChanged?.Invoke();
        host.ScrollOffsetChanged += _ => _scrollOffsetChanged?.Invoke();
    }

    public static readonly MewProperty<Color?> LinkTextForegroundBrushProperty =
        MewProperty<Color?>.Register<TextView>(nameof(LinkTextForegroundBrush), null,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<Color?> LinkTextBackgroundBrushProperty =
        MewProperty<Color?>.Register<TextView>(nameof(LinkTextBackgroundBrush), null,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> LinkTextUnderlineProperty =
        MewProperty<bool>.Register<TextView>(nameof(LinkTextUnderline), true,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<Color?> NonPrintableCharacterBrushProperty =
        MewProperty<Color?>.Register<TextView>(nameof(NonPrintableCharacterBrush), null,
            MewPropertyOptions.AffectsRender);

    /// <summary>Colour of link text. Null follows the theme.</summary>
    public Color? LinkTextForegroundBrush
    {
        get => GetValue(LinkTextForegroundBrushProperty);
        set => SetValue(LinkTextForegroundBrushProperty, value);
    }

    /// <summary>Background painted behind link text. Null paints none.</summary>
    public Color? LinkTextBackgroundBrush
    {
        get => GetValue(LinkTextBackgroundBrushProperty);
        set => SetValue(LinkTextBackgroundBrushProperty, value);
    }

    /// <summary>Whether link text is underlined. Clearing it leaves the link clickable.</summary>
    public bool LinkTextUnderline
    {
        get => GetValue(LinkTextUnderlineProperty);
        set => SetValue(LinkTextUnderlineProperty, value);
    }

    /// <summary>Colour of the space, tab and end-of-line markers. Null follows the theme.</summary>
    public Color? NonPrintableCharacterBrush
    {
        get => GetValue(NonPrintableCharacterBrushProperty);
        set => SetValue(NonPrintableCharacterBrushProperty, value);
    }

    public static readonly MewProperty<double> EmptyLineSelectionWidthProperty =
        MewProperty<double>.Register<TextView>(nameof(EmptyLineSelectionWidth), 1.0,
            MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Width of the selection rectangle drawn where a selected line has nothing on it, so a
    /// multi-line selection does not break across an empty line.
    /// </summary>
    public double EmptyLineSelectionWidth
    {
        get => GetValue(EmptyLineSelectionWidthProperty);
        set => SetValue(EmptyLineSelectionWidthProperty, value);
    }

    public static readonly MewProperty<Color?> FoldingMarkerBrushProperty =
        MewProperty<Color?>.Register<TextView>(nameof(FoldingMarkerBrush), null,
            MewPropertyOptions.AffectsRender);

    /// <summary>Colour of the placeholder a folded section leaves behind. Null follows the theme.</summary>
    public Color? FoldingMarkerBrush
    {
        get => GetValue(FoldingMarkerBrushProperty);
        set => SetValue(FoldingMarkerBrushProperty, value);
    }

    public static readonly MewProperty<ColorPen?> ColumnRulerPenProperty =
        MewProperty<ColorPen?>.Register<TextView>(nameof(ColumnRulerPen), null,
            MewPropertyOptions.AffectsRender);

    // Translucent so they read on either theme, which is why these two carry a fixed default
    // instead of the null-follows-the-theme the other colours use.
    public static readonly MewProperty<Color> CurrentLineBackgroundProperty =
        MewProperty<Color>.Register<TextView>(nameof(CurrentLineBackground),
            Color.FromArgb(22, 20, 220, 224), MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<ColorPen> CurrentLineBorderProperty =
        MewProperty<ColorPen>.Register<TextView>(nameof(CurrentLineBorder),
            new ColorPen(Color.FromArgb(52, 0, 255, 110)), MewPropertyOptions.AffectsRender);

    /// <summary>Stroke of the column rule. Null follows the theme.</summary>
    public ColorPen? ColumnRulerPen
    {
        get => GetValue(ColumnRulerPenProperty);
        set => SetValue(ColumnRulerPenProperty, value);
    }

    /// <summary>Background of the line holding the caret.</summary>
    public Color CurrentLineBackground
    {
        get => GetValue(CurrentLineBackgroundProperty);
        set => SetValue(CurrentLineBackgroundProperty, value);
    }

    /// <summary>Stroke of the outline drawn around the line holding the caret.</summary>
    public ColorPen CurrentLineBorder
    {
        get => GetValue(CurrentLineBorderProperty);
        set => SetValue(CurrentLineBorderProperty, value);
    }

    internal double DpiScale => textArea.Editor.EditorDpi / 96.0;

    // Read per paint, so a theme switch repaints in the other palette without any rebuild.
    internal bool IsDarkTheme => textArea.Editor.ThemeIsDark;

    internal ColorPen ResolvedColumnRulerPen
        => ColumnRulerPen ?? new ColorPen(textArea.Editor.ControlBorderColor);

    internal Color ResolvedLinkTextForeground
        => LinkTextForegroundBrush ?? textArea.Editor.AccentColor;

    internal Color ResolvedNonPrintableCharacter
        => NonPrintableCharacterBrush ?? textArea.Editor.PlaceholderColor;

    internal Color ResolvedFoldingMarker
        => FoldingMarkerBrush ?? textArea.Editor.PlaceholderColor;

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        // No visual tree here, so AffectsRender invalidates nothing by itself.
        if (property.AffectsRender)
        {
            textArea.Editor.InvalidateTextView();
        }
    }

    /// <summary>Renderers painting into the known layers, in registration order.</summary>
    public IList<IBackgroundRenderer> BackgroundRenderers => textArea.Editor.BackgroundRenderers;

    /// <summary>Transformers restyling ranges of each visual line.</summary>
    public IList<IVisualLineTransformer> LineTransformers => textArea.Editor.LineTransformers;

    /// <summary>Generators replacing document ranges with elements that draw themselves.</summary>
    public IList<VisualLineElementGenerator> ElementGenerators => textArea.Editor.ElementGenerators;

    /// <summary>Extension pipeline of the editing surface, for MewUI-native extensions.</summary>
    public TextViewExtensionPipeline Extensions => textArea.Editor.Surface.Extensions;

    /// <summary>The editing surface as a text view host, for host-neutral extensions.</summary>
    public ITextViewHost Host => textArea.Editor.Surface;

    /// <summary>Document the view presents.</summary>
    public Document.TextDocument Document => textArea.Editor.Document;

    public string FontFamily
    {
        get => textArea.Editor.FontFamily;
        set => textArea.Editor.FontFamily = value;
    }

    public Color Foreground
    {
        get => textArea.Editor.Foreground;
        set => textArea.Editor.Foreground = value;
    }

    /// <summary>Options of the editor this view belongs to.</summary>
    public TextEditorOptions Options => textArea.Editor.Options;

    /// <summary>
    /// Services registered on this view. A colorizer puts its highlighter here so code holding only
    /// the view can find it. Document services are not in here: call <see cref="GetService"/> rather
    /// than <c>Services.GetService</c> to reach those as well.
    /// </summary>
    public ServiceContainer Services { get; } = new();

    /// <summary>
    /// The service registered on this view, or failing that the one the document carries. Null when
    /// neither has it.
    /// </summary>
    public TService? GetService<TService>() where TService : class
        => Services.GetService<TService>() ?? Document.Services.GetService<TService>();

    /// <summary>Raised after the document was replaced.</summary>
    public event EventHandler? DocumentChanged
    {
        add => textArea.Editor.DocumentChanged += value;
        remove => textArea.Editor.DocumentChanged -= value;
    }

    /// <summary>Raised after an option changed.</summary>
    public event EventHandler<MewProperty>? OptionChanged
    {
        add => Options.OptionChanged += value;
        remove => Options.OptionChanged -= value;
    }

    public void Redraw() => textArea.Editor.InvalidateTextView();

    /// <summary>Rebuilds only the lines overlapping the document range.</summary>
    public void Redraw(int offset, int length) => Host.InvalidateTextRange(offset, length);

    /// <summary>Rebuilds only the lines overlapping the segment.</summary>
    public void Redraw(ISegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        Host.InvalidateTextRange(segment.Offset, segment.Length);
    }

    /// <summary>Rebuilds one laid-out line.</summary>
    public void Redraw(VisualLine visualLine)
    {
        ArgumentNullException.ThrowIfNull(visualLine);
        Host.InvalidateTextRange(visualLine.StartOffset, visualLine.DocumentLength);
    }

    /// <summary>Repaints a layer without rebuilding any line.</summary>
    public void InvalidateLayer(KnownLayer layer) => Host.InvalidateLayer(ToAnchor(layer));

    /// <summary>Draw order of the view, in painting order.</summary>
    public IReadOnlyList<ITextViewLayer> Layers => Host.Layers.Layers;

    /// <summary>Inserts a layer relative to a known anchor.</summary>
    public void InsertLayer(ITextViewLayer layer, KnownLayer anchor, LayerInsertionPosition position)
        => Host.InsertLayer(layer, ToAnchor(anchor), position switch
        {
            LayerInsertionPosition.Replace => TextLayerPosition.Replace,
            LayerInsertionPosition.Above => TextLayerPosition.Above,
            _ => TextLayerPosition.Below
        });

    internal static TextViewLayerAnchor ToAnchor(KnownLayer layer) => layer switch
    {
        KnownLayer.Background => TextViewLayerAnchor.Background,
        KnownLayer.Selection => TextViewLayerAnchor.Selection,
        KnownLayer.Caret => TextViewLayerAnchor.Caret,
        _ => TextViewLayerAnchor.Text
    };

    /// <summary>Raised before the visible lines are built, carrying the first line number.</summary>
    public event Action<int>? VisualLineConstructionStarting
    {
        add => _constructionStarting += value;
        remove => _constructionStarting -= value;
    }

    /// <summary>Raised after the visible lines were built.</summary>
    public event Action? VisualLinesChanged
    {
        add => _linesChanged += value;
        remove => _linesChanged -= value;
    }

    /// <summary>
    /// Raised when the pointer has rested over the text. AvalonEdit pairs this with a preview event
    /// because its events are routed; MewUI has no such route, so there is one of each.
    /// </summary>
    public event EventHandler<MouseEventArgs>? MouseHover
    {
        add => EnsureHoverLogic().MouseHover += value;
        remove { if (_hoverLogic is not null) _hoverLogic.MouseHover -= value; }
    }

    /// <summary>Raised when the pointer moved on or left the text after <see cref="MouseHover"/>.</summary>
    public event EventHandler<MouseEventArgs>? MouseHoverStopped
    {
        add => EnsureHoverLogic().MouseHoverStopped += value;
        remove { if (_hoverLogic is not null) _hoverLogic.MouseHoverStopped -= value; }
    }

    /// <summary>Built on the first subscription: an editor nobody watches runs no hover timer.</summary>
    private MouseHoverLogic EnsureHoverLogic()
        => _hoverLogic ??= new MouseHoverLogic(textArea.Editor.Surface);

    /// <summary>Height of the whole document in view coordinates.</summary>
    public double DocumentHeight => Host.ExtentHeight;

    /// <summary>Height of a line holding one character, independent of content.</summary>
    public double DefaultLineHeight => Host.DefaultLineHeight;

    /// <summary>Baseline of a line holding one character.</summary>
    public double DefaultBaseline => Host.DefaultBaseline;

    /// <summary>
    /// Width of a wide space, the unit AvalonEdit sizes gutters and column rulers in. Measured here
    /// rather than taken from the core, whose tab stops are defined on the space advance.
    /// </summary>
    public double WideSpaceWidth
    {
        get
        {
            var factory = Application.IsRunning
                ? Application.Current.GraphicsFactory
                : Application.DefaultGraphicsFactory;
            var layout = factory.TextEngine.GetOrCreateLayout(
                new TextLayoutRequest
                {
                    Text = "x".AsMemory(),
                    // Measured at the density the view lays out at, or every width derived from
                    // this one lands on the wrong column above 100% scaling.
                    Dpi = textArea.Editor.EditorDpi,
                    DefaultStyle = new TextRunStyle(FontFamily, textArea.Editor.FontSize, textArea.Editor.FontWeight),
                    Paragraph = new TextParagraphStyle
                    {
                        Wrapping = TextWrapping.NoWrap,
                        MaxWidth = double.PositiveInfinity
                    }
                },
                TextLayoutCachePolicy.Content);
            return layout.MeasuredSize.Width;
        }
    }

    /// <summary>Document-space top of a one-based document line.</summary>
    public double GetVisualTopByDocumentLine(int documentLineNumber)
        => Host.GetLineY(documentLineNumber - 1);

    /// <summary>One-based document line whose row contains the document-space Y.</summary>
    public int GetDocumentLineByVisualTop(double documentY)
        => Host.FindLineByY(documentY) + 1;

    /// <summary>The laid-out line containing the document-space Y, or null when not visible.</summary>
    public VisualLine? GetVisualLineFromVisualTop(double documentY)
    {
        foreach (var line in Host.VisibleTextLines)
        {
            if (documentY >= line.DocumentY && documentY < line.DocumentY + line.Height)
            {
                return Wrap(line);
            }
        }
        return null;
    }

    /// <summary>
    /// Position at a document-space point, rounded to the nearest character boundary. Null when the
    /// point is outside the laid-out lines.
    /// </summary>
    public TextViewPosition? GetPosition(Point documentPoint)
        => GetVisualLineFromVisualTop(documentPoint.Y)?.GetTextViewPosition(documentPoint, AllowVirtualSpace);

    /// <summary>
    /// Position at a document-space point, rounded down to the character the point is inside. Null
    /// when the point is outside the laid-out lines.
    /// </summary>
    public TextViewPosition? GetPositionFloor(Point documentPoint)
        => GetVisualLineFromVisualTop(documentPoint.Y)?.GetTextViewPositionFloor(documentPoint, AllowVirtualSpace);

    /// <summary>
    /// The laid-out line of a document line, laying it out when it is off screen. Null only before
    /// the view has been laid out at all.
    /// </summary>
    public VisualLine? GetOrConstructVisualLine(DocumentLine documentLine)
    {
        ArgumentNullException.ThrowIfNull(documentLine);
        return GetOrConstructVisualLine(documentLine.Offset);
    }

    /// <summary>
    /// A line long enough to be laid out in slices has more than one, so the offset being asked
    /// about decides which one comes back.
    /// </summary>
    internal VisualLine? GetOrConstructVisualLine(int documentOffset)
    {
        var layout = Host.GetLineLayout(documentOffset);
        return layout is null ? null : Wrap(layout);
    }

    /// <summary>
    /// The laid-out line of <paramref name="documentLine"/> holding <paramref name="x"/>. A line
    /// long enough to be sliced answers with the slice that x falls in, found from the whole line's
    /// coordinates so the parts outside the viewport are not laid out to look for it.
    /// </summary>
    internal VisualLine? GetOrConstructVisualLine(DocumentLine documentLine, double x)
    {
        ArgumentNullException.ThrowIfNull(documentLine);
        var extent = Host.GetLineExtent(documentLine.Offset);
        return extent is null
            ? GetOrConstructVisualLine(documentLine.Offset)
            : GetOrConstructVisualLine(extent.SourceOffset + extent.GetOffsetForX(x));
    }

    /// <summary>Document-space position of a text view position.</summary>
    public Point GetVisualPosition(TextViewPosition position, VisualYPosition yPositionMode)
    {
        var documentLine = Document.GetLineByNumber(Math.Clamp(position.Line, 1, Document.LineCount));
        int offset = documentLine.Offset + Math.Clamp(position.Column - 1, 0, documentLine.Length);
        var visualLine = GetOrConstructVisualLine(offset);
        if (visualLine is null)
        {
            return default;
        }

        int visualColumn = visualLine.ValidateVisualColumn(offset, position.VisualColumn, AllowVirtualSpace);
        return visualLine.GetVisualPosition(visualColumn, position.IsAtEndOfLine, yPositionMode);
    }

    /// <summary>Document-space position of a document offset, at the top of its row.</summary>
    public Point GetVisualPosition(int documentOffset)
    {
        var rect = Surface.GetCharRectInWindow(documentOffset);
        var viewport = Host.TextViewportBounds;
        return new Point(
            rect.X - viewport.X + Host.ScrollOffset.X,
            rect.Y - viewport.Y + Host.ScrollOffset.Y);
    }

    /// <summary>
    /// Whether a position may sit past the end of its line. A rectangular selection needs it
    /// whatever the option says, since it spans columns rather than offsets.
    /// </summary>
    private bool AllowVirtualSpace
        => textArea.Options.EnableVirtualSpace || textArea.Selection is RectangleSelection;

    public double HorizontalOffset => Host.ScrollOffset.X;

    public double VerticalOffset => Host.ScrollOffset.Y;

    public Point ScrollOffset => Host.ScrollOffset;

    /// <summary>Raised after the scroll offset changed.</summary>
    public event Action? ScrollOffsetChanged
    {
        add => _scrollOffsetChanged += value;
        remove => _scrollOffsetChanged -= value;
    }

    /// <summary>Scrolls the smallest amount that brings the document-space rectangle into view.</summary>
    public void MakeVisible(Rect documentRect) => Host.MakeVisible(documentRect);

    /// <summary>
    /// Whether <see cref="VisualLines"/> can be read. False only before the view has been laid out;
    /// unlike the original, lines here are rebuilt every frame and so are never stale.
    /// </summary>
    public bool VisualLinesValid => Host.VisibleTextLines.Count > 0;

    /// <summary>
    /// Lines currently laid out, in document order. Rebuilt from the engine's materialized lines on
    /// each read, so hold one only within a single pass over the view.
    /// </summary>
    public IReadOnlyList<VisualLine> VisualLines
    {
        get
        {
            var host = Host;
            var lines = host.VisibleTextLines;
            var result = new VisualLine[lines.Count];
            for (int index = 0; index < lines.Count; index++)
            {
                result[index] = Wrap(lines[index]);
            }
            return result;
        }
    }

    /// <summary>The laid-out line containing the document line number, or null when not visible.</summary>
    public VisualLine? GetVisualLine(int documentLineNumber)
    {
        foreach (var line in Host.VisibleTextLines)
        {
            if (line.LogicalLine.LineNumber == documentLineNumber - 1)
            {
                return Wrap(line);
            }
        }
        return null;
    }

    private VisualLine Wrap(TextLineLayout line)
        => new(
            this,
            line,
            Document.GetLineByOffset(line.LogicalLine.Offset),
            textArea.Editor.ElementGeneratorAdapter.GetScannedElements(line.LogicalLine.Offset));

    internal MultiLineTextBox Surface => textArea.Editor.Surface;
}
