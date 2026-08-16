using System.Text;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Highlighting;
using Aprillz.MewUI.MewvalonEdit.Indentation;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit;

public class TextEditor : Control, ITextEditorComponent
{
    private const string PART_MARGIN_HOST = "PART_MarginHost";
    private const string PART_OVERLAY_HOST = "PART_OverlayHost";

    static TextEditor()
    {
        // So Focus() reaches the editor at all; OnGotFocus hands it on to the surface.
        FocusableProperty.OverrideDefaultValue<TextEditor>(true);
    }

    private readonly MultiLineTextBox _surface;
    private readonly LineNumberMargin _lineNumberMargin;
    private readonly System.Collections.ObjectModel.ObservableCollection<AbstractMargin> _leftMargins = [];
    private Grid? _marginHost;
    private Grid? _overlayHost;
    private FrameworkElement? _pendingOverlay;
    private Adorner? _pendingAdorner;
    private HighlightingColorizer? _colorizer;
    private readonly LineTransformerAdapter _lineTransformers;
    private readonly ElementGeneratorAdapter _elementGenerators;
    // Where the pointer last was, so the cursor can be re-asked for without it moving again.
    private Point? _lastCursorProbe;
    private bool _cursorHiddenWhileTyping;
    private ModifierKeys _lastCursorModifiers;
    private bool _rectangleDragging;
    private SingleCharacterElementGenerator? _singleCharacterGenerator;
    private LinkElementGenerator? _linkGenerator;
    private MailLinkElementGenerator? _mailLinkGenerator;
    private readonly BackgroundRendererRegistry _backgroundRenderers;

    public TextEditor()
    {
        Options = new TextEditorOptions();
        IndentationStrategy = new DefaultIndentationStrategy();
        _lineTransformers = new LineTransformerAdapter(this);
        _elementGenerators = new ElementGeneratorAdapter(this);
        _backgroundRenderers = new BackgroundRendererRegistry(this);
        Options.OptionChanged += OnOptionsChanged;
        var document = new TextDocument();
        StyleSheet = new StyleSheet();
        StyleSheet.Define<TextEditor>(CreateFrameStyle());

        // The editor owns the frame so it encloses the line number margin, as AvalonEdit's
        // templated ScrollViewer encloses TextArea's left margins. The surface paints neither
        // border nor background: a square fill would cover the frame's rounded corners from the
        // inside. Font properties are inherited, so the surface must not take local values.
        _surface = new MultiLineTextBox(document.CoreDocument)
        {
            Wrap = false,
            AcceptTab = true,
            TabSize = Options.IndentationSize,
            Background = Color.Transparent,
            BorderThickness = 0,
            CornerRadius = 0
        };
        _surface.KeyDown += OnSurfaceKeyDown;
        _surface.KeyUp += OnSurfaceKeyUp;
        _surface.GotFocus += () => TextArea!.Caret.Show();
        _surface.LostFocus += () => TextArea!.Caret.Hide();
        _surface.TextCommitted += OnTextCommitted;
        _surface.TextInput += OnSurfaceTextInput;
        _surface.MouseDown += OnSurfaceMouseDown;
        _surface.MouseMove += OnSurfaceMouseMove;
        _surface.MouseUp += OnSurfaceMouseUp;
        // The element under a stationary pointer changes when the lines are rebuilt, which is where
        // the original re-asks for the cursor as well.
        _surface.LinesChanged += _ => InvalidateCursorIfMouseWithinTextView();
        // Pass boundaries for the colorizer: the state scan up to the viewport runs before lines
        // are built, and its per-line notifications stay suppressed for the pass (core first line
        // is 0-based, document lines 1-based).
        _surface.LineConstructionStarting += (_, firstLine) => _colorizer?.OnVisualLineConstructionStarting(Document, firstLine + 1);
        _surface.LinesChanged += _ => _colorizer?.OnVisualLinesChanged();
        // The generator projection runs first so it scans raw document text; the space markers
        // then restyle whatever survives, including projected replacement text.
        _surface.Extensions.Projections.Add(_elementGenerators);
        // Ported transformers land below the whitespace markers, as AvalonEdit's baked marker
        // glyphs cannot be recolored by a colorizer.
        _surface.Extensions.Classifiers.Add(_lineTransformers);
        // After the transformers so a link underline survives the colorizer's colours.
        _surface.Extensions.Classifiers.Add(_elementGenerators);
        _surface.Extensions.Transformers.Add(_lineTransformers);
        _surface.Extensions.ElementGenerators.Add(_elementGenerators);
        // The surface's own scope routes undo to its TextBase history; this editor's undo truth is
        // the document's UndoStack (the keyboard path already intercepts it before the surface),
        // so the command path (menus, toolbars) must hit the same stack.
        _surface.Commands.Unregister(StandardCommands.Undo);
        _surface.Commands.Register(StandardCommands.Undo, this,
            static editor => editor.Document.UndoStack.Undo(),
            static editor => !editor.IsReadOnly && editor.Document.UndoStack.CanUndo);
        _surface.Commands.Unregister(StandardCommands.Redo);
        _surface.Commands.Register(StandardCommands.Redo, this,
            static editor => editor.Document.UndoStack.Redo(),
            static editor => !editor.IsReadOnly && editor.Document.UndoStack.CanRedo);
        // Copy and Cut likewise: while a rectangle is active the surface selection is empty, so
        // the command path must read the rectangle's column text instead.
        _surface.Commands.Unregister(StandardCommands.Copy);
        _surface.Commands.Register(StandardCommands.Copy, this,
            static editor => editor.CopySelectionCommand(),
            static editor => editor.HasCopyableSelection());
        _surface.Commands.Unregister(StandardCommands.Cut);
        _surface.Commands.Register(StandardCommands.Cut, this,
            static editor => editor.CutSelectionCommand(),
            static editor => !editor.IsReadOnly && editor.HasCopyableSelection());
        Commands.Register(EditingCommands.IndentSelection, this,
            static editor => editor.IndentSelection(),
            static editor => !editor.IsReadOnly && editor.IndentationStrategy is not null);
        InputMap.Map(EditingCommands.IndentSelection, new KeyGesture(Key.I, ModifierKeys.Primary));
        UpdateBuiltInElementGenerators();
        _backgroundRenderers.RegisterInto(_surface);
        _surface.InsertLayer(
            new CurrentLineLayer(Options, this), TextViewLayerAnchor.Background, TextLayerPosition.Above);
        _surface.InsertLayer(
            new ColumnRulerLayer(Options, this), TextViewLayerAnchor.Text, TextLayerPosition.Below);
        _lineNumberMargin = new LineNumberMargin { IsVisible = ShowLineNumbers };
        _lineNumberMargin.WithTheme((theme, margin) =>
            margin.Foreground = LineNumbersForeground ?? theme.Palette.PlaceholderText);
        // Assigned once the surface and the margin exist, because the change callback wires both.
        Document = document;
        Template = new DelegateControlTemplate<TextEditor>(BuildTemplate);
        TextArea = new TextArea(this);
        TextArea.TextView.Services.AddService(this);
        _leftMargins.CollectionChanged += (_, _) => OnLeftMarginsChanged();
        _leftMargins.Add(_lineNumberMargin);
    }

    /// <summary>
    /// Frames the editor like the built-in text inputs. The style lives on the editor's own
    /// StyleSheet because default styles are registered for core control types only; hover and
    /// focus resolve from IsFocusWithin, so the frame reacts while the inner surface holds focus.
    /// </summary>
    private static Style CreateFrameStyle() =>
        new(typeof(TextEditor))
        {
            Transitions =
            [
                Transition.Create(BackgroundProperty),
                Transition.Create(BorderBrushProperty),
            ],
            Setters =
            [
                Setter.Create(BackgroundProperty, theme => theme.Palette.ControlBackground),
                Setter.Create(BorderBrushProperty, theme => theme.Palette.ControlBorder),
                Setter.Create(BorderThicknessProperty, theme => theme.Metrics.ControlBorderThickness),
                Setter.Create(CornerRadiusProperty, theme => theme.Metrics.ControlCornerRadius),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(BorderBrushProperty,
                            theme => Color.Composite(theme.Palette.ControlBorder, theme.Palette.AccentBorderHotOverlay)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(BorderBrushProperty, theme => theme.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(BackgroundProperty, theme => theme.Palette.DisabledControlBackground),
                        Setter.Create(ForegroundProperty, theme => theme.Palette.DisabledText),
                    ],
                },
            ],
        };

    internal IList<AbstractMargin> LeftMargins => _leftMargins;

    private static Element BuildTemplate(TextEditor owner, ControlTemplateContext context)
    {
        var host = new Grid();
        context.Register(PART_MARGIN_HOST, host);
        // The overlay sits beside the margin host rather than inside it, because that host is
        // cleared and rebuilt whenever a margin joins or leaves and would take the overlay with it.
        var layers = new Grid().Children(host);
        context.Register(PART_OVERLAY_HOST, layers);
        // A templated control suppresses its own chrome, so the border has to draw it.
        var chrome = new Border { Child = layers, ClipToBounds = true };
        context.BindChrome(chrome);
        return chrome;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _marginHost = GetTemplateChild<Grid>(PART_MARGIN_HOST);
        _overlayHost = GetTemplateChild<Grid>(PART_OVERLAY_HOST);
        OnLeftMarginsChanged();
        if (_pendingOverlay is FrameworkElement pending)
        {
            _pendingOverlay = null;
            ShowOverlay(pending);
        }
    }

    /// <summary>
    /// Puts an element over the text, such as the search panel. Held until the template is applied
    /// when it arrives before that.
    /// </summary>
    internal void ShowOverlay(FrameworkElement element)
    {
        if (_overlayHost is not Grid overlay)
        {
            _pendingOverlay = element;
            return;
        }
        if (!overlay.Children.Contains(element))
        {
            overlay.Add(element);
        }
    }

    internal void HideOverlay(FrameworkElement element)
    {
        _pendingOverlay = null;
        _overlayHost?.Remove(element);
    }

    /// <summary>
    /// Floats an adorner over the editor on the window's adorner layer. Held until the editor
    /// reaches a window when it arrives before that, as an overlay is.
    /// </summary>
    internal void ShowAdorner(Adorner adorner)
    {
        if (AdornerLayer.GetAdornerLayer(this) is not AdornerLayer layer)
        {
            _pendingAdorner = adorner;
            return;
        }
        _pendingAdorner = null;
        layer.Add(adorner);
    }

    internal void HideAdorner(Adorner adorner)
    {
        _pendingAdorner = null;
        AdornerLayer.GetAdornerLayer(this)?.Remove(adorner);
    }

    /// <inheritdoc/>
    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (_pendingAdorner is Adorner pending && newRoot is Window)
        {
            ShowAdorner(pending);
        }
    }

    private void OnLeftMarginsChanged()
    {
        // Attachment stays here rather than in the template, so a margin is connected the moment it
        // joins the collection whether or not a layout pass has run.
        foreach (var margin in _leftMargins)
        {
            margin.TextView = TextArea.TextView;
        }
        RebuildMargins();
    }

    /// <summary>Lays the margins out as leading grid columns, outermost first.</summary>
    private void RebuildMargins()
    {
        if (_marginHost is not Grid host)
        {
            return;
        }

        host.Clear();
        host.Columns(string.Join(',', Enumerable.Repeat("Auto", _leftMargins.Count).Append("*")));
        for (int index = 0; index < _leftMargins.Count; index++)
        {
            var margin = _leftMargins[index];
            host.Children(margin);
            // After the add: adding re-parents the child and a column set before that is lost.
            margin.Column(index);
        }
        host.Children(_surface);
        _surface.Column(_leftMargins.Count);
    }

    public static readonly MewProperty<TextDocument?> DocumentProperty =
        MewProperty<TextDocument?>.Register<TextEditor>(nameof(Document), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnDocumentPropertyChanged(oldValue, newValue),
            validate: static (_, value) => ArgumentNullException.ThrowIfNull(value));

    /// <summary>Document being edited. Never null; the editor creates one for itself.</summary>
    public TextDocument Document
    {
        get => GetValue(DocumentProperty)!;
        set => SetValue(DocumentProperty, value);
    }

    private void OnDocumentPropertyChanged(TextDocument? oldValue, TextDocument? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.TextChanged -= OnDocumentTextChanged;
            oldValue.LineCountChanged -= OnDocumentLineCountChanged;
            oldValue.Surface = null;
        }
        if (newValue is null)
        {
            return;
        }

        newValue.TextChanged += OnDocumentTextChanged;
        newValue.LineCountChanged += OnDocumentLineCountChanged;
        newValue.Surface = _surface;
        _surface.Document = newValue.CoreDocument;
        OnDocumentLineCountChanged(newValue, EventArgs.Empty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private static readonly MewPropertyKey<int> LineCountPropertyKey =
        MewProperty<int>.RegisterReadOnly<TextEditor>(nameof(LineCount), 1);

    public static readonly MewProperty<int> LineCountProperty = LineCountPropertyKey.Property;

    /// <summary>Number of lines in the document.</summary>
    public int LineCount => GetValue(LineCountProperty);

    /// <summary>
    /// Runs only when the count moved, which is also the condition the line number margin re-measures
    /// on: its width follows the number of digits, not the text.
    /// </summary>
    private void OnDocumentLineCountChanged(object? sender, EventArgs e)
    {
        SetValue(LineCountPropertyKey, Document.LineCount);
        _lineNumberMargin?.SyncWidthToLineCount();
    }

    public TextEditorOptions Options { get; }
    public TextArea TextArea { get; }
    public IIndentationStrategy? IndentationStrategy { get; set; }

    /// <summary>
    /// The document text. Assigning it starts over: the caret returns to the beginning and the undo
    /// history is dropped, so the text that was there cannot be brought back.
    /// </summary>
    public string Text
    {
        get => Document.Text;
        set
        {
            // A wholesale assignment is not an edit: the core drops the undo history along with it,
            // so the editing state that pointed into the old text has to go the same way a document
            // swap takes it. Dropping the selection first also keeps a rectangle from mapping its
            // corners across a change whose removed text was never materialized.
            TextArea.ClearSelection();
            // Through the surface, whose own setter drops the history the way the original's
            // UndoStack.ClearAll does; Document.Text alone would leave the replace undoable.
            _surface.Text = value ?? string.Empty;
            CaretOffset = 0;
            // The assignment left no undo to return through, so the text now in the document is the
            // only state there is to be original. The original marks this modified because there an
            // assignment is an ordinary undoable edit.
            Document.UndoStack.MarkAsOriginalFile();
        }
    }

    public static readonly MewProperty<IHighlightingDefinition?> SyntaxHighlightingProperty =
        MewProperty<IHighlightingDefinition?>.Register<TextEditor>(nameof(SyntaxHighlighting), null,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.ApplyHighlighting());

    public IHighlightingDefinition? SyntaxHighlighting
    {
        get => GetValue(SyntaxHighlightingProperty);
        set => SetValue(SyntaxHighlightingProperty, value);
    }

    public static readonly MewProperty<bool> WordWrapProperty =
        MewProperty<bool>.Register<TextEditor>(nameof(WordWrap), false,
            MewPropertyOptions.AffectsLayout,
            static (self, _, newValue) => self._surface.Wrap = newValue);

    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    public static readonly MewProperty<bool> IsReadOnlyProperty =
        MewProperty<bool>.Register<TextEditor>(nameof(IsReadOnly), false,
            MewPropertyOptions.None,
            static (self, _, newValue) => self._surface.IsReadOnly = newValue);

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly MewProperty<bool> ShowLineNumbersProperty =
        MewProperty<bool>.Register<TextEditor>(nameof(ShowLineNumbers), false,
            MewPropertyOptions.AffectsLayout,
            static (self, _, newValue) => self._lineNumberMargin.IsVisible = newValue);

    public bool ShowLineNumbers
    {
        get => GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    public static readonly MewProperty<Color?> LineNumbersForegroundProperty =
        MewProperty<Color?>.Register<TextEditor>(nameof(LineNumbersForeground), null,
            MewPropertyOptions.AffectsRender,
            static (self, _, newValue) => self.ApplyLineNumbersForeground(newValue));

    /// <summary>Colour of the line numbers. Null follows the theme.</summary>
    public Color? LineNumbersForeground
    {
        get => GetValue(LineNumbersForegroundProperty);
        set => SetValue(LineNumbersForegroundProperty, value);
    }

    private void ApplyLineNumbersForeground(Color? value)
    {
        // A local value, or the inherited Foreground would hand the numbers the body text colour.
        _lineNumberMargin.Foreground = value ?? Theme.Palette.PlaceholderText;
    }

    public int CaretOffset
    {
        get => _surface.CaretPosition;
        set => _surface.CaretPosition = value;
    }

    public int SelectionStart => _surface.SelectionStart;
    public int SelectionLength => _surface.SelectionLength;
    public string SelectedText => _surface.SelectedText;
    public bool CanUndo => _surface.CanUndo;
    public bool CanRedo => _surface.CanRedo;
    public double VerticalOffset => _surface.VerticalOffset;
    public double HorizontalOffset => _surface.HorizontalOffset;

    /// <summary>Height of the whole document.</summary>
    public double ExtentHeight => _surface.ExtentHeight;

    /// <summary>
    /// Width of the widest line laid out so far. Lines not reached yet count as empty, so the value
    /// grows while the document is scrolled through.
    /// </summary>
    public double ExtentWidth => _surface.ExtentWidth;

    /// <summary>Width of the viewport the text is shown in.</summary>
    public double ViewportWidth => _surface.TextViewportBounds.Width;

    /// <summary>Height of the viewport the text is shown in.</summary>
    public double ViewportHeight => _surface.TextViewportBounds.Height;

    /// <summary>Scrolls to the vertical position, as far as the document allows.</summary>
    public void ScrollToVerticalOffset(double offset) => ScrollToOffset(HorizontalOffset, offset);

    /// <summary>Scrolls to the horizontal position, as far as the document allows.</summary>
    public void ScrollToHorizontalOffset(double offset) => ScrollToOffset(offset, VerticalOffset);

    /// <summary>Scrolls to the start of the document, left edge included.</summary>
    public void ScrollToHome() => ScrollToOffset(0, 0);

    /// <summary>Scrolls to the end of the document, left edge included.</summary>
    public void ScrollToEnd() => ScrollToOffset(0, ExtentHeight);

    /// <summary>Scrolls one line up.</summary>
    public void LineUp() => ScrollToVerticalOffset(VerticalOffset - TextArea.TextView.DefaultLineHeight);

    /// <summary>Scrolls one line down.</summary>
    public void LineDown() => ScrollToVerticalOffset(VerticalOffset + TextArea.TextView.DefaultLineHeight);

    /// <summary>Scrolls one character to the left.</summary>
    public void LineLeft() => ScrollToHorizontalOffset(HorizontalOffset - TextArea.TextView.WideSpaceWidth);

    /// <summary>Scrolls one character to the right.</summary>
    public void LineRight() => ScrollToHorizontalOffset(HorizontalOffset + TextArea.TextView.WideSpaceWidth);

    /// <summary>Scrolls one viewport up.</summary>
    public void PageUp() => ScrollToVerticalOffset(VerticalOffset - ViewportHeight);

    /// <summary>Scrolls one viewport down.</summary>
    public void PageDown() => ScrollToVerticalOffset(VerticalOffset + ViewportHeight);

    /// <summary>Scrolls one viewport to the left.</summary>
    public void PageLeft() => ScrollToHorizontalOffset(HorizontalOffset - ViewportWidth);

    /// <summary>Scrolls one viewport to the right.</summary>
    public void PageRight() => ScrollToHorizontalOffset(HorizontalOffset + ViewportWidth);

    /// <summary>
    /// Scrolls the line into view, centred vertically. Requires the editor to have been laid out.
    /// </summary>
    public void ScrollToLine(int line) => ScrollTo(line, -1);

    /// <summary>
    /// Scrolls the line and column into view, the line centred vertically. Requires the editor to
    /// have been laid out. A move shorter than a third of the viewport is skipped, so repeatedly
    /// asking for a position already near the middle does not shift the view.
    /// </summary>
    public void ScrollTo(int line, int column)
    {
        const double MINIMUM_SCROLL_FRACTION = 0.3;
        ScrollTo(line, column, VisualYPosition.LineMiddle, ViewportHeight / 2, MINIMUM_SCROLL_FRACTION);
    }

    /// <summary>
    /// Scrolls the line and column into view. Requires the editor to have been laid out.
    /// </summary>
    /// <param name="line">Line to scroll to.</param>
    /// <param name="column">Column to scroll to. Zero or less scrolls vertically only.</param>
    /// <param name="yPositionMode">Which Y of the line is being placed.</param>
    /// <param name="referencedVerticalViewPortOffset">Where in the viewport that Y should land.</param>
    /// <param name="minimumScrollFraction">
    /// Shortest move worth making, as a fraction of the viewport. A smaller one is skipped.
    /// </param>
    public void ScrollTo(
        int line,
        int column,
        VisualYPosition yPositionMode,
        double referencedVerticalViewPortOffset,
        double minimumScrollFraction)
    {
        if (Document.LineCount == 0 || ViewportHeight <= 0)
        {
            return;
        }

        var position = TextArea.TextView.GetVisualPosition(
            new TextViewPosition(Math.Clamp(line, 1, Document.LineCount), Math.Max(1, column)), yPositionMode);
        double vertical = position.Y - referencedVerticalViewPortOffset;
        if (Math.Abs(vertical - VerticalOffset) > minimumScrollFraction * ViewportHeight)
        {
            ScrollToVerticalOffset(Math.Max(0, vertical));
        }
        if (column <= 0)
        {
            return;
        }
        if (position.X > ViewportWidth - (Caret.MINIMUM_DISTANCE_TO_VIEW_BORDER * 2))
        {
            double horizontal = Math.Max(0, position.X - (ViewportWidth / 2));
            if (Math.Abs(horizontal - HorizontalOffset) > minimumScrollFraction * ViewportWidth)
            {
                ScrollToHorizontalOffset(horizontal);
            }
        }
        else
        {
            ScrollToHorizontalOffset(0);
        }
    }

    /// <summary>
    /// Position at a point relative to the top left of this editor, or null when the point is not
    /// over the text.
    /// </summary>
    public TextViewPosition? GetPositionFromPoint(Point point)
    {
        var viewport = _surface.TextViewportBounds;
        return TextArea.TextView.GetPosition(new Point(
            point.X - viewport.X + HorizontalOffset,
            point.Y - viewport.Y + VerticalOffset));
    }

    /// <summary>
    /// Scrolls so the offsets become the scroll position. Reached through the smallest-scroll
    /// contract: a viewport-sized rectangle asked for at a position lands exactly there, clamped to
    /// what the document allows.
    /// </summary>
    private void ScrollToOffset(double horizontal, double vertical)
        => _surface.MakeVisible(new Rect(horizontal, vertical, ViewportWidth, ViewportHeight));


    internal MultiLineTextBox Surface => _surface;

    /// <summary>
    /// Hands the keyboard to the surface that carries the document. The editor is a templated
    /// control around that surface, so focusing the editor itself would leave typing nowhere to go.
    /// </summary>
    protected override void OnGotFocus()
    {
        base.OnGotFocus();
        if (!_surface.IsFocused)
        {
            _surface.Focus();
        }
    }

    /// <summary>Pixel density the text is laid out at. Generated elements measure at the same one.</summary>
    internal uint EditorDpi => GetDpi();
    internal Color WhitespaceMarkerColor => TextArea.TextView.ResolvedNonPrintableCharacter;

    internal Color PlaceholderColor => Theme.Palette.PlaceholderText;

    internal Color AccentColor => Theme.Palette.Accent;

    internal Color ControlBorderColor => Theme.Palette.ControlBorder;

    internal bool ThemeIsDark => Theme.IsDark;
    internal Color ThemeSelectionBackground => Theme.Palette.SelectionBackground;
    internal Color FoldingMarkerColor => Theme.Palette.PlaceholderText;
    internal ElementGeneratorAdapter ElementGeneratorAdapter => _elementGenerators;
    internal IList<IBackgroundRenderer> BackgroundRenderers => _backgroundRenderers.Renderers;
    internal IList<IVisualLineTransformer> LineTransformers => _lineTransformers.Transformers;
    internal IList<VisualLineElementGenerator> ElementGenerators => _elementGenerators.Generators;

    public event EventHandler? TextChanged;
    public event EventHandler? DocumentChanged;

    /// <summary>Raised when an option changed.</summary>
    public event EventHandler<MewProperty>? OptionChanged;

    /// <summary>The requested service, looked up on the text view and then on the document.</summary>
    public TService? GetService<TService>() where TService : class => TextArea.GetService<TService>();

    public static readonly MewProperty<System.Text.Encoding?> EncodingProperty =
        MewProperty<System.Text.Encoding?>.Register<TextEditor>(nameof(Encoding), null);

    /// <summary>Encoding used by <see cref="Save(Stream)"/>. <see cref="Load(Stream)"/> stores what it detected.</summary>
    public System.Text.Encoding? Encoding
    {
        get => GetValue(EncodingProperty);
        set => SetValue(EncodingProperty, value);
    }

    public void Load(string fileName)
    {
        using var stream = File.OpenRead(fileName);
        Load(stream);
    }

    /// <summary>
    /// Reads the whole stream as the document text. A byte order mark decides the encoding; without
    /// one the current <see cref="Encoding"/> is used, so a caller that knows the file's encoding
    /// sets it first. <see cref="Encoding"/> ends up at whatever was actually read.
    /// </summary>
    public void Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(stream, Encoding ?? System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        Text = reader.ReadToEnd();
        // After reading, so the reader has had its chance to detect one from the bytes.
        Encoding = reader.CurrentEncoding;
        // Assigning the text counts as an edit against the original-file marker, so what was just
        // read off disk would otherwise present as modified.
        IsModified = false;
    }

    public void Save(string fileName)
    {
        using var stream = File.Create(fileName);
        Save(stream);
    }

    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var writer = new StreamWriter(stream, Encoding ?? new System.Text.UTF8Encoding(false), leaveOpen: true);
        writer.Write(Text);
        writer.Flush();
    }

    public void Select(int start, int length) => _surface.Select(start, length);

    /// <summary>
    /// Moves the caret, keeping the selection anchor where it is when extending. Extending is how a
    /// selection whose caret sits at its start is made; <see cref="Select"/> leaves it at the end.
    /// </summary>
    public void MoveCaret(int position, bool extendSelection)
        => _surface.MoveCaret(position, extendSelection);

    public void SelectAll() => _surface.SelectAll();
    public void AppendText(string? text) => _surface.AppendText(text, scrollToCaret: true);
    /// <summary>
    /// Copies the selection, or the caret's whole line when nothing is selected and
    /// <see cref="TextEditorOptions.CutCopyWholeLine"/> allows it.
    /// </summary>
    public void Copy()
    {
        if (!TryCutCopyWholeLine(cut: false))
        {
            _surface.Copy();
        }
    }

    /// <inheritdoc cref="Copy"/>
    public void Cut()
    {
        if (!TryCutCopyWholeLine(cut: true))
        {
            _surface.Cut();
        }
    }

    public void Paste() => _surface.Paste();

    /// <summary>
    /// Opens an undo group; every edit until the matching <see cref="EndChange"/> undoes as one
    /// step. Nesting extends the outermost group.
    /// </summary>
    public void BeginChange() => Document.UndoStack.StartUndoGroup();

    /// <summary>Closes the group opened by <see cref="BeginChange"/>.</summary>
    public void EndChange() => Document.UndoStack.EndUndoGroup();

    /// <summary>
    /// An undo group that closes when the returned object is disposed, which is the shape a caller
    /// wants when the edits are made inside one scope.
    /// </summary>
    public IDisposable DeclareChangeBlock() => Document.UndoStack.OpenUndoGroup();

    /// <summary>Undoes the most recent command. False when there was nothing to undo.</summary>
    public bool Undo()
    {
        if (IsReadOnly)
        {
            return false;
        }
        // Through the stack, which is what keeps the original-file marker counting steps.
        return Document.UndoStack.Undo();
    }

    /// <summary>Redoes the most recently undone command. False when there was nothing to redo.</summary>
    public bool Redo()
    {
        if (IsReadOnly)
        {
            return false;
        }
        return Document.UndoStack.Redo();
    }

    /// <summary>
    /// Whether the document has changed since it was last marked unmodified. Undoing back to that
    /// state clears it again. Assigning false marks the current state, which is what saving does;
    /// assigning true drops the marker, so nothing counts as unmodified until one is set again.
    /// </summary>
    public bool IsModified
    {
        get => !Document.UndoStack.IsOriginalFile;
        set
        {
            if (value)
            {
                Document.UndoStack.DiscardOriginalFileMarker();
            }
            else
            {
                Document.UndoStack.MarkAsOriginalFile();
            }
        }
    }

    /// <summary>Raised after <see cref="IsModified"/> changed.</summary>
    public event EventHandler? IsModifiedChanged
    {
        add => Document.UndoStack.IsOriginalFileChanged += value;
        remove => Document.UndoStack.IsOriginalFileChanged -= value;
    }
    public void InvalidateTextView() => _surface.InvalidateTextView();

    protected override void OnDispose()
    {
        Options.OptionChanged -= OnOptionsChanged;
        Document.TextChanged -= OnDocumentTextChanged;
        base.OnDispose();
    }

    private void ApplyHighlighting()
    {
        if (_colorizer is not null)
        {
            LineTransformers.Remove(_colorizer);
            _colorizer = null;
        }
        if (SyntaxHighlighting is IHighlightingDefinition definition)
        {
            // First in the list: syntax colors are the base layer, so whitespace markers and search
            // highlights registered later keep their own colors where the ranges overlap.
            _colorizer = new HighlightingColorizer(definition);
            _colorizer.HighlightingStateChanged += RepaintHighlightedLines;
            LineTransformers.Insert(0, _colorizer);
            // The highlighter is reachable from the view alone, as in AvalonEdit, so ported code
            // that only holds a TextView can still ask for the document's highlighting state.
            TextArea.TextView.Services.AddService<IHighlighter>(_colorizer.GetHighlighter(Document));
        }
        else
        {
            TextArea.TextView.Services.RemoveService<IHighlighter>();
        }
        _surface.InvalidateTextView();
    }

    /// <summary>
    /// Rebuilds the lines a highlighting span changed the starting state of. The signal arrives
    /// while a line is being laid out; the surface absorbs that and rebuilds once the pass ends.
    /// </summary>
    private void RepaintHighlightedLines(int fromLineNumber, int toLineNumber)
    {
        var document = Document;
        int first = Math.Clamp(fromLineNumber, 1, document.LineCount);
        int last = Math.Clamp(toLineNumber, first, document.LineCount);
        int start = document.GetLineByNumber(first).Offset;
        var lastLine = document.GetLineByNumber(last);
        _surface.InvalidateTextRange(start, lastLine.Offset + lastLine.TotalLength - start);
    }

    // The document raises this after its Changed handlers, so anything anchored to the document has
    // already moved. A listener that recomputes offsets from the new text, such as a folding
    // strategy, would otherwise have its result shifted a second time.
    private void OnDocumentTextChanged(object? sender, EventArgs e)
        => TextChanged?.Invoke(this, EventArgs.Empty);

    private bool HasCopyableSelection()
        => TextArea.Selection is RectangleSelection rectangle
            ? rectangle.Length > 0
            : _surface.SelectionLength > 0;

    private void CopySelectionCommand()
    {
        if (!TextArea.CopyRectangleSelection())
        {
            _surface.Copy();
        }
    }

    private void CutSelectionCommand()
    {
        if (TextArea.CopyRectangleSelection())
        {
            if (TextArea.Selection is RectangleSelection rectangle)
            {
                rectangle.ReplaceSelectionWithText(string.Empty);
            }
        }
        else
        {
            _surface.Cut();
        }
    }

    /// <summary>
    /// Runs <see cref="IndentationStrategy"/> over the selected lines, or the whole document when
    /// nothing is selected, as one undo step. The default strategy reindents nothing, so this does
    /// something only where a host supplied one that reads the language.
    /// </summary>
    public void IndentSelection()
    {
        if (IndentationStrategy is not IIndentationStrategy strategy || IsReadOnly)
        {
            return;
        }
        int first = 1;
        int last = Document.LineCount;
        if (TextArea.Selection.SurroundingSegment is ISegment segment)
        {
            first = Document.GetLineByOffset(segment.Offset).LineNumber;
            last = Document.GetLineByOffset(segment.EndOffset).LineNumber;
        }
        Document.RunUpdate(() => strategy.IndentLines(Document, first, last));
        TextArea.Caret.BringCaretToView();
    }

    private void OnSurfaceMouseDown(MouseEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }
        if (e.Button == MouseButton.Left && e.AltKey && Options.EnableRectangularSelection)
        {
            StartRectangleDrag(e);
            if (e.Handled)
            {
                return;
            }
        }
        // Ahead of the surface's own caret placement: its OnMouseDown raises this event first and
        // honors Handled, which is the AvalonEdit "if (!e.Handled) route to element" structure.
        FindElementAtPoint(ToWindowPoint(e))?.OnMouseDown(e);
    }

    /// <summary>
    /// Alt+drag draws a rectangular selection. Claiming the press keeps the surface's own drag
    /// selection out of it, so the drag loop and the capture are the editor's to manage.
    /// </summary>
    private void StartRectangleDrag(MouseEventArgs e)
    {
        if (GetRectanglePosition(e) is not TextViewPosition position)
        {
            return;
        }
        _surface.Focus();
        // Alt+Shift+click extends an existing rectangle instead of starting a new one.
        if (e.ShiftKey && TextArea.Selection is RectangleSelection existing)
        {
            TextArea.Selection = existing.SetEndpoint(position);
        }
        else
        {
            TextArea.Selection = new RectangleSelection(TextArea, position, position);
        }
        _rectangleDragging = true;
        if (FindVisualRoot() is Window window)
        {
            window.CaptureMouse(_surface);
        }
        e.Handled = true;
    }

    private void OnSurfaceMouseMove(MouseEventArgs e)
    {
        if (_rectangleDragging &&
            TextArea.Selection is RectangleSelection rectangle &&
            GetRectanglePosition(e) is TextViewPosition dragPosition)
        {
            TextArea.Selection = rectangle.SetEndpoint(dragPosition);
        }
        _lastCursorProbe = ToWindowPoint(e);
        _lastCursorModifiers = e.Modifiers;
        UpdateCursor();
    }

    private void OnSurfaceMouseUp(MouseEventArgs e)
    {
        if (!_rectangleDragging)
        {
            return;
        }
        _rectangleDragging = false;
        if (FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// Position under the pointer with virtual space allowed, which a rectangle needs before it
    /// exists and so cannot leave to the view's own option-driven policy.
    /// </summary>
    private TextViewPosition? GetRectanglePosition(MouseEventArgs e)
    {
        var window = ToWindowPoint(e);
        var viewport = _surface.TextViewportBounds;
        ITextViewHost host = _surface;
        var documentPoint = new Point(
            window.X - viewport.X + host.ScrollOffset.X,
            window.Y - viewport.Y + host.ScrollOffset.Y);
        return TextArea.TextView.GetVisualLineFromVisualTop(documentPoint.Y)
            ?.GetTextViewPosition(documentPoint, allowVirtualSpace: true);
    }

    /// <summary>
    /// Re-asks the element under the pointer which cursor to show, for when the lines were rebuilt
    /// beneath a pointer that did not move. Does nothing while the pointer is elsewhere, since
    /// updating the cursor from outside the view makes it flicker over a window border.
    /// </summary>
    internal void InvalidateCursorIfMouseWithinTextView()
    {
        // Typing rebuilds the lines, and re-asking here would undo the hiding on the very keystroke
        // that asked for it. Only the pointer moving brings it back.
        if (_surface.IsMouseOver && !_cursorHiddenWhileTyping)
        {
            UpdateCursor();
        }
    }

    /// <summary>
    /// Re-asks the element under the pointer with the modifiers a key press just changed, for an
    /// element that answers differently under them. A link asked with Control is a hand, so holding
    /// the key over one has to change the cursor without the pointer moving, and letting go of it
    /// has to change the cursor back.
    /// </summary>
    private void RefreshCursorForModifiers(ModifierKeys modifiers)
    {
        if (_lastCursorModifiers == modifiers)
        {
            return;
        }
        _lastCursorModifiers = modifiers;
        InvalidateCursorIfMouseWithinTextView();
    }

    /// <summary>
    /// Takes the pointer out of the way of what is being typed. It comes back the moment the
    /// pointer moves, which is where <see cref="UpdateCursor"/> takes over again.
    /// </summary>
    private void HideCursorWhileTyping()
    {
        if (Options.HideCursorWhileTyping && _surface.IsMouseOver)
        {
            _cursorHiddenWhileTyping = true;
            _surface.Cursor = CursorType.None;
        }
    }

    private void UpdateCursor()
    {
        if (_lastCursorProbe is not Point position)
        {
            return;
        }
        _cursorHiddenWhileTyping = false;
        if (FindElementAtPoint(position) is not VisualLineElement element)
        {
            _surface.Cursor = CursorType.IBeam;
            return;
        }
        var query = new QueryCursorEventArgs(position, _lastCursorModifiers);
        element.OnQueryCursor(query);
        _surface.Cursor = query.Cursor ?? CursorType.IBeam;
    }

    private Point ToWindowPoint(MouseEventArgs e)
    {
        var local = e.GetPosition(_surface);
        return new Point(local.X + _surface.Bounds.X, local.Y + _surface.Bounds.Y);
    }

    private VisualLineElement? FindElementAtPoint(Point position)
    {
        var viewport = _surface.TextViewportBounds;
        if (!viewport.Contains(position))
        {
            return null;
        }
        ITextViewHost host = _surface;
        double documentX = position.X - viewport.X + host.ScrollOffset.X;
        double documentY = position.Y - viewport.Y + host.ScrollOffset.Y;
        foreach (var line in host.VisibleTextLines)
        {
            if (documentY < line.DocumentY || documentY >= line.DocumentY + line.Height)
            {
                continue;
            }
            var hit = line.HitTest(new Point(documentX - line.DocumentX, documentY - line.DocumentY));
            int sourceOffset = line.MapProjectedOffsetToSource(hit.FirstCharacterIndex);
            return _elementGenerators.FindElementAt(line.LogicalLine.Offset + sourceOffset);
        }
        return null;
    }

    /// <summary>
    /// Claims Enter so the inserted terminator matches the one already in use around the caret.
    /// The surface would insert a line feed, which turns a CRLF file into a mixed one.
    /// </summary>
    private void OnSurfaceKeyDown(KeyEventArgs e)
    {
        RefreshCursorForModifiers(e.Modifiers);

        // Ahead of everything the editor does with the key: a stacked handler exists to take the
        // keyboard away from the editor, which it cannot do after the editor has acted.
        TextArea.HandleKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (!e.Handled && e.PrimaryKey && e.Key is Key.C or Key.X
            && TryCutCopyWholeLine(cut: e.Key == Key.X))
        {
            e.Handled = true;
            return;
        }

        if (e.Handled || e.Key != Key.Enter || IsReadOnly || !Document.CoreDocument.PreservesLineEndings)
        {
            return;
        }
        string newLine = TextUtilities.GetNewLineFromDocument(Document, Document.GetLocation(CaretOffset).Line);
        if (newLine == "\n")
        {
            return;
        }
        e.Handled = true;
        _surface.ReplaceSelection(newLine);
    }

    private void OnSurfaceKeyUp(KeyEventArgs e)
    {
        RefreshCursorForModifiers(e.Modifiers);
        TextArea.HandleKeyUp(e);
    }

    /// <summary>
    /// Copies the caret's whole line when nothing is selected, terminator included, and cuts it out
    /// for a cut. Returns false when the option is off or something is selected, leaving the plain
    /// selection copy to the surface.
    /// </summary>
    private bool TryCutCopyWholeLine(bool cut)
    {
        if (!Options.CutCopyWholeLine || SelectionLength > 0 || (cut && IsReadOnly))
        {
            return false;
        }
        var line = Document.GetLineByNumber(Document.GetLocation(CaretOffset).Line);
        if (line.TotalLength == 0)
        {
            return false;
        }
        // Through the surface's own clipboard path, which is the only one open to an extension.
        // Selecting the line first is what makes it copy the line rather than nothing.
        int caret = CaretOffset;
        _surface.Select(line.Offset, line.TotalLength);
        if (cut)
        {
            _surface.Cut();
        }
        else
        {
            _surface.Copy();
            _surface.Select(caret, 0);
        }
        return true;
    }

    /// <summary>
    /// Indents the line the caret landed on after a line break, which is where the original applies
    /// the strategy. A line that is not fully editable is left alone, as there too.
    /// </summary>
    private void OnTextCommitted(string text)
    {
        if (IndentationStrategy is null || !TextUtilities.IsNewLine(text))
        {
            return;
        }
        var line = Document.GetLineByNumber(Document.GetLocation(CaretOffset).Line);
        if (!IsFullyEditable(line))
        {
            return;
        }
        IndentationStrategy.IndentLine(Document, line);
    }

    private bool IsFullyEditable(DocumentLine line)
    {
        var provider = TextArea.ReadOnlySectionProvider;
        if (provider is null)
        {
            return true;
        }
        var deletable = provider.GetDeletableSegments(new SimpleSegment(line.Offset, line.Length)).ToArray();
        return deletable.Length == 1
            && deletable[0].Offset == line.Offset
            && deletable[0].Length == line.Length;
    }

    private void OnSurfaceTextInput(TextInputEventArgs e)
    {
        HideCursorWhileTyping();
        if (!Options.ConvertTabsToSpaces || string.IsNullOrEmpty(e.Text) || !e.Text.Contains('\t'))
        {
            return;
        }
        e.Handled = true;
        InsertTextInput(e.Text);
    }

    /// <summary>
    /// Inserts text as if typed. Both the keyboard path and the programmatic one come through here,
    /// so a tab converts the same way whichever put it in.
    /// </summary>
    internal void InsertTextInput(string text)
        => _surface.ReplaceSelection(
            Options.ConvertTabsToSpaces && text.Contains('\t')
                ? ExpandTabs(text, Document.GetLocation(SelectionStart).Column)
                : text);

    /// <summary>
    /// Replaces every tab with spaces reaching the next indentation stop, starting from the column
    /// the text is going into. A whole indent per tab would overshoot every stop but the first.
    /// </summary>
    private string ExpandTabs(string text, int column)
    {
        var expanded = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            if (character == '\t')
            {
                string spaces = Options.GetIndentationString(column);
                expanded.Append(spaces);
                column += spaces.Length;
            }
            else
            {
                expanded.Append(character);
                column = character is '\n' or '\r' ? 1 : column + 1;
            }
        }
        return expanded.ToString();
    }

    private void OnOptionsChanged(object? sender, MewProperty option)
    {
        _surface.TabSize = Options.IndentationSize;
        _surface.ImeMode = Options.EnableImeSupport ? ImeMode.Auto : ImeMode.Disabled;
        UpdateBuiltInElementGenerators();
        // A generator reads the options while it scans, and the scan cache keys on the document
        // version alone, so without this a toggled option waits for the next edit to show.
        _elementGenerators.InvalidateScans();
        _surface.InvalidateTextView();
        OptionChanged?.Invoke(this, option);
    }

    /// <summary>
    /// Attaches and detaches the link generators the options ask for, so an editor gets them without
    /// registering anything. The original builds them the same way, from the same two options.
    /// </summary>
    private void UpdateBuiltInElementGenerators()
    {
        SetBuiltInGenerator(
            ref _singleCharacterGenerator,
            Options.ShowSpaces || Options.ShowTabs || Options.ShowBoxForControlCharacters,
            () => new SingleCharacterElementGenerator(Options, this));
        SetBuiltInGenerator(ref _linkGenerator, Options.EnableHyperlinks, static () => new LinkElementGenerator());
        SetBuiltInGenerator(ref _mailLinkGenerator, Options.EnableEmailHyperlinks, static () => new MailLinkElementGenerator());
        if (_linkGenerator is not null)
        {
            _linkGenerator.RequireControlModifierForClick = Options.RequireControlModifierForHyperlinkClick;
        }
        if (_mailLinkGenerator is not null)
        {
            _mailLinkGenerator.RequireControlModifierForClick = Options.RequireControlModifierForHyperlinkClick;
        }
    }

    private void SetBuiltInGenerator<TGenerator>(
        ref TGenerator? generator,
        bool wanted,
        Func<TGenerator> create)
        where TGenerator : VisualLineElementGenerator
    {
        if (wanted == (generator is not null))
        {
            return;
        }
        if (wanted)
        {
            generator = create();
            ElementGenerators.Add(generator);
        }
        else
        {
            ElementGenerators.Remove(generator!);
            generator = null;
        }
    }
}
