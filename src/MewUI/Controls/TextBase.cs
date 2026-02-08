using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for text input controls.
/// </summary>
public abstract partial class TextBase : Control
{
    private bool _suppressBindingSet;
    private string? _cachedText;
    private int _cachedTextVersion = -1;
    private readonly TextEditorCore _editor;
    private readonly TextViewState _view = new();

    private bool _suppressTextInputNewline;
    private bool _suppressTextInputTab;

    private ContextMenu? _defaultContextMenu;

    /// <summary>
    /// Gets the text document.
    /// </summary>
    private protected TextDocument Document { get; } = new();

    /// <summary>
    /// Gets the document version number for change tracking.
    /// </summary>
    protected int DocumentVersion { get; private set; }

    /// <summary>
    /// Initializes a new instance of the TextBase class.
    /// </summary>
    protected TextBase()
    {
        _editor = new TextEditorCore(
            GetTextLengthCore,
            GetTextCharCore,
            GetTextSubstringCore,
            ApplyInsertForEdit,
            ApplyRemoveForEdit,
            OnEditCommitted);
    }

    /// <summary>
    /// Copies the selected text to the clipboard.
    /// </summary>
    public void Copy()
    {
        CopyToClipboardCore();
    }

    /// <summary>
    /// Cuts the selected text to the clipboard.
    /// </summary>
    public void Cut()
    {
        if (IsReadOnly)
        {
            return;
        }

        CutToClipboardCore();
        EnsureCaretVisibleCore(GetInteractionContentBounds());
        InvalidateVisual();
    }

    /// <summary>
    /// Pastes text from the clipboard.
    /// </summary>
    public void Paste()
    {
        if (IsReadOnly)
        {
            return;
        }

        PasteFromClipboardCore();
        EnsureCaretVisibleCore(GetInteractionContentBounds());
        InvalidateVisual();
    }

    /// <summary>
    /// Selects all text in the control.
    /// </summary>
    public void SelectAll()
    {
        SelectAllCore();
        InvalidateVisual();
    }

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text
    {
        get => GetTextCore();
        set
        {
            var normalized = NormalizeText(value ?? string.Empty);
            var current = GetTextCore();
            if (current == normalized)
            {
                return;
            }

            var old = current;
            SetTextCore(normalized);
            _editor.ResetAfterTextSet();
            InvalidateVisual();

            OnTextChanged(old, normalized);
            TextChanged?.Invoke(GetTextCore());
        }
    }

    /// <summary>
    /// Gets or sets the placeholder text shown when the control is empty.
    /// </summary>
    public string Placeholder
    {
        get;
        set
        {
            var v = value ?? string.Empty;
            if (Set(ref field, v))
            {
                InvalidateVisual();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets whether the text is read-only.
    /// </summary>
    public bool IsReadOnly
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateVisual();
            }
        }
    }

    public bool AcceptTab { get; set; }

    public bool AcceptReturn { get; set; }

    public int CaretPosition
    {
        get => _editor.CaretPosition;
        set
        {
            int old = _editor.CaretPosition;
            _editor.SetCaretPosition(value);
            if (old != _editor.CaretPosition)
            {
                InvalidateVisual();
            }
        }
    }

    public event Action<string>? TextChanged;
    public event Action<bool>? WrapChanged;

    public bool CanUndo => _editor.CanUndo;

    public bool CanRedo => _editor.CanRedo;

    public override bool Focusable => true;

    protected double HorizontalOffset => _view.HorizontalOffset;

    protected double VerticalOffset => _view.VerticalOffset;

    protected void SetHorizontalOffset(double value, bool invalidateVisual = true)
    {
        var dpiScale = GetDpi() / 96.0;
        if (_view.SetHorizontalOffset(value, dpiScale) && invalidateVisual)
        {
            InvalidateVisual();
        }
    }

    protected void SetVerticalOffset(double value, bool invalidateVisual = true)
    {
        var dpiScale = GetDpi() / 96.0;
        if (_view.SetVerticalOffset(value, dpiScale) && invalidateVisual)
        {
            InvalidateVisual();
        }
    }

    protected void SetScrollOffsets(double horizontal, double vertical, bool invalidateVisual = true)
    {
        var dpiScale = GetDpi() / 96.0;
        if (_view.SetScrollOffsets(horizontal, vertical, dpiScale) && invalidateVisual)
        {
            InvalidateVisual();
        }
    }

    protected virtual TextAlignment PlaceholderVerticalAlignment => TextAlignment.Center;

    protected virtual UIElement? HitTestOverride(Point point) => null;

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        var hit = HitTestOverride(point);
        if (hit != null)
        {
            return hit;
        }

        return base.OnHitTest(point);
    }

    protected bool HasSelection => _editor.HasSelection;

    protected (int start, int end) GetSelectionRange()
    {
        return _editor.GetSelectionRange();
    }

    protected virtual string NormalizeText(string text)
    {
        text ??= string.Empty;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        bool needsTabRemoval = !AcceptTab && text.IndexOf('\t') >= 0;

        if (AcceptReturn)
        {
            int firstCr = text.IndexOf('\r');
            if (firstCr < 0 && !needsTabRemoval)
            {
                return text;
            }

            if (firstCr >= 0)
            {
                text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            }
        }
        else
        {
            bool hasCr = text.IndexOf('\r') >= 0;
            bool hasLf = text.IndexOf('\n') >= 0;
            if (!hasCr && !hasLf && !needsTabRemoval)
            {
                return text;
            }

            if (hasCr || hasLf)
            {
                text = text.Replace("\r\n", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            }
        }

        if (needsTabRemoval)
        {
            text = text.Replace("\t", string.Empty);
        }

        return text;
    }

    protected virtual string NormalizePastedText(string text) => text ?? string.Empty;

    protected virtual Rect GetInteractionContentBounds() => Bounds.Deflate(Padding);

    protected Rect GetTextInnerBounds()
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        return bounds.Deflate(new Thickness(borderInset));
    }

    protected virtual Rect AdjustViewportBoundsForScrollbars(Rect innerBounds, Theme theme) => innerBounds;

    protected Rect GetViewportInnerBounds()
    {
        
        var innerBounds = GetTextInnerBounds();
        return AdjustViewportBoundsForScrollbars(innerBounds, Theme);
    }

    protected Rect GetViewportContentBounds()
    {
        var viewportBounds = GetViewportInnerBounds();
        var dpiScale = GetDpi() / 96.0;
        // Viewport/clip rect should not shrink due to edge rounding; snap outward.
        return LayoutRounding.SnapViewportRectToPixels(viewportBounds.Deflate(Padding), dpiScale);
    }

    protected abstract void RenderTextContent(IGraphicsContext context, Rect contentBounds, IFont font, Theme theme, in VisualState state);

    protected virtual void RenderAfterContent(IGraphicsContext context, Theme theme, in VisualState state)
    {
    }

    protected sealed override void OnRender(IGraphicsContext context)
    {
        
        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = Theme.Metrics.ControlCornerRadius;

        var state = GetVisualState();
        var borderColor = PickAccentBorder(Theme, BorderBrush, state, 0.6);

        DrawBackgroundAndBorder(
            context,
            bounds,
            state.IsEnabled ? Background : Theme.Palette.DisabledControlBackground,
            borderColor,
            radius);

        var contentBounds = GetViewportContentBounds();

        context.Save();
        var dpiScale = GetDpi() / 96.0;
        context.SetClip(LayoutRounding.MakeClipRect(contentBounds, dpiScale));

        var font = GetFont();

        if (Document.IsEmpty && !string.IsNullOrEmpty(Placeholder) && !state.IsFocused)
        {
            context.DrawText(Placeholder, contentBounds, font, Theme.Palette.PlaceholderText,
                TextAlignment.Left, PlaceholderVerticalAlignment, TextWrapping.NoWrap);
        }
        else
        {
            RenderTextContent(context, contentBounds, font, Theme, state);
        }

        context.Restore();

        RenderAfterContent(context, Theme, state);
    }

    protected abstract void SetCaretFromPoint(Point point, Rect contentBounds);

    protected virtual void AutoScrollForSelectionDrag(Point point, Rect contentBounds)
    {
    }

    protected virtual void EnsureCaretVisibleCore(Rect contentBounds)
    {
    }

    protected virtual void MoveCaretHorizontalKey(int direction, bool extendSelection, bool word)
        => MoveCaretHorizontal(direction, extendSelection, word);

    protected virtual void MoveCaretVerticalKey(int deltaLines, bool extendSelection)
    {
    }

    protected virtual string GetTextCore()
    {
        if (_cachedTextVersion == DocumentVersion && _cachedText != null)
        {
            return _cachedText;
        }

        _cachedText = Document.GetText();
        _cachedTextVersion = DocumentVersion;
        return _cachedText;
    }

    protected virtual void SetTextCore(string normalizedText)
    {
        BumpDocumentVersion();
        Document.SetText(normalizedText ?? string.Empty);
    }

    protected virtual int GetTextLengthCore() => Document.Length;

    protected virtual char GetTextCharCore(int index) => Document[index];

    protected virtual string GetTextSubstringCore(int start, int length) => Document.GetText(start, length);

    protected virtual void OnTextChanged(string oldText, string newText)
    {
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.ControlKey)
        {
            switch (e.Key)
            {
                case Key.Home:
                    MoveCaretToDocumentEdge(true, e.ShiftKey);
                    e.Handled = true;
                    EnsureCaretVisibleCore(GetInteractionContentBounds());
                    InvalidateVisual();
                    return;

                case Key.End:
                    MoveCaretToDocumentEdge(false, e.ShiftKey);
                    e.Handled = true;
                    EnsureCaretVisibleCore(GetInteractionContentBounds());
                    InvalidateVisual();
                    return;

                case Key.Z:
                    if (!IsReadOnly)
                    {
                        if (e.ShiftKey)
                        {
                            _editor.Redo();
                        }
                        else
                        {
                            _editor.Undo();
                        }

                        EnsureCaretVisibleCore(GetInteractionContentBounds());
                        InvalidateVisual();
                    }

                    e.Handled = true;
                    return;

                case Key.Y:
                    if (!IsReadOnly)
                    {
                        _editor.Redo();
                        EnsureCaretVisibleCore(GetInteractionContentBounds());
                        InvalidateVisual();
                    }

                    e.Handled = true;
                    return;

                case Key.A:
                    SelectAllCore();
                    e.Handled = true;
                    return;

                case Key.C:
                    CopyToClipboardCore();
                    e.Handled = true;
                    return;

                case Key.X:
                    if (!IsReadOnly)
                    {
                        CutToClipboardCore();
                        EnsureCaretVisibleCore(GetInteractionContentBounds());
                        InvalidateVisual();
                    }

                    e.Handled = true;
                    return;

                case Key.V:
                    if (!IsReadOnly)
                    {
                        PasteFromClipboardCore();
                        EnsureCaretVisibleCore(GetInteractionContentBounds());
                        InvalidateVisual();
                    }

                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.Home:
                MoveCaretToLineEdge(true, e.ShiftKey);
                e.Handled = true;
                break;

            case Key.End:
                MoveCaretToLineEdge(false, e.ShiftKey);
                e.Handled = true;
                break;

            case Key.Left:
                MoveCaretHorizontalKey(-1, e.ShiftKey, e.ControlKey);
                e.Handled = true;
                break;

            case Key.Right:
                MoveCaretHorizontalKey(1, e.ShiftKey, e.ControlKey);
                e.Handled = true;
                break;

            case Key.Up:
                MoveCaretVerticalKey(-1, e.ShiftKey);
                e.Handled = true;
                break;

            case Key.Down:
                MoveCaretVerticalKey(1, e.ShiftKey);
                e.Handled = true;
                break;

            case Key.Backspace:
                if (!IsReadOnly)
                {
                    _editor.BackspaceForEdit(e.ControlKey);
                }

                e.Handled = true;
                break;

            case Key.Delete:
                if (!IsReadOnly)
                {
                    _editor.DeleteForEdit(e.ControlKey);
                }

                e.Handled = true;
                break;

            case Key.Tab:
                if (!IsReadOnly && AcceptTab)
                {
                    _editor.InsertTextAtCaretForEdit("\t");
                    _suppressTextInputTab = true;
                    e.Handled = true;
                }

                break;

            case Key.Enter:
                if (!IsReadOnly && AcceptReturn)
                {
                    _editor.InsertTextAtCaretForEdit("\n");
                    _suppressTextInputNewline = true;
                    e.Handled = true;
                }

                break;
        }

        if (e.Handled)
        {
            EnsureCaretVisibleCore(GetInteractionContentBounds());
            InvalidateVisual();
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || IsReadOnly)
        {
            return;
        }

        var text = e.Text ?? string.Empty;

        if (_suppressTextInputNewline)
        {
            _suppressTextInputNewline = false;
            if (text.Contains('\r') || text.Contains('\n'))
            {
                e.Handled = true;
                return;
            }
        }

        if (_suppressTextInputTab)
        {
            _suppressTextInputTab = false;
            if (text.Contains('\t'))
            {
                e.Handled = true;
                return;
            }
        }

        text = NormalizeText(text);
        if (text.Length == 0)
        {
            return;
        }

        InsertTextAtCaretForEdit(text);
        e.Handled = true;

        EnsureCaretVisibleCore(GetInteractionContentBounds());
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Handled)
        {
            return;
        }

        if (!IsEnabled)
        {
            return;
        }

        if (e.Button == MouseButton.Right)
        {
            // If the user assigned a custom context menu, let Control handle it.
            if (ContextMenu != null)
            {
                return;
            }

            ShowDefaultTextContextMenu(e.Position);
            e.Handled = true;
            return;
        }

        if (e.Button != MouseButton.Left)
        {
            return;
        }

        Focus();

        var contentBounds = GetInteractionContentBounds();

        SetCaretFromPoint(e.Position, contentBounds);
        _editor.BeginSelectionAtCaret();

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        EnsureCaretVisibleCore(contentBounds);
        InvalidateVisual();
        e.Handled = true;
    }

    private void ShowDefaultTextContextMenu(Point positionInWindow)
    {
        var menu = _defaultContextMenu ??= new ContextMenu();
        menu.Items.Clear();

        bool canPaste = false;
        if (!IsReadOnly && TryClipboardGetText(out var clip) && !string.IsNullOrEmpty(clip))
        {
            canPaste = true;
        }

        menu.AddItem("Undo", () => Undo(), isEnabled: !IsReadOnly && CanUndo, shortcutText: "Ctrl+Z");
        menu.AddItem("Redo", () => Redo(), isEnabled: !IsReadOnly && CanRedo, shortcutText: "Ctrl+Y");
        menu.AddSeparator();
        menu.AddItem("Cut", () => Cut(), isEnabled: !IsReadOnly && HasSelection, shortcutText: "Ctrl+X");
        menu.AddItem("Copy", () => Copy(), isEnabled: HasSelection, shortcutText: "Ctrl+C");
        menu.AddItem("Paste", () => Paste(), isEnabled: canPaste, shortcutText: "Ctrl+V");
        menu.AddSeparator();
        menu.AddItem("Select All", () => SelectAll(), isEnabled: GetTextLengthCore() > 0, shortcutText: "Ctrl+A");

        menu.ShowAt(this, positionInWindow);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsEnabled || !IsMouseCaptured || !e.LeftButton)
        {
            return;
        }

        var contentBounds = GetInteractionContentBounds();
        AutoScrollForSelectionDrag(e.Position, contentBounds);
        SetCaretFromPoint(e.Position, contentBounds);
        _editor.UpdateSelectionToCaret();
        EnsureCaretVisibleCore(contentBounds);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left)
        {
            return;
        }

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }
    }

    public void SetTextBinding(
        Func<string> get,
        Action<string> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        SetTextBindingCore(get, set, subscribe, unsubscribe);
    }

    protected override void OnDispose()
    {
        Document.Dispose();
        TextChanged -= ForwardTextChangedToBinding;
    }

    protected void NotifyTextChanged()
    {
        TextChanged?.Invoke(GetTextCore());
    }

    protected void NotifyWrapChanged(bool value)
    {
        WrapChanged?.Invoke(value);
    }

    private void ForwardTextChangedToBinding(string text)
    {
        if (_suppressBindingSet)
        {
            return;
        }

        if (TryGetBinding(TextBindingSlot, out ValueBinding<string> textBinding))
        {
            textBinding.Set(text);
        }
    }

    public void Undo()
    {
        if (IsReadOnly)
        {
            return;
        }

        _editor.Undo();
    }

    public void Redo()
    {
        if (IsReadOnly)
        {
            return;
        }

        _editor.Redo();
    }

    protected void BumpDocumentVersion()
    {
        DocumentVersion++;
        _cachedTextVersion = -1;
        _cachedText = null;
    }

    protected void InsertIntoDocument(int index, ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
        {
            return;
        }

        BumpDocumentVersion();
        Document.Insert(index, text);
    }

    protected void RemoveFromDocument(int index, int length)
    {
        if (length <= 0)
        {
            return;
        }

        BumpDocumentVersion();
        Document.Remove(index, length);
    }

    protected int ApplyInsertCore(int index, ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
        {
            return index;
        }

        int max = GetTextLengthCore();
        index = Math.Clamp(index, 0, max);
        InsertIntoDocument(index, text);
        return index;
    }

    protected int ApplyRemoveCore(int index, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        int max = GetTextLengthCore();
        index = Math.Clamp(index, 0, max);
        length = Math.Min(length, max - index);
        if (length <= 0)
        {
            return 0;
        }

        RemoveFromDocument(index, length);
        return length;
    }

    protected virtual void SelectAllCore()
    {
        _editor.SelectAll();
        InvalidateVisual();
    }

    protected virtual void CopyToClipboardCore()
    {
        if (!HasSelection)
        {
            return;
        }

        var (start, end) = GetSelectionRange();
        string selected = GetTextSubstringCore(start, end - start);
        TryClipboardSetText(selected);
    }

    protected virtual void CutToClipboardCore()
    {
        if (!HasSelection)
        {
            return;
        }

        CopyToClipboardCore();
        DeleteSelectionForEdit();
    }

    protected virtual void PasteFromClipboardCore()
    {
        if (!TryClipboardGetText(out var text) || string.IsNullOrEmpty(text))
        {
            return;
        }

        InsertTextAtCaretForEdit(NormalizePastedText(text));
    }

    protected virtual void ApplyInsertForEdit(int index, string text) => InsertIntoDocument(index, text.AsSpan());

    protected virtual void ApplyRemoveForEdit(int index, int length) => RemoveFromDocument(index, length);

    protected virtual void OnEditCommitted() => NotifyTextChanged();

    protected void SetCaretAndSelection(int newPos, bool extendSelection)
    {
        _editor.SetCaretAndSelection(newPos, extendSelection);
    }

    protected void MoveCaretHorizontal(int direction, bool extendSelection, bool word)
    {
        _editor.MoveCaretHorizontal(direction, extendSelection, word);
    }

    protected void MoveCaretToDocumentEdge(bool start, bool extendSelection)
    {
        _editor.MoveCaretToDocumentEdge(start, extendSelection);
    }

    protected virtual void MoveCaretToLineEdge(bool start, bool extendSelection)
        => MoveCaretToDocumentEdge(start, extendSelection);

    protected void BackspaceForEdit(bool word)
    {
        _editor.BackspaceForEdit(word);
    }

    protected void DeleteForEdit(bool word)
    {
        _editor.DeleteForEdit(word);
    }

    protected int FindPreviousWordBoundary(int from)
    {
        return _editor.FindPreviousWordBoundary(from);
    }

    protected int FindNextWordBoundary(int from)
    {
        return _editor.FindNextWordBoundary(from);
    }

    protected virtual bool DeleteSelectionForEdit()
    {
        return _editor.DeleteSelectionForEdit();
    }

    protected virtual void InsertTextAtCaretForEdit(string text)
    {
        text = NormalizeText(text ?? string.Empty);
        if (text.Length == 0)
        {
            return;
        }

        _editor.InsertTextAtCaretForEdit(text);
    }

    protected bool TryClipboardSetText(string text)
    {
        if (!Application.IsRunning)
        {
            return false;
        }

        var clipboard = Application.Current.PlatformHost.Clipboard;
        return clipboard.TrySetText(text ?? string.Empty);
    }

    protected bool TryClipboardGetText(out string text)
    {
        text = string.Empty;
        if (!Application.IsRunning)
        {
            return false;
        }

        var clipboard = Application.Current.PlatformHost.Clipboard;
        return clipboard.TryGetText(out text);
    }

    protected virtual bool SupportsWrap => false;

    protected bool WrapEnabled
    {
        get;
        private set
        {
            if (field == value)
            {
                return;
            }

            var old = field;
            field = value;
            OnWrapChanged(old, value);
            WrapChanged?.Invoke(value);
        }
    }

    protected void SetWrapEnabled(bool value)
    {
        if (!SupportsWrap)
        {
            WrapEnabled = false;
            return;
        }

        WrapEnabled = value;
    }

    protected virtual void OnWrapChanged(bool oldValue, bool newValue)
    {
    }

    protected static double ClampOffset(double value, double extent, double viewport)
    {
        extent = Math.Max(0, extent);
        viewport = Math.Max(0, viewport);
        double max = Math.Max(0, extent - viewport);
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        if (value < 0)
        {
            return 0;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    protected static double ClampOffset(double value, double extent, double viewport, double dpiScale)
    {
        double clamped = ClampOffset(value, extent, viewport);

        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            return clamped;
        }

        clamped = LayoutRounding.RoundToPixel(clamped, dpiScale);
        return ClampOffset(clamped, extent, viewport);
    }

    protected void ClearUndoRedo() => _editor.ClearUndoRedo();
}
