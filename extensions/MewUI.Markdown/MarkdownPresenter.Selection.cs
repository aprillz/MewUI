using System.Text;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Markdown;

/// <summary>Document-level text selection: one range across blocks, addressed by text unit and character offset.</summary>
public partial class MarkdownPresenter
{
    /// <summary>Identifies the selection enable property.</summary>
    public static readonly MewProperty<bool> IsSelectionEnabledProperty = MewProperty<bool>.Register<MarkdownPresenter>(
        nameof(IsSelectionEnabled), true, changed: static (self, _, enabled) => self.OnSelectionEnabledChanged(enabled));
    private static readonly MewPropertyKey<bool> HasSelectionPropertyKey =
        MewProperty<bool>.RegisterReadOnly<MarkdownPresenter>(nameof(HasSelection), false);
    /// <summary>Identifies the read-only selection presence property.</summary>
    public static readonly MewProperty<bool> HasSelectionProperty = HasSelectionPropertyKey.Property;
    private static readonly MewPropertyKey<string> SelectedTextPropertyKey =
        MewProperty<string>.RegisterReadOnly<MarkdownPresenter>(nameof(SelectedText), string.Empty);
    /// <summary>Identifies the read-only selected text property.</summary>
    public static readonly MewProperty<string> SelectedTextProperty = SelectedTextPropertyKey.Property;

    private (int Unit, int Offset)? _selectionAnchor;
    private (int Unit, int Offset)? _selectionFocus;
    private bool _dragSelecting;
    private ContextMenu? _defaultContextMenu;

    /// <summary>Gets or sets whether text can be selected with the mouse and keyboard.</summary>
    public bool IsSelectionEnabled { get => GetValue(IsSelectionEnabledProperty); set => SetValue(IsSelectionEnabledProperty, value); }
    /// <summary>Gets whether a non-empty selection exists.</summary>
    public bool HasSelection => GetValue(HasSelectionProperty);
    /// <summary>Gets the selected text as plain text; blocks and list items are separated by line breaks, table cells by tabs, and a list item that starts inside the selection is preceded by its marker and a tab.</summary>
    public string SelectedText => GetValue(SelectedTextProperty);
    /// <summary>Raised when the selection changes, including when it is cleared.</summary>
    public event Action? SelectionChanged;
    /// <summary>Raised before the selected text is written to the clipboard; a handler may replace the text or cancel the copy.</summary>
    public event Action<MarkdownCopyingEventArgs>? Copying;

    /// <summary>Selects the whole document.</summary>
    public void SelectAll()
    {
        if (!IsSelectionEnabled || _document == null || _document.TextUnits.Count == 0)
        {
            return;
        }
        int last = _document.TextUnits.Count - 1;
        SetSelection((0, 0), (last, _document.TextUnits[last].Text.Length));
    }

    /// <summary>Clears the selection.</summary>
    public void ClearSelection() => SetSelection(null, null);

    /// <summary>Copies the selected text to the platform clipboard; returns false when nothing is selected, a <see cref="Copying"/> handler cancels, or no clipboard is available.</summary>
    public bool CopySelection()
    {
        string text = SelectedText;
        if (text.Length == 0)
        {
            return false;
        }
        var args = new MarkdownCopyingEventArgs(text);
        Copying?.Invoke(args);
        if (args.Cancel || string.IsNullOrEmpty(args.Text) || !Application.IsRunning)
        {
            return false;
        }
        return Application.Current?.PlatformServices.Clipboard?.TrySetText(args.Text) == true;
    }

    private void OnSelectionEnabledChanged(bool enabled)
    {
        Focusable = enabled;
        Cursor = enabled ? CursorType.IBeam : null;
        if (!enabled)
        {
            ClearSelection();
        }
    }

    private void InitializeSelection()
    {
        Focusable = true;
        Cursor = CursorType.IBeam;
        // Commands rather than key handling, so the platform shortcut, menus and toolbars share one path.
        Commands.Register(StandardCommands.Copy, this,
            static presenter => presenter.CopySelection(),
            static presenter => presenter.IsSelectionEnabled && presenter.HasSelection);
        Commands.Register(StandardCommands.SelectAll, this,
            static presenter => presenter.SelectAll(),
            static presenter => presenter.IsSelectionEnabled && presenter._document != null && presenter._document.TextUnits.Count > 0);
    }

    /// <summary>Starts a selection at a window point; exposed for tests that bypass input routing.</summary>
    internal bool BeginSelectionAt(Point windowPoint, int clickCount = 1)
    {
        var hit = ResolveSelectionPoint(windowPoint);
        if (hit == null)
        {
            return false;
        }
        var (unit, offset, element) = hit.Value;
        if (clickCount >= 3)
        {
            SetSelection((unit, 0), (unit, element.TextLength));
        }
        else if (clickCount == 2)
        {
            var (start, end) = element.WordAt(offset);
            SetSelection((unit, start), (unit, end));
        }
        else
        {
            SetSelection((unit, offset), (unit, offset));
        }
        return true;
    }

    /// <summary>Moves the selection focus to a window point, keeping the anchor.</summary>
    internal void ExtendSelectionTo(Point windowPoint)
    {
        if (_selectionAnchor == null)
        {
            return;
        }
        var hit = ResolveSelectionPoint(windowPoint);
        if (hit != null)
        {
            SetSelection(_selectionAnchor, (hit.Value.Unit, hit.Value.Offset));
        }
    }

    private void SetSelection((int Unit, int Offset)? anchor, (int Unit, int Offset)? focus)
    {
        if (anchor == _selectionAnchor && focus == _selectionFocus)
        {
            return;
        }
        _selectionAnchor = anchor;
        _selectionFocus = focus;
        ApplySelectionToRealized();
        string text = BuildSelectedText();
        SetValue(SelectedTextPropertyKey, text);
        SetValue(HasSelectionPropertyKey, text.Length > 0);
        SelectionChanged?.Invoke();
    }

    private void ClearSelectionState()
    {
        _dragSelecting = false;
        SetSelection(null, null);
    }

    // Ordered range, or null when the selection is empty.
    private ((int Unit, int Offset) Start, (int Unit, int Offset) End)? OrderedSelection()
    {
        if (_selectionAnchor is not (int, int) anchor || _selectionFocus is not (int, int) focus || anchor == focus)
        {
            return null;
        }
        return anchor.CompareTo(focus) <= 0 ? (anchor, focus) : (focus, anchor);
    }

    private string BuildSelectedText()
    {
        var range = OrderedSelection();
        if (_document == null || range == null)
        {
            return string.Empty;
        }
        var (start, end) = range.Value;
        var units = _document.TextUnits;
        var builder = new StringBuilder();
        for (int unit = start.Unit; unit <= end.Unit && unit < units.Count; unit++)
        {
            string text = units[unit].Text;
            int from = unit == start.Unit ? Math.Clamp(start.Offset, 0, text.Length) : 0;
            int to = unit == end.Unit ? Math.Clamp(end.Offset, 0, text.Length) : text.Length;
            if (unit > start.Unit)
            {
                builder.Append(units[unit].Separator);
            }
            // The marker belongs to the item's start, so a selection beginning inside the item text leaves it out.
            if (from == 0)
            {
                builder.Append(units[unit].ListMarkerPrefix);
            }
            builder.Append(text, from, Math.Max(0, to - from));
        }
        return builder.ToString();
    }

    /// <summary>Pushes the current range into one realized element; called when the element is created.</summary>
    internal void ApplySelectionTo(ISelectableText element)
    {
        if (element.TextUnit < 0)
        {
            return;
        }
        var range = OrderedSelection();
        if (range == null)
        {
            element.SetSelection(0, 0);
            return;
        }
        var (start, end) = range.Value;
        int unit = element.TextUnit;
        if (unit < start.Unit || unit > end.Unit)
        {
            element.SetSelection(0, 0);
        }
        else
        {
            int from = unit == start.Unit ? start.Offset : 0;
            int to = unit == end.Unit ? end.Offset : element.TextLength;
            element.SetSelection(from, to);
        }
    }

    private void ApplySelectionToRealized()
    {
        foreach (var element in RealizedSelectableElements())
        {
            ApplySelectionTo(element);
        }
    }

    private IEnumerable<ISelectableText> RealizedSelectableElements()
    {
        var pending = new Stack<Element>();
        if (DocumentRoot is Element root)
        {
            pending.Push(root);
        }
        var children = new List<Element>();
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is ISelectableText selectable && selectable.TextUnit >= 0)
            {
                yield return selectable;
            }
            if (current is IVisualTreeHost host)
            {
                children.Clear();
                host.VisitChildren(child => { children.Add(child); return true; });
                for (int index = children.Count - 1; index >= 0; index--)
                {
                    pending.Push(children[index]);
                }
            }
        }
    }

    // The unit under the point, or the vertically nearest realized unit when the point is between blocks.
    private (int Unit, int Offset, ISelectableText Element)? ResolveSelectionPoint(Point windowPoint)
    {
        ISelectableText? best = null;
        double bestDistance = double.PositiveInfinity;
        foreach (var element in RealizedSelectableElements())
        {
            var bounds = element.Bounds;
            double distance = windowPoint.Y < bounds.Y ? bounds.Y - windowPoint.Y
                : windowPoint.Y > bounds.Bottom ? windowPoint.Y - bounds.Bottom : 0;
            if (distance < bestDistance)
            {
                best = element;
                bestDistance = distance;
                if (distance == 0)
                {
                    break;
                }
            }
        }
        if (best == null)
        {
            return null;
        }
        return (best.TextUnit, best.OffsetAt(windowPoint), best);
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Handled || !IsSelectionEnabled || !IsEffectivelyEnabled)
        {
            return;
        }
        if (args.Button == MouseButton.Right)
        {
            ShowDefaultContextMenu(args);
            return;
        }
        if (args.Button != MouseButton.Left)
        {
            return;
        }
        if (args.ClickCount == 1 && args.Modifiers.HasFlag(ModifierKeys.Shift) && _selectionAnchor != null)
        {
            ExtendSelectionTo(WindowPoint(args));
        }
        else if (!BeginSelectionAt(WindowPoint(args), args.ClickCount))
        {
            return;
        }
        Focus();
        _dragSelecting = args.ClickCount == 1;
        if (_dragSelecting && FindVisualRoot() is Window window)
        {
            window.CaptureMouse(this);
        }
        args.Handled = true;
    }

    /// <summary>Shows the Copy and Select All menu unless the host assigned its own context menu.</summary>
    private void ShowDefaultContextMenu(MouseEventArgs args)
    {
        if (ContextMenu != null)
        {
            return;
        }
        // Focus first so the menu's commands resolve to this presenter's selection.
        Focus();
        var menu = _defaultContextMenu ??= new ContextMenu();
        menu.Items.Clear();
        menu.AddItem(StandardCommands.Copy);
        menu.AddSeparator();
        menu.AddItem(StandardCommands.SelectAll);
        menu.Show(this, WindowPoint(args));
        args.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (!_dragSelecting || !args.LeftButton)
        {
            return;
        }
        AutoScrollTowards(WindowPoint(args));
        ExtendSelectionTo(WindowPoint(args));
        args.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        if (args.Button == MouseButton.Left && _dragSelecting)
        {
            _dragSelecting = false;
            if (FindVisualRoot() is Window window)
            {
                window.ReleaseMouseCapture();
            }
        }
    }

    private Point WindowPoint(MouseEventArgs args)
    {
        var local = args.GetPosition(this);
        return new Point(Bounds.X + local.X, Bounds.Y + local.Y);
    }

    // Dragging past the viewport edge scrolls by the overshoot, as the core text boxes do, so a
    // pointer far outside moves the view faster than one just past the edge.
    private void AutoScrollTowards(Point windowPoint)
    {
        if (_scroll == null)
        {
            return;
        }
        double top = _scroll.Bounds.Y;
        double bottom = top + _scroll.Bounds.Height;
        if (windowPoint.Y < top)
        {
            _scroll.SetScrollOffsets(_scroll.HorizontalOffset, _scroll.VerticalOffset + windowPoint.Y - top);
        }
        else if (windowPoint.Y > bottom)
        {
            _scroll.SetScrollOffsets(_scroll.HorizontalOffset, _scroll.VerticalOffset + windowPoint.Y - bottom);
        }
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (args.Handled)
        {
            return;
        }
        if (args.Key == Key.Tab && !args.ControlKey && TryMoveLinkFocus(forward: !args.ShiftKey))
        {
            args.Handled = true;
            return;
        }
        // Copy and select all arrive through the input map as commands; only Escape is handled here.
        if (IsSelectionEnabled && args.Key == Key.Escape && HasSelection)
        {
            ClearSelection();
            args.Handled = true;
        }
    }
}
