using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Indentation;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text.Editing;

namespace Aprillz.MewUI.MewvalonEdit.Editing;

public sealed class TextArea : MewObject, ITextEditorComponent
{
    private readonly TextEditor _editor;
    private SelectionLayer? _selectionLayer;
    private Selection _selection;
    private bool _applyingSelection;
    private TextDocument _observedDocument;
    private List<DocumentChangeEventArgs>? _pendingSelectionUpdates;
    private readonly List<TextAreaStackedInputHandler> _stackedInputHandlers = [];
    private ITextAreaInputHandler? _activeInputHandler;

    internal TextArea(TextEditor editor)
    {
        _editor = editor;
        Caret = new Caret(this);
        EmptySelection = new EmptySelection(this);
        _selection = EmptySelection;
        // A rectangular selection is the extension's own state, so unlike the simple selection it
        // is not re-derived from the surface and has to ride document changes itself.
        _observedDocument = editor.Document;
        _observedDocument.Changed += OnDocumentChangedForSelection;
        editor.DocumentChanged += OnEditorDocumentChanged;
        TextView = new TextView(this);
        TextView.Services.AddService(this);
        // Taken unconditionally: the caret's colour, its visibility and its overstrike width all
        // live here, and a caret that only sometimes belongs to the editor would answer differently
        // depending on whether one of them had been touched.
        TextView.InsertLayer(new CaretLayer(this), KnownLayer.Caret, LayerInsertionPosition.Replace);
        editor.Surface.EditingStateChanged += OnEditingStateChanged;
        editor.Surface.TextInput += OnTextInput;
        DefaultInputHandler = new TextAreaDefaultInputHandler(this);
        ActiveInputHandler = DefaultInputHandler;
    }

    public TextDocument Document => _editor.Document;
    public TextEditorOptions Options => _editor.Options;
    public Caret Caret { get; }
    public TextView TextView { get; }

    /// <summary>The one empty selection of this text area, which is what an empty selection is.</summary>
    internal Selection EmptySelection { get; }

    /// <summary>
    /// What is selected. Assigning moves the editing surface, and a selection the surface makes on
    /// its own replaces this with the matching range.
    /// </summary>
    /// <remarks>
    /// A selection keeps the direction it was made in: which of its two positions the caret sits at
    /// decides where a replacement leaves the caret, and it is what a rectangular selection grows
    /// along.
    /// </remarks>
    public Selection Selection
    {
        get
        {
            FlushPendingSelectionUpdates();
            return _selection;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            // A newly assigned selection was built against the current document, so changes that
            // predate it must not be replayed onto it.
            _pendingSelectionUpdates = null;
            if (_selection.Equals(value))
            {
                return;
            }
            _selection = value;
            if (value is RectangleSelection)
            {
                // The surface holds no range while a rectangle is active, so the host paints no
                // selection; the segment layer must exist even when no appearance was ever set.
                ResolveSelectionLayer(install: true);
            }
            ApplyToSurface(value);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyToSurface(Selection selection)
    {
        _applyingSelection = true;
        try
        {
            if (selection is RectangleSelection rectangle)
            {
                // The surface cannot represent a column block. It keeps an empty selection with
                // the caret on the rectangle's active corner, and the rectangle itself stays the
                // extension's own state.
                _editor.MoveCaret(
                    Document.GetOffset(rectangle.EndPosition.Line, rectangle.EndPosition.Column),
                    extendSelection: false);
                // The corner may sit in virtual space; the caret keeps that column beside the
                // clamped surface offset.
                Caret.Position = rectangle.EndPosition;
            }
            else if (selection.SurroundingSegment is ISegment segment)
            {
                // Anchored at the start position and extended to the end, rather than selected as a
                // range: a range leaves the caret at the higher offset, which would drop the
                // direction of a selection made backwards on the way to the surface.
                int anchor = Document.GetOffset(selection.StartPosition.Line, selection.StartPosition.Column);
                int caret = Document.GetOffset(selection.EndPosition.Line, selection.EndPosition.Column);
                if (anchor == segment.Offset || anchor == segment.EndOffset)
                {
                    _editor.MoveCaret(anchor, extendSelection: false);
                    _editor.MoveCaret(caret, extendSelection: true);
                }
                else
                {
                    _editor.Select(segment.Offset, segment.Length);
                }
            }
            else
            {
                _editor.Select(_editor.CaretOffset, 0);
            }
        }
        finally
        {
            _applyingSelection = false;
        }
    }

    /// <summary>
    /// Margins placed left of the text, outermost first. Adding one attaches it to the view; the
    /// line number margin is the built-in entry that <see cref="TextEditor.ShowLineNumbers"/> adds
    /// and removes.
    /// </summary>
    public IList<AbstractMargin> LeftMargins => _editor.LeftMargins;

    /// <summary>The requested service, or null when neither the view nor the document has it.</summary>
    public TService? GetService<TService>() where TService : class => TextView.GetService<TService>();

    public IIndentationStrategy? IndentationStrategy
    {
        get => _editor.IndentationStrategy;
        set => _editor.IndentationStrategy = value;
    }

    public event EventHandler? SelectionChanged;
    public event Action<TextInputEventArgs>? TextEntering;

    /// <summary>
    /// Raised after typed or composed text reached the document, once per commit. During an IME
    /// composition only the final commit raises it, which makes it the completion trigger point.
    /// </summary>
    public event Action<string>? TextEntered
    {
        add => _editor.Surface.TextCommitted += value;
        remove => _editor.Surface.TextCommitted -= value;
    }

    public static readonly MewProperty<Color?> SelectionBrushProperty =
        MewProperty<Color?>.Register<TextArea>(nameof(SelectionBrush), null,
            MewPropertyOptions.AffectsRender,
            static (self, _, newValue) =>
            {
                var layer = self.ResolveSelectionLayer(newValue.HasValue);
                if (layer is not null)
                {
                    layer.Background = newValue;
                }
            });

    /// <summary>Background painted behind the selection. Null restores the theme's selection.</summary>
    public Color? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>
    /// Color the selected glyphs are painted in. Null leaves them as they are, matching the
    /// original, where a null brush makes the selection colorizer keep the existing foreground.
    /// </summary>
    public Color? SelectionForeground
    {
        get => _editor.Surface.SelectionForeground;
        set => _editor.Surface.SelectionForeground = value;
    }

    public static readonly MewProperty<Color?> SelectionBorderProperty =
        MewProperty<Color?>.Register<TextArea>(nameof(SelectionBorder), null,
            MewPropertyOptions.AffectsRender,
            static (self, _, newValue) =>
            {
                var layer = self.ResolveSelectionLayer(newValue.HasValue);
                if (layer is not null)
                {
                    layer.Border = newValue;
                }
            });

    /// <summary>Outline drawn around the selection. Null draws none.</summary>
    public Color? SelectionBorder
    {
        get => GetValue(SelectionBorderProperty);
        set => SetValue(SelectionBorderProperty, value);
    }

    public static readonly MewProperty<bool> OverstrikeModeProperty =
        MewProperty<bool>.Register<TextArea>(nameof(OverstrikeMode), false,
            MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Whether typing overwrites the character at the caret rather than inserting before it. The
    /// caret covers that character while it is on, translucently, so the character stays readable.
    /// </summary>
    public bool OverstrikeMode
    {
        get => GetValue(OverstrikeModeProperty);
        set => SetValue(OverstrikeModeProperty, value);
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        // No visual tree here, so AffectsRender invalidates nothing by itself.
        if (property.AffectsRender)
        {
            _editor.InvalidateTextView();
        }
    }

    /// <summary>
    /// The replacement selection layer, installed on the first appearance change. Replacing the
    /// anchor stops the host painting its own selection, so clearing a property back to its default
    /// must not install one; an editor that only ever clears keeps the theme's selection untouched.
    /// </summary>
    private SelectionLayer? ResolveSelectionLayer(bool install)
    {
        if (_selectionLayer is null && install)
        {
            _selectionLayer = new SelectionLayer(this);
            TextView.InsertLayer(_selectionLayer, KnownLayer.Selection, LayerInsertionPosition.Replace);
        }
        return _selectionLayer;
    }

    /// <summary>Consulted before every edit. Null leaves the document fully editable.</summary>
    public IReadOnlySectionProvider? ReadOnlySectionProvider
    {
        get => (_editor.Surface.EditableRegions as ReadOnlySectionAdapter)?.Provider;
        set => _editor.Surface.EditableRegions = value is null ? null : new ReadOnlySectionAdapter(value);
    }

    /// <summary>Raised after the document was replaced.</summary>
    public event EventHandler? DocumentChanged
    {
        add => _editor.DocumentChanged += value;
        remove => _editor.DocumentChanged -= value;
    }

    /// <summary>Raised after an option changed.</summary>
    public event EventHandler<MewProperty>? OptionChanged
    {
        add => Options.OptionChanged += value;
        remove => Options.OptionChanged -= value;
    }

    public void ReplaceSelection(string? text) => _editor.Surface.ReplaceSelection(text);

    /// <summary>
    /// Inserts text as if typed, whatever the selection: a rectangle writes every line it covers,
    /// overstrike takes the place of the character in front of the caret, and anything else
    /// replaces the selection. The keyboard lands in the same handling, so a programmatic call and
    /// a keystroke produce the same document.
    /// </summary>
    public void PerformTextInput(string text)
    {
        text ??= string.Empty;
        if (text.Length == 0)
        {
            return;
        }
        if (Selection is RectangleSelection rectangle)
        {
            rectangle.ReplaceSelectionWithText(text);
            return;
        }
        if (TryGetOverstrikeRange(text, out int start, out int length))
        {
            // The typed-range path: undo returns to the caret and no selection is disturbed, so a
            // box selection in progress survives the keystroke.
            _editor.Surface.EnterText(start, length, text);
            _editor.Surface.ScrollToCaret();
            return;
        }
        Selection.ReplaceSelectionWithText(text);
    }

    /// <summary>
    /// The range an overstrike keystroke takes the place of: the character in front of the caret.
    /// False where there is nothing to take the place of - a line ending, the end of a line, or a
    /// selection - which is where overstrike inserts like any other keystroke.
    /// </summary>
    private bool TryGetOverstrikeRange(string text, out int start, out int length)
    {
        start = Caret.Offset;
        length = 0;
        if (!OverstrikeMode || !Selection.IsEmpty || text is "\n" or "\r" or "\r\n")
        {
            return false;
        }
        var line = Document.GetLineByOffset(start);
        if (start >= line.EndOffset)
        {
            return false;
        }
        int next = TextUtilities.GetNextCaretPosition(
            Document, start, Aprillz.MewUI.Text.LogicalDirection.Forward, CaretPositioningMode.Normal);
        if (next <= start || next > line.EndOffset)
        {
            return false;
        }
        length = next - start;
        return true;
    }

    /// <summary>Collapses the selection to the caret.</summary>
    public void ClearSelection() => Selection = EmptySelection;

    /// <summary>
    /// Marks the surface changes inside <paramref name="action"/> as the extension's own, so the
    /// selection is not re-derived from them. A box-selection step moves the caret before it moves
    /// the rectangle's corner, and re-deriving in between would dissolve the rectangle mid-step.
    /// </summary>
    internal void RunOwningSurface(Action action)
    {
        bool previous = _applyingSelection;
        _applyingSelection = true;
        try
        {
            action();
        }
        finally
        {
            _applyingSelection = previous;
        }
    }

    private void OnEditingStateChanged()
    {
        // After the edit finished recording, which is the first moment the history answers for it.
        Document.NotifyUndoHistoryChanged();
        Caret.RaisePositionChanged();
        if (!_applyingSelection)
        {
            FlushPendingSelectionUpdates();
            int start = _editor.SelectionStart;
            int end = start + _editor.SelectionLength;
            if (_selection is RectangleSelection rectangle
                && end == start && (CaretSitsOnCorner(rectangle) || SurfaceIsComposing))
            {
                // The caret resting on the rectangle's active corner is the rectangle's own
                // bookkeeping, and so is the preedit an IME composes there: the caret rides the
                // preedit until the committed text arrives for the rectangle to write. Anything
                // else the surface did (a click, a plain caret move, a drag) dissolves the
                // rectangle into what the surface holds.
            }
            else
            {
                // Which end the caret sits at is which way the selection was made. The surface
                // reports the range with the smaller offset first, so reading it straight would
                // turn every backwards drag into a forwards selection, and replacing one would
                // leave the caret at the wrong end of the new text.
                _selection = end > start && _editor.CaretOffset == start
                    ? Selection.Create(this, end, start)
                    : Selection.Create(this, start, end);
            }
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool SurfaceIsComposing => ((ITextCompositionClient)_editor.Surface).IsComposing;

    /// <summary>
    /// The rectangle cannot be rebuilt inside the change notification: the surface's layout has
    /// not seen the change yet and constructing a visual line against it reads stale line states.
    /// The changes queue up and replay when the selection is next read or the surface settles.
    /// </summary>
    private void OnDocumentChangedForSelection(object? sender, DocumentChangeEventArgs e)
    {
        if (_selection is RectangleSelection)
        {
            (_pendingSelectionUpdates ??= []).Add(e);
        }
    }

    private void FlushPendingSelectionUpdates()
    {
        if (_pendingSelectionUpdates is null || _pendingSelectionUpdates.Count == 0)
        {
            return;
        }
        var pending = _pendingSelectionUpdates;
        _pendingSelectionUpdates = null;
        foreach (var change in pending)
        {
            if (_selection is RectangleSelection rectangle)
            {
                _selection = rectangle.UpdateOnDocumentChange(change);
            }
        }
    }

    private void OnEditorDocumentChanged(object? sender, EventArgs e)
    {
        _observedDocument.Changed -= OnDocumentChangedForSelection;
        _observedDocument = _editor.Document;
        _observedDocument.Changed += OnDocumentChangedForSelection;
        // A selection holds offsets of the document it was made in, and none of them mean anything
        // in the replacement. A surviving rectangle would hand the old offsets to the selection
        // layer on the next render pass.
        _pendingSelectionUpdates = null;
        if (!ReferenceEquals(_selection, EmptySelection))
        {
            _selection = EmptySelection;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CaretSitsOnCorner(RectangleSelection rectangle)
    {
        var corner = rectangle.EndPosition;
        if (corner.Line < 1 || corner.Line > Document.LineCount)
        {
            return false;
        }
        var line = Document.GetLineByNumber(corner.Line);
        if (corner.Column < 1 || corner.Column > line.Length + 1)
        {
            return false;
        }
        return _editor.CaretOffset == Document.GetOffset(corner.Line, corner.Column);
    }

    private void OnTextInput(TextInputEventArgs args)
    {
        TextEntering?.Invoke(args);
        if (args.Handled || string.IsNullOrEmpty(args.Text) || _editor.IsReadOnly)
        {
            return;
        }
        // A rectangle writes every line it covers, and overstrike replaces a range: the surface,
        // whose selection stays empty in both, would insert at the caret only. Both are claims.
        if (Selection is RectangleSelection || TryGetOverstrikeRange(args.Text, out _, out _))
        {
            PerformTextInput(args.Text);
            args.Handled = true;
            return;
        }
        // A caret standing in virtual space owns columns the document does not have yet; the text
        // grows the spaces that create them and the surface inserts it as one keystroke.
        if (Selection.IsEmpty)
        {
            args.Text = Selection.AddSpacesIfRequired(args.Text, Caret.Position, Caret.Position);
        }
    }

    /// <summary>
    /// Puts the rectangle's column text on the clipboard, lines joined with line breaks. False
    /// when there is no rectangle, nothing in it, or no clipboard to write to.
    /// </summary>
    internal bool CopyRectangleSelection()
    {
        if (Selection is not RectangleSelection rectangle || rectangle.Length == 0)
        {
            return false;
        }
        var clipboard = _editor.Surface.ClipboardService
            ?? (Application.IsRunning ? Application.Current.PlatformServices.Clipboard : null);
        return clipboard?.TrySetText(rectangle.GetText()) == true;
    }

    internal bool TryGetClipboardText(out string text)
    {
        text = string.Empty;
        var clipboard = _editor.Surface.ClipboardService
            ?? (Application.IsRunning ? Application.Current.PlatformServices.Clipboard : null);
        return clipboard is not null && clipboard.TryGetText(out text);
    }

    /// <summary>
    /// Handler the editor starts with. It stays reachable after <see cref="ActiveInputHandler"/> is
    /// replaced, so a caller can put the ordinary keyboard back.
    /// </summary>
    public TextAreaDefaultInputHandler DefaultInputHandler { get; }

    /// <summary>
    /// The one handler whose bindings answer keys. Assigning detaches the previous handler and
    /// attaches the new one.
    /// </summary>
    public ITextAreaInputHandler? ActiveInputHandler
    {
        get => _activeInputHandler;
        set
        {
            if (value is not null && value.TextArea != this)
            {
                throw new ArgumentException("The handler must belong to this text area.", nameof(value));
            }
            if (ReferenceEquals(_activeInputHandler, value))
            {
                return;
            }
            _activeInputHandler?.Detach();
            _activeInputHandler = value;
            _activeInputHandler?.Attach();
            ActiveInputHandlerChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised after <see cref="ActiveInputHandler"/> changed.</summary>
    public event EventHandler? ActiveInputHandlerChanged;

    /// <summary>Pushed handlers, outermost first. The last one pushed sees a key first.</summary>
    public IReadOnlyList<TextAreaStackedInputHandler> StackedInputHandlers => _stackedInputHandlers;

    /// <summary>Adds a handler on top of the stack and attaches it.</summary>
    public void PushStackedInputHandler(TextAreaStackedInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (handler.TextArea != this)
        {
            throw new ArgumentException("The handler must belong to this text area.", nameof(handler));
        }
        _stackedInputHandlers.Add(handler);
        handler.Attach();
    }

    /// <summary>
    /// Removes the handler and everything pushed after it, detaching in reverse order of pushing.
    /// Does nothing when the handler is not on the stack, so a panel can close twice.
    /// </summary>
    public void PopStackedInputHandler(TextAreaStackedInputHandler handler)
    {
        int index = _stackedInputHandlers.IndexOf(handler);
        if (index < 0)
        {
            return;
        }
        for (int position = _stackedInputHandlers.Count - 1; position >= index; position--)
        {
            var popped = _stackedInputHandlers[position];
            _stackedInputHandlers.RemoveAt(position);
            popped.Detach();
        }
    }

    /// <summary>
    /// Offers a key to the stacked handlers, newest first, and then to the active handler. Runs
    /// before the editing surface acts on the key, which is what lets a handler claim it.
    /// </summary>
    internal void HandleKeyDown(KeyEventArgs e)
    {
        for (int index = _stackedInputHandlers.Count - 1; index >= 0 && !e.Handled; index--)
        {
            _stackedInputHandlers[index].OnPreviewKeyDown(e);
        }
        if (!e.Handled && _activeInputHandler is TextAreaInputHandler handler)
        {
            handler.TryHandleKey(e);
        }
    }

    /// <summary>Offers a key release to the stacked handlers, newest first.</summary>
    internal void HandleKeyUp(KeyEventArgs e)
    {
        for (int index = _stackedInputHandlers.Count - 1; index >= 0 && !e.Handled; index--)
        {
            _stackedInputHandlers[index].OnPreviewKeyUp(e);
        }
    }

    internal TextEditor Editor => _editor;
}

public sealed class Caret(TextArea textArea)
{
    // Room kept between the caret and the edge of the view while scrolling it into sight.
    internal const double MINIMUM_DISTANCE_TO_VIEW_BORDER = 30;

    private Color? _caretBrush;
    private Color? _primaryCaretBrush;
    private Color? _secondaryCaretBrush;
    // An editor no one is typing in draws no caret; taking the focus is what turns it on.
    private bool _isVisible;
    private int _visualColumnOverride = -1;
    private int _visualColumnOverrideOffset = -1;
    // The position last announced through PositionChanged.
    private (TextLocation Location, int VisualColumn) _raisedPosition;
    private bool _hasRaisedPosition;

    public int Offset
    {
        get => textArea.Editor.CaretOffset;
        set
        {
            _visualColumnOverride = -1;
            textArea.Editor.CaretOffset = value;
        }
    }

    public int Line => textArea.Document.GetLocation(Offset).Line;
    public int Column => textArea.Document.GetLocation(Offset).Column;
    public TextLocation Location => textArea.Document.GetLocation(Offset);

    /// <summary>
    /// Where the caret is, including the visual column it lands on. An assigned visual column is
    /// kept while the caret stays on that offset, which is how a caret in virtual space remembers
    /// the column the clamped surface offset cannot carry.
    /// </summary>
    public TextViewPosition Position
    {
        get => new(Location, VisualColumn);
        set
        {
            int offset = textArea.Document.GetOffset(value.Line, value.Column);
            _visualColumnOverride = value.VisualColumn;
            _visualColumnOverrideOffset = value.VisualColumn >= 0 ? offset : -1;
            textArea.Editor.CaretOffset = offset;
        }
    }

    /// <summary>Visual column of the caret, which a projection moves away from the column.</summary>
    public int VisualColumn
    {
        get
        {
            int offset = Offset;
            if (_visualColumnOverride >= 0 && offset == _visualColumnOverrideOffset)
            {
                return _visualColumnOverride;
            }
            var line = textArea.TextView.GetOrConstructVisualLine(textArea.Document.GetLineByOffset(offset));
            return line is null ? Column - 1 : line.GetVisualColumn(offset - line.StartOffset);
        }
    }

    /// <summary>Colour of the caret. Null follows the editor's foreground.</summary>
    public Color? CaretBrush
    {
        get => _caretBrush;
        set
        {
            if (_caretBrush == value) return;
            _caretBrush = value;
            textArea.Editor.Surface.InvalidateLayer(Aprillz.MewUI.Text.TextViewLayerAnchor.Caret);
        }
    }

    /// <summary>
    /// Colour of the caret on the active corner while a rectangle selection has several carets
    /// live. Null falls back to <see cref="CaretBrush"/>, and then to a built-in colour that says
    /// typing goes to more than one line.
    /// </summary>
    public Color? PrimaryCaretBrush
    {
        get => _primaryCaretBrush;
        set
        {
            if (_primaryCaretBrush == value) return;
            _primaryCaretBrush = value;
            textArea.Editor.Surface.InvalidateLayer(Aprillz.MewUI.Text.TextViewLayerAnchor.Caret);
        }
    }

    /// <summary>
    /// Colour of the carets a rectangle selection puts on the lines the caret is not on. Null takes
    /// a built-in colour that tells them apart from the one on the active corner; it does not fall
    /// back to <see cref="CaretBrush"/>, which would erase that difference.
    /// </summary>
    public Color? SecondaryCaretBrush
    {
        get => _secondaryCaretBrush;
        set
        {
            if (_secondaryCaretBrush == value) return;
            _secondaryCaretBrush = value;
            textArea.Editor.Surface.InvalidateLayer(Aprillz.MewUI.Text.TextViewLayerAnchor.Caret);
        }
    }

    /// <summary>Whether the caret is drawn at all, apart from the blink it follows while shown.</summary>
    public bool IsVisible => _isVisible;

    /// <summary>
    /// Draws the caret again after a <see cref="Hide"/>, whether or not the editor holds the
    /// keyboard: a search selecting its match shows where the reader is while the search box has it.
    /// Taking the keyboard away hides it again.
    /// </summary>
    public void Show() => SetVisible(true);

    /// <summary>Stops drawing the caret until <see cref="Show"/>, as losing the keyboard does.</summary>
    public void Hide() => SetVisible(false);

    /// <summary>Scrolls the smallest amount that brings the caret into view.</summary>
    public void BringCaretToView() => textArea.Editor.Surface.ScrollToCaret();

    /// <summary>
    /// The x the caret wants to stay at while moving up and down, or NaN when the next vertical
    /// move should take the x from the caret's current position.
    /// </summary>
    public double DesiredXPos { get; set; } = double.NaN;

    public event EventHandler? PositionChanged;

    internal void RaisePositionChanged()
    {
        // The remembered virtual column belongs to one offset; once the caret leaves it, the
        // column is derived again.
        if (_visualColumnOverride >= 0 && _visualColumnOverrideOffset != Offset)
        {
            _visualColumnOverride = -1;
        }
        // Only a move is announced, as in the original; compared by location, since the visual column lays out the line.
        var position = (Location, _visualColumnOverride);
        if (_hasRaisedPosition && position == _raisedPosition)
        {
            return;
        }
        _raisedPosition = position;
        _hasRaisedPosition = true;
        // The desired x belongs to the walk that set it, as the original's caret offset setter has
        // it. Ordinary caret movement is the editing surface's, so this is where the extension
        // hears about it; a vertical walk assigns the x again after moving, which outlives this.
        DesiredXPos = double.NaN;
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetVisible(bool value)
    {
        if (_isVisible == value) return;
        _isVisible = value;
        textArea.Editor.Surface.InvalidateLayer(Aprillz.MewUI.Text.TextViewLayerAnchor.Caret);
    }
}
