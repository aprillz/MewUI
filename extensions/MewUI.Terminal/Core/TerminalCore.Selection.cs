// Ported from Windows Terminal src/cascadia/TerminalCore/TerminalSelection.cpp and terminalrenderdata.cpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Input;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Core;

internal enum SelectionInteractionMode
{
    None,
    Mouse,
    Keyboard,
    Mark,
}

internal enum SelectionDirection
{
    Left,
    Right,
    Up,
    Down,
}

internal enum SearchDirection
{
    Forward,
    Backward,
}

internal enum SelectionExpansion
{
    Char,
    Word,

    // Mouse selection only.
    Line,
    Viewport,
    Buffer,
}

[Flags]
internal enum SelectionEndpoint
{
    None = 0,
    Start = 1 << 0,
    End = 1 << 1,
}

/// <summary>
/// The selection. The pivot is the cell a selection was started on (a whole word or line for multi-click selections); it
/// stays selected while the other end is dragged across it.
/// </summary>
internal sealed partial class TerminalCore
{
    /// <summary>A selection between two buffer cells; the pivot is the end that stays put while dragging.</summary>
    private struct SelectionInfo
    {
        public TilPoint Start;
        public TilPoint End;
        public TilPoint Pivot;
        public bool BlockSelection;
        public bool Active;
    }

    private SelectionInfo _selection;

    // Counts changes to the selection, so the cached selection spans know when to recompute.
    private int _selectionGeneration;

    private List<TilPointSpan> _lastSelectionSpans = new();
    private int _lastSelectionGeneration = -1;
    private SelectionExpansion _multiClickSelectionMode = SelectionExpansion.Char;
    private SelectionInteractionMode _selectionMode = SelectionInteractionMode.None;
    private SelectionEndpoint _selectionEndpoint = SelectionEndpoint.None;
    private bool _anchorInactiveSelectionEndpoint;

    private List<TilPointSpan> _searchHighlights = new();
    private int _searchHighlightFocused;

    /// <summary>The selection, for changing; every write counts as a change.</summary>
    private ref SelectionInfo WriteSelection()
    {
        _selectionGeneration++;
        return ref _selection;
    }

    public bool IsSelectionActive
    {
        get
        {
            AssertLocked();
            return _selection.Active;
        }
    }

    public bool IsBlockSelection
    {
        get
        {
            AssertLocked();
            return _selection.BlockSelection;
        }
    }

    public TilPoint GetSelectionAnchor()
    {
        AssertLocked();
        return _selection.Start;
    }

    public TilPoint GetSelectionEnd()
    {
        AssertLocked();
        return _selection.End;
    }

    public SelectionInteractionMode SelectionMode => _selectionMode;

    public SelectionEndpoint SelectionEndpointTarget => _selectionEndpoint;

    /// <summary>The selected cells as spans in reading order, recomputed only after the selection changed.</summary>
    public List<TilPointSpan> GetSelectionSpans()
    {
        if (_selectionGeneration != _lastSelectionGeneration)
        {
            _lastSelectionSpans = ComputeSelectionSpans();
            _lastSelectionGeneration = _selectionGeneration;
        }

        return _lastSelectionSpans;
    }

    private List<TilPointSpan> ComputeSelectionSpans()
    {
        if (!IsSelectionActive)
        {
            return new List<TilPointSpan>();
        }

        return ActiveBuffer.GetTextSpans(_selection.Start, _selection.End, _selection.BlockSelection, false);
    }

    /// <summary>The viewport cell where the start marker of a keyboard selection is drawn: the cell before the start.</summary>
    public TilPoint SelectionStartForRendering()
    {
        var pos = _selection.Start;
        var buffer = GetTextBuffer();
        var bufferSize = buffer.Size;
        if (bufferSize.IsInBounds(pos) && buffer.GetRowByOffset(pos.Y).DbcsAttrAt(pos.X) == DbcsAttribute.Trailing)
        {
            // On the trailing half of a wide glyph, move off it so the glyph is inside the marker.
            bufferSize.DecrementInExclusiveBounds(ref pos);
        }

        // The marker is drawn on the left edge of the start, which is the cell before it.
        if (pos.X != bufferSize.Left)
        {
            bufferSize.DecrementInExclusiveBounds(ref pos);
        }

        return pos with { Y = pos.Y - VisibleStartIndex };
    }

    /// <summary>The viewport cell where the end marker of a keyboard selection is drawn.</summary>
    public TilPoint SelectionEndForRendering()
    {
        var pos = _selection.End;
        var buffer = GetTextBuffer();
        var bufferSize = buffer.Size;
        if (bufferSize.IsInBounds(pos) && buffer.GetRowByOffset(pos.Y).DbcsAttrAt(pos.X) == DbcsAttribute.Trailing)
        {
            bufferSize.IncrementInExclusiveBounds(ref pos);
        }

        // An end past the right edge is drawn on the last column.
        if (pos.X == bufferSize.RightExclusive)
        {
            bufferSize.DecrementInExclusiveBounds(ref pos);
        }

        return pos with { Y = pos.Y - VisibleStartIndex };
    }

    /// <summary>Starts a word or line selection at a viewport cell (double or triple click).</summary>
    public void MultiClickSelection(TilPoint viewportPos, SelectionExpansion expansionMode)
    {
        ref var selection = ref WriteSelection();
        selection.Pivot = ConvertToBufferCell(viewportPos, true);
        selection.Active = true;

        _multiClickSelectionMode = expansionMode;
        SetSelectionEnd(ref selection, viewportPos);

        // The expanded start becomes the pivot, so the whole word or line stays selected.
        selection.Pivot = selection.Start;
    }

    /// <summary>Starts a character selection at a viewport cell.</summary>
    public void SetSelectionAnchor(TilPoint viewportPos)
    {
        AssertLocked();
        ref var selection = ref WriteSelection();
        selection.Pivot = ConvertToBufferCell(viewportPos, true);
        selection.Active = true;

        _multiClickSelectionMode = SelectionExpansion.Char;
        SetSelectionEnd(ref selection, viewportPos);

        selection.Start = selection.Pivot;
    }

    /// <summary>Moves the end of the selection to a viewport cell, expanding by the selection mode.</summary>
    public void SetSelectionEnd(TilPoint viewportPos, SelectionExpansion? newExpansionMode = null)
    {
        ref var selection = ref WriteSelection();
        SetSelectionEnd(ref selection, viewportPos, newExpansionMode);
    }

    private void SetSelectionEnd(ref SelectionInfo selection, TilPoint viewportPos, SelectionExpansion? newExpansionMode = null)
    {
        if (!selection.Active)
        {
            return;
        }

        var textBufferPos = ConvertToBufferCell(viewportPos, true);

        // A shift-click character selection includes the clicked cell when it is after the pivot.
        if (newExpansionMode == SelectionExpansion.Char && textBufferPos >= selection.Pivot)
        {
            ActiveBuffer.Size.IncrementInExclusiveBounds(ref textBufferPos);
        }

        _multiClickSelectionMode = newExpansionMode ?? _multiClickSelectionMode;

        var (anchorStart, anchorEnd, targetStart) = PivotSelection(selection.Pivot, textBufferPos);
        var (expandedStart, expandedEnd) = ExpandSelectionAnchors(anchorStart, anchorEnd, selection.Pivot);

        if (newExpansionMode.HasValue)
        {
            // A shift-click only expands the end that moved.
            if (targetStart)
            {
                selection.Start = expandedStart;
                selection.End = selection.Pivot;
            }
            else
            {
                selection.End = expandedEnd;
                selection.Start = selection.Pivot;
            }
        }
        else
        {
            selection.Start = expandedStart;
            selection.End = expandedEnd;
        }

        _selectionMode = SelectionInteractionMode.Mouse;
    }

    /// <summary>Orders the pivot and a target; <c>TargetStart</c> tells whether the target became the start.</summary>
    private static (TilPoint Start, TilPoint End, bool TargetStart) PivotSelection(TilPoint pivot, TilPoint targetPos)
    {
        if (targetPos <= pivot)
        {
            return (targetPos, pivot, true);
        }
        else
        {
            return (pivot, targetPos, false);
        }
    }

    /// <summary>Expands selection ends to whole words or lines, by the multi-click mode.</summary>
    private (TilPoint Start, TilPoint End) ExpandSelectionAnchors(TilPoint start, TilPoint end, TilPoint pivot)
    {
        var buffer = ActiveBuffer;
        var bufferSize = buffer.Size;
        int height = bufferSize.Height;

        switch (_multiClickSelectionMode)
        {
            case SelectionExpansion.Line:
                // A line is all the rows it wrapped over.
                while (start.Y > 0 && buffer.GetRowByOffset(start.Y - 1).WasWrapForced)
                {
                    start = start with { Y = start.Y - 1 };
                }

                while (end.Y + 1 < height && buffer.GetRowByOffset(end.Y).WasWrapForced)
                {
                    end = end with { Y = end.Y + 1 };
                }

                start = new TilPoint(bufferSize.Left, start.Y);
                end = new TilPoint(bufferSize.RightExclusive, end.Y);
                break;
            case SelectionExpansion.Word:
                start = buffer.GetWordStart(start, _wordDelimiters, false);

                // An end past the pivot is exclusive; the word that ends there is the one before it.
                if (end > pivot)
                {
                    bufferSize.DecrementInExclusiveBounds(ref end);
                }

                end = buffer.GetWordEnd(end, _wordDelimiters, false);
                break;
        }

        return (start, end);
    }

    public void SetBlockSelection(bool isEnabled) => WriteSelection().BlockSelection = isEnabled;

    /// <summary>Enters mark mode (keyboard selection) at the selection, the focused search result or the cursor; leaves it when in it.</summary>
    public void ToggleMarkMode()
    {
        if (_selectionMode == SelectionInteractionMode.Mark)
        {
            ClearSelection();
            return;
        }

        if (IsSelectionActive)
        {
            // An existing selection is kept, and arrow keys move its end.
            if ((_selectionEndpoint & (SelectionEndpoint.Start | SelectionEndpoint.End)) == 0)
            {
                _selectionEndpoint |= SelectionEndpoint.End;
            }
        }
        else if (GetSearchHighlightFocused() is TilPointSpan focusedSearchResult)
        {
            WriteSelection() = new SelectionInfo
            {
                Start = focusedSearchResult.Start,
                End = focusedSearchResult.End,
                Pivot = focusedSearchResult.Start,
                BlockSelection = false,
                Active = true,
            };
            _selectionEndpoint |= SelectionEndpoint.End;
        }
        else
        {
            // A scrolled view starts at its top left, since the cursor is off screen.
            var cursorPos = _scrollOffset != 0 ? GetVisibleViewport().Origin : ActiveBuffer.Cursor.Position;
            WriteSelection() = new SelectionInfo
            {
                Start = cursorPos,
                End = cursorPos,
                Pivot = cursorPos,
                BlockSelection = false,
                Active = true,
            };
            _selectionEndpoint |= SelectionEndpoint.Start | SelectionEndpoint.End;
        }

        ScrollToPoint(_selection.Start);
        _selectionMode = SelectionInteractionMode.Mark;
    }

    /// <summary>Switches which end of the selection the keyboard moves.</summary>
    public void SwitchSelectionEndpoint()
    {
        if (!IsSelectionActive)
        {
            return;
        }

        bool start = (_selectionEndpoint & SelectionEndpoint.Start) != 0;
        bool end = (_selectionEndpoint & SelectionEndpoint.End) != 0;
        if (start && end)
        {
            // Both ends moving together: the end moves alone from now on and the start stays.
            _selectionEndpoint = SelectionEndpoint.End;
            _anchorInactiveSelectionEndpoint = true;
        }
        else if (end)
        {
            _selectionEndpoint = SelectionEndpoint.Start;
            WriteSelection().Pivot = _selection.End;
        }
        else if (start)
        {
            _selectionEndpoint = SelectionEndpoint.End;
            WriteSelection().Pivot = _selection.Start;
        }
    }

    public void ExpandSelectionToWord()
    {
        if (!IsSelectionActive)
        {
            return;
        }

        var buffer = ActiveBuffer;
        ref var selection = ref WriteSelection();
        selection.Start = buffer.GetWordStart(selection.Start, _wordDelimiters, false);
        selection.Pivot = selection.Start;
        selection.End = buffer.GetWordEnd(selection.End, _wordDelimiters, false);

        // A collapsed selection in mark mode starts moving its end.
        if ((_selectionEndpoint & SelectionEndpoint.Start) != 0 && (_selectionEndpoint & SelectionEndpoint.End) != 0)
        {
            _selectionEndpoint = SelectionEndpoint.End;
        }
    }

    /// <summary>In mark mode, selects the next or previous hyperlink: a detected URL or an OSC 8 link.</summary>
    public void SelectHyperlink(SearchDirection direction)
    {
        if (_selectionMode != SelectionInteractionMode.Mark)
        {
            return;
        }

        var buffer = ActiveBuffer;
        var bufferSize = buffer.Size;
        int viewportHeight = GetMutableViewport().Height;

        (TilPoint Start, TilPoint End)? ExtractResultFromList(List<(TilPoint Start, TilPoint Stop, int Value)> list)
        {
            var selectionStart = _selection.Start;
            var selectionEnd = _selection.End;
            if (list.Count == 0)
            {
                return null;
            }

            if (direction == SearchDirection.Forward)
            {
                // The link that is already selected is skipped.
                var range = list[0];
                if (range.Start == selectionStart && range.Stop == selectionEnd)
                {
                    if (list.Count > 1)
                    {
                        return (list[1].Start, list[1].Stop);
                    }

                    return null;
                }

                return (range.Start, range.Stop);
            }
            else
            {
                var range = list[^1];
                return (range.Start, range.Stop);
            }
        }

        // First search the detected URLs of the visible rows, then a screen at a time beyond them.
        TilPoint searchStart;
        TilPoint searchEnd;
        if (direction == SearchDirection.Forward)
        {
            searchStart = _selection.Start;
            searchEnd = new TilPoint(bufferSize.RightInclusive, VisibleEndIndex);
        }
        else
        {
            searchStart = new TilPoint(bufferSize.Left, VisibleStartIndex);
            searchEnd = _selection.Start;
        }

        var result = ExtractResultFromList(FindContained(_patterns, searchStart, searchEnd));
        if (!result.HasValue)
        {
            if (direction == SearchDirection.Forward)
            {
                searchStart = new TilPoint(bufferSize.Left, searchEnd.Y + 1);
                searchEnd = new TilPoint(bufferSize.RightInclusive, Math.Min(searchStart.Y + viewportHeight, ViewEndIndex));
            }
            else
            {
                searchEnd = new TilPoint(bufferSize.RightInclusive, searchStart.Y - 1);
                searchStart = new TilPoint(bufferSize.Left, Math.Max(searchStart.Y - viewportHeight, bufferSize.Top));
            }

            var bufferStart = bufferSize.Origin;
            var bufferEnd = new TilPoint(bufferSize.RightInclusive, ViewEndIndex);
            while (!result.HasValue && bufferSize.IsInBounds(searchStart) && bufferSize.IsInBounds(searchEnd)
                && searchStart <= searchEnd && bufferStart <= searchStart && searchEnd <= bufferEnd)
            {
                var patterns = GetPatterns(searchStart.Y, searchEnd.Y);
                result = ExtractResultFromList(FindContained(patterns, searchStart, searchEnd));

                if (!result.HasValue)
                {
                    if (direction == SearchDirection.Forward)
                    {
                        searchStart = searchStart with { Y = searchStart.Y + 1 };
                        searchEnd = searchEnd with { Y = Math.Min(searchStart.Y + viewportHeight, ViewEndIndex) };
                    }
                    else
                    {
                        searchEnd = searchEnd with { Y = searchEnd.Y - 1 };
                        searchStart = searchStart with { Y = Math.Max(searchEnd.Y - viewportHeight, bufferSize.Top) };
                    }
                }
            }
        }

        // An OSC 8 link between the selection and the found URL comes first.
        if (direction == SearchDirection.Forward)
        {
            searchStart = _selection.Start;
            searchEnd = result.HasValue ? result.Value.Start : buffer.GetLastNonSpaceCharacter();
        }
        else
        {
            searchStart = result.HasValue ? result.Value.End : bufferSize.Origin;
            searchEnd = _selection.Start;
        }

        int step = direction == SearchDirection.Forward ? 1 : -1;
        var iterPos = bufferSize.Clamp(direction == SearchDirection.Forward ? searchStart : searchEnd);
        while (direction == SearchDirection.Forward ? iterPos < searchEnd : iterPos > searchStart)
        {
            if (iterPos < _selection.Start || iterPos > _selection.End)
            {
                var attr = AttributeAt(buffer, iterPos);
                if (attr.IsHyperlink())
                {
                    ushort hyperlinkId = attr.HyperlinkId;

                    var linkStart = iterPos;
                    while (linkStart > searchStart && attr.IsHyperlink() && attr.HyperlinkId == hyperlinkId)
                    {
                        bufferSize.WalkInBounds(ref linkStart, -1);
                        attr = AttributeAt(buffer, linkStart);
                    }

                    // The walk stopped one cell before the link, unless it reached the buffer start.
                    if (linkStart != bufferSize.Origin)
                    {
                        bufferSize.WalkInBounds(ref linkStart, 1);
                    }

                    var linkEnd = iterPos;
                    attr = AttributeAt(buffer, linkEnd);
                    while (linkEnd < searchEnd && attr.IsHyperlink() && attr.HyperlinkId == hyperlinkId)
                    {
                        bufferSize.WalkInBounds(ref linkEnd, 1);
                        attr = AttributeAt(buffer, linkEnd);
                    }

                    result = (linkStart, linkEnd);
                    break;
                }
            }

            if (!bufferSize.WalkInBounds(ref iterPos, step))
            {
                break;
            }
        }

        if (!result.HasValue)
        {
            return;
        }

        ref var selection = ref WriteSelection();
        selection.Start = result.Value.Start;
        selection.Pivot = result.Value.Start;
        selection.End = result.Value.End;
        _selectionEndpoint = SelectionEndpoint.End;
        ScrollToPoint(direction == SearchDirection.Forward ? selection.End : selection.Start);
    }

    private static TextAttribute AttributeAt(TextBuffer buffer, TilPoint pos) => buffer.GetRowByOffset(pos.Y).GetAttrByColumn(pos.X);

    /// <summary>The patterns that lie entirely within [<paramref name="start"/>, <paramref name="end"/>], in order.</summary>
    private static List<(TilPoint Start, TilPoint Stop, int Value)> FindContained(List<(TilPoint Start, TilPoint Stop, int Value)> patterns, TilPoint start, TilPoint end)
    {
        var result = new List<(TilPoint Start, TilPoint Stop, int Value)>();
        foreach (var pattern in patterns)
        {
            if (pattern.Start >= start && pattern.Stop <= end)
            {
                result.Add(pattern);
            }
        }

        result.Sort((left, right) => left.Start.CompareTo(right.Start));
        return result;
    }

    /// <summary>
    /// The selection change a key asks for: Shift (or any key in mark mode) with arrows, Home, End, Page Up and Down;
    /// with Ctrl by word or to the buffer ends. Null for other keys.
    /// </summary>
    public (SelectionDirection Direction, SelectionExpansion Mode)? ConvertKeyEventToUpdateSelectionParams(ControlKeyStates mods, int virtualKey)
    {
        bool shift = (mods & ControlKeyStates.ShiftPressed) != 0;
        bool alt = (mods & ControlKeyStates.AltPressed) != 0;
        bool ctrl = (mods & ControlKeyStates.CtrlPressed) != 0;

        if ((_selectionMode == SelectionInteractionMode.Mark || shift) && !alt)
        {
            if (ctrl)
            {
                switch (virtualKey)
                {
                    case VirtualKey.LEFT:
                        return (SelectionDirection.Left, SelectionExpansion.Word);
                    case VirtualKey.RIGHT:
                        return (SelectionDirection.Right, SelectionExpansion.Word);
                    case VirtualKey.HOME:
                        return (SelectionDirection.Left, SelectionExpansion.Buffer);
                    case VirtualKey.END:
                        return (SelectionDirection.Right, SelectionExpansion.Buffer);
                }
            }
            else
            {
                switch (virtualKey)
                {
                    case VirtualKey.HOME:
                        return (SelectionDirection.Left, SelectionExpansion.Viewport);
                    case VirtualKey.END:
                        return (SelectionDirection.Right, SelectionExpansion.Viewport);
                    case VirtualKey.PRIOR:
                        return (SelectionDirection.Up, SelectionExpansion.Viewport);
                    case VirtualKey.NEXT:
                        return (SelectionDirection.Down, SelectionExpansion.Viewport);
                    case VirtualKey.LEFT:
                        return (SelectionDirection.Left, SelectionExpansion.Char);
                    case VirtualKey.RIGHT:
                        return (SelectionDirection.Right, SelectionExpansion.Char);
                    case VirtualKey.UP:
                        return (SelectionDirection.Up, SelectionExpansion.Char);
                    case VirtualKey.DOWN:
                        return (SelectionDirection.Down, SelectionExpansion.Char);
                }
            }
        }

        return null;
    }

    /// <summary>Moves one or both ends of the selection by keyboard.</summary>
    public void UpdateSelection(SelectionDirection direction, SelectionExpansion mode, ControlKeyStates mods)
    {
        // In mark mode without Shift, the collapsed selection moves as a whole, like a cursor.
        bool shouldMoveBothEndpoints = _selectionMode == SelectionInteractionMode.Mark && !_anchorInactiveSelectionEndpoint
            && (mods & ControlKeyStates.ShiftPressed) == 0;

        _selectionEndpoint = SelectionEndpoint.None;
        if (shouldMoveBothEndpoints)
        {
            _selectionEndpoint = SelectionEndpoint.Start | SelectionEndpoint.End;
        }
        else if (_selection.Start == _selection.Pivot)
        {
            _selectionEndpoint = SelectionEndpoint.End;
        }
        else if (_selection.End == _selection.Pivot)
        {
            _selectionEndpoint = SelectionEndpoint.Start;
        }

        var targetPos = (_selectionEndpoint & SelectionEndpoint.Start) != 0 ? _selection.Start : _selection.End;

        switch (mode)
        {
            case SelectionExpansion.Char:
                MoveByChar(direction, ref targetPos);
                break;
            case SelectionExpansion.Word:
                MoveByWord(direction, ref targetPos);
                break;
            case SelectionExpansion.Viewport:
                MoveByViewport(direction, ref targetPos);
                break;
            case SelectionExpansion.Buffer:
                MoveByBuffer(direction, ref targetPos);
                break;
        }

        // Moving a mouse selection by keyboard makes it a keyboard selection; mark mode stays.
        if (_selectionMode < SelectionInteractionMode.Keyboard)
        {
            _selectionMode = SelectionInteractionMode.Keyboard;
        }

        ref var selection = ref WriteSelection();
        if (shouldMoveBothEndpoints)
        {
            selection.Start = targetPos;
            selection.End = targetPos;
            selection.Pivot = targetPos;
        }
        else
        {
            var (start, end, targetStart) = PivotSelection(selection.Pivot, targetPos);
            selection.Start = start;
            selection.End = end;
            _selectionEndpoint = targetStart ? SelectionEndpoint.Start : SelectionEndpoint.End;
        }

        ScrollToPoint(targetPos);
    }

    public void SelectAll()
    {
        var bufferSize = ActiveBuffer.Size;
        var end = new TilPoint(bufferSize.RightInclusive, GetMutableViewport().BottomInclusive);
        WriteSelection() = new SelectionInfo
        {
            Start = bufferSize.Origin,
            End = end,
            Pivot = end,
            BlockSelection = false,
            Active = true,
        };
        _selectionMode = SelectionInteractionMode.Keyboard;
        ScrollToPoint(_selection.Start);
    }

    private void MoveByChar(SelectionDirection direction, ref TilPoint pos)
    {
        switch (direction)
        {
            case SelectionDirection.Left:
                ActiveBuffer.MoveToPreviousGlyph2(ref pos);
                break;
            case SelectionDirection.Right:
                ActiveBuffer.MoveToNextGlyph2(ref pos, GetMutableViewport().BottomInclusiveRightExclusive);
                break;
            case SelectionDirection.Up:
            {
                var bufferSize = ActiveBuffer.Size;
                int newY = pos.Y - 1;
                pos = newY < bufferSize.Top ? bufferSize.Origin : pos with { Y = newY };
                break;
            }

            case SelectionDirection.Down:
            {
                var bufferSize = ActiveBuffer.Size;
                int mutableBottom = GetMutableViewport().BottomInclusive;
                int newY = pos.Y + 1;
                pos = newY > mutableBottom ? new TilPoint(bufferSize.RightExclusive, mutableBottom) : pos with { Y = newY };
                break;
            }
        }
    }

    private void MoveByWord(SelectionDirection direction, ref TilPoint pos)
    {
        var buffer = ActiveBuffer;
        switch (direction)
        {
            case SelectionDirection.Left:
            {
                var nextPos = buffer.GetWordStart(pos, _wordDelimiters, true);

                // Already at a word start: go to the start of the word before.
                if (nextPos == pos)
                {
                    buffer.Size.DecrementInExclusiveBounds(ref nextPos);
                    nextPos = buffer.GetWordStart(nextPos, _wordDelimiters, true);
                }

                pos = nextPos;
                break;
            }

            case SelectionDirection.Right:
            {
                var mutableViewportEndExclusive = GetMutableViewport().BottomInclusiveRightExclusive;
                var nextPos = buffer.GetWordEnd(pos, _wordDelimiters, true, mutableViewportEndExclusive);
                if (nextPos == pos)
                {
                    buffer.Size.IncrementInExclusiveBounds(ref nextPos);
                    nextPos = buffer.GetWordEnd(nextPos, _wordDelimiters, true, mutableViewportEndExclusive);
                }

                pos = nextPos;
                break;
            }

            case SelectionDirection.Up:
                MoveByChar(direction, ref pos);
                pos = buffer.GetWordStart(pos, _wordDelimiters, true);
                break;
            case SelectionDirection.Down:
                MoveByChar(direction, ref pos);
                pos = buffer.GetWordEnd(pos, _wordDelimiters, true);
                break;
        }
    }

    private void MoveByViewport(SelectionDirection direction, ref TilPoint pos)
    {
        var bufferSize = ActiveBuffer.Size;
        switch (direction)
        {
            case SelectionDirection.Left:
                pos = new TilPoint(bufferSize.Left, pos.Y);
                break;
            case SelectionDirection.Right:
                pos = new TilPoint(bufferSize.RightExclusive, pos.Y);
                break;
            case SelectionDirection.Up:
            {
                int viewportHeight = GetMutableViewport().Height;
                int newY = pos.Y - viewportHeight;
                pos = newY < bufferSize.Top ? bufferSize.Origin : pos with { Y = newY };
                break;
            }

            case SelectionDirection.Down:
            {
                int viewportHeight = GetMutableViewport().Height;
                int mutableBottom = GetMutableViewport().BottomInclusive;
                int newY = pos.Y + viewportHeight;
                pos = newY > mutableBottom ? new TilPoint(bufferSize.RightExclusive, mutableBottom) : pos with { Y = newY };
                break;
            }
        }
    }

    private void MoveByBuffer(SelectionDirection direction, ref TilPoint pos)
    {
        var bufferSize = ActiveBuffer.Size;
        switch (direction)
        {
            case SelectionDirection.Left:
            case SelectionDirection.Up:
                pos = bufferSize.Origin;
                break;
            case SelectionDirection.Right:
            case SelectionDirection.Down:
                pos = new TilPoint(bufferSize.RightExclusive, GetMutableViewport().BottomInclusive);
                break;
        }
    }

    public void ClearSelection()
    {
        AssertLocked();
        WriteSelection().Active = false;
        _selectionMode = SelectionInteractionMode.None;
        _selectionEndpoint = SelectionEndpoint.None;
        _anchorInactiveSelectionEndpoint = false;
    }

    /// <summary>The selected text; with <paramref name="singleLine"/> the lines are joined and trailing spaces kept.</summary>
    public string RetrieveSelectedTextFromBuffer(bool singleLine)
    {
        if (!IsSelectionActive)
        {
            return string.Empty;
        }

        var textBuffer = ActiveBuffer;
        var request = CopyRequest.FromConfig(textBuffer, _selection.Start, _selection.End, singleLine, _selection.BlockSelection, _trimBlockSelection);
        return textBuffer.GetPlainText(request);
    }

    /// <summary>Converts a viewport cell to a buffer cell, clamped to the buffer.</summary>
    private TilPoint ConvertToBufferCell(TilPoint viewportPos, bool allowRightExclusive)
    {
        var bufferSize = ActiveBuffer.Size;
        int right = allowRightExclusive ? bufferSize.RightExclusive : bufferSize.RightInclusive;
        return new TilPoint(
            Math.Clamp(viewportPos.X, bufferSize.Left, right),
            Math.Clamp(viewportPos.Y + VisibleStartIndex, bufferSize.Top, bufferSize.BottomInclusive));
    }

    /// <summary>Scrolls the view so a buffer cell is visible.</summary>
    private void ScrollToPoint(TilPoint pos)
    {
        var visibleViewport = GetVisibleViewport();
        if (visibleViewport.IsInExclusiveBounds(pos))
        {
            return;
        }

        int amountAboveView = visibleViewport.Top - pos.Y;
        if (amountAboveView > 0)
        {
            _scrollOffset += amountAboveView;
        }
        else
        {
            int amountBelowView = pos.Y - visibleViewport.BottomInclusive;
            _scrollOffset -= amountBelowView;
        }

        NotifyScrollEvent();
        ActiveBuffer.TriggerScroll();
        UpdateUrlDetection();
    }

    public void SetSearchHighlights(List<TilPointSpan> highlights)
    {
        AssertLocked();
        _searchHighlights = highlights;
    }

    public void SetSearchHighlightFocused(int focusedIndex)
    {
        AssertLocked();
        _searchHighlightFocused = focusedIndex;
    }

    public List<TilPointSpan> GetSearchHighlights()
    {
        AssertLocked();
        return _searchHighlights;
    }

    public TilPointSpan? GetSearchHighlightFocused()
    {
        AssertLocked();
        if (_searchHighlightFocused >= 0 && _searchHighlightFocused < _searchHighlights.Count)
        {
            return _searchHighlights[_searchHighlightFocused];
        }

        return null;
    }

    /// <summary>Scrolls to the focused search result, keeping <paramref name="searchScrollOffset"/> rows above it when it is near the top.</summary>
    public void ScrollToSearchHighlight(int searchScrollOffset)
    {
        if (_searchHighlightFocused < _searchHighlights.Count)
        {
            var focused = _searchHighlights[_searchHighlightFocused];

            // The search box covers the top rows, so a result there is scrolled down past it.
            var adjustedStart = focused.Start;
            int firstVisibleRow = VisibleStartIndex;
            if (focused.Start.Y > firstVisibleRow && focused.Start.Y < firstVisibleRow + searchScrollOffset)
            {
                adjustedStart = adjustedStart with { Y = Math.Max(0, focused.Start.Y - searchScrollOffset) };
            }

            ScrollToPoints(adjustedStart, focused.End);
        }
    }

    /// <summary>Scrolls so the range is visible, the start first; returns the visible top row.</summary>
    private int ScrollToPoints(TilPoint start, TilPoint end)
    {
        if (start.Y < VisibleStartIndex)
        {
            ScrollToPoint(start);
        }
        else if (end.Y > VisibleEndIndex)
        {
            ScrollToPoint(end);
        }

        return VisibleStartIndex;
    }

    /// <summary>Selects a buffer range, scrolling it into view.</summary>
    public void SelectNewRegion(TilPoint start, TilPoint end)
    {
        int newScrollOffset = ScrollToPoints(start, end);

        var newStart = start with { Y = start.Y - newScrollOffset };
        var newEnd = end with { Y = end.Y - newScrollOffset };
        SetSelectionAnchor(newStart);
        SetSelectionEnd(newEnd, SelectionExpansion.Char);

        ActiveBuffer.TriggerSelection();
    }
}
