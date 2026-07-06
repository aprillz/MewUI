using System.Globalization;

namespace Aprillz.MewUI.Controls.Text;

internal sealed class TextEditorCore
{
    private const int MAX_UNDO_ENTRIES = 1000;

    private readonly Func<int> _getLength;
    private readonly Func<int, char> _getChar;
    private readonly Func<int, int, string> _getSubstring;
    private readonly Action<int, string> _applyInsert;
    private readonly Action<int, int> _applyRemove;
    private readonly Action _onEditCommitted;

    private readonly List<Edit> _undo = new();
    private readonly List<Edit> _redo = new();

    private int _selectionStart;
    private int _selectionLength;
    private bool _suppressUndoRecording;

    public TextEditorCore(
        Func<int> getLength,
        Func<int, char> getChar,
        Func<int, int, string> getSubstring,
        Action<int, string> applyInsert,
        Action<int, int> applyRemove,
        Action onEditCommitted)
    {
        _getLength = getLength ?? throw new ArgumentNullException(nameof(getLength));
        _getChar = getChar ?? throw new ArgumentNullException(nameof(getChar));
        _getSubstring = getSubstring ?? throw new ArgumentNullException(nameof(getSubstring));
        _applyInsert = applyInsert ?? throw new ArgumentNullException(nameof(applyInsert));
        _applyRemove = applyRemove ?? throw new ArgumentNullException(nameof(applyRemove));
        _onEditCommitted = onEditCommitted ?? throw new ArgumentNullException(nameof(onEditCommitted));
    }

    public int CaretPosition { get; private set; }

    public bool HasSelection => _selectionLength != 0;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Records an insert edit for undo without applying it to the document.
    /// Used when text is already in the document (e.g. IME composition commit).
    /// </summary>
    public void RecordInsertForUndo(int index, string text)
    {
        RecordEdit(new Edit(EditKind.Insert, index, text));
    }

    public void ResetAfterTextSet()
    {
        ClearUndoRedo();
        CaretPosition = Math.Clamp(CaretPosition, 0, _getLength());
        _selectionStart = 0;
        _selectionLength = 0;
    }

    public void BeginSelectionAtCaret()
    {
        _selectionStart = CaretPosition;
        _selectionLength = 0;
    }

    public void UpdateSelectionToCaret()
    {
        _selectionLength = CaretPosition - _selectionStart;
    }

    public (int start, int end) GetSelectionRange()
    {
        if (_selectionLength == 0)
        {
            return (CaretPosition, CaretPosition);
        }

        int a = _selectionStart;
        int b = _selectionStart + _selectionLength;
        return a <= b ? (a, b) : (b, a);
    }

    public void SetCaretPosition(int value)
    {
        CaretPosition = AdjustForSurrogate(Math.Clamp(value, 0, _getLength()));
    }

    public void SetCaretAndSelection(int newPos, bool extendSelection)
    {
        newPos = AdjustForSurrogate(Math.Clamp(newPos, 0, _getLength()));
        if (!extendSelection)
        {
            CaretPosition = newPos;
            _selectionStart = newPos;
            _selectionLength = 0;
            return;
        }

        if (_selectionLength == 0)
        {
            _selectionStart = CaretPosition;
        }

        CaretPosition = newPos;
        _selectionLength = CaretPosition - _selectionStart;
    }

    public void SelectAll()
    {
        _selectionStart = 0;
        _selectionLength = _getLength();
        CaretPosition = _getLength();
    }

    /// <summary>
    /// Selects the word at the given character position.
    /// The caret is placed at the end of the word.
    /// </summary>
    public void SelectWordAt(int position)
    {
        int length = _getLength();
        if (length == 0) return;

        position = Math.Clamp(position, 0, length);

        // Determine the character class at the position
        int start = position;
        int end = position;

        if (position < length)
        {
            char ch = _getChar(position);
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                // Word character: expand to word boundaries
                while (start > 0 && IsWordChar(_getChar(start - 1))) start--;
                while (end < length && IsWordChar(_getChar(end))) end++;
            }
            else if (char.IsWhiteSpace(ch))
            {
                // Whitespace: select contiguous whitespace, but don't cross line boundaries
                while (start > 0 && char.IsWhiteSpace(_getChar(start - 1)) && _getChar(start - 1) != '\n') start--;
                while (end < length && char.IsWhiteSpace(_getChar(end)) && _getChar(end) != '\n') end++;
            }
            else
            {
                // Punctuation/symbol: select contiguous punctuation
                while (start > 0 && IsPunctuation(_getChar(start - 1))) start--;
                while (end < length && IsPunctuation(_getChar(end))) end++;
            }
        }
        else if (position > 0)
        {
            // At end of text: select the last word
            char ch = _getChar(position - 1);
            if (IsWordChar(ch))
            {
                while (start > 0 && IsWordChar(_getChar(start - 1))) start--;
            }
        }

        _selectionStart = start;
        _selectionLength = end - start;
        CaretPosition = end;
    }

    /// <summary>
    /// Extends the selection by word from the double-click anchor.
    /// Used during drag after double-click to select word-by-word.
    /// </summary>
    public void ExtendSelectionByWord(int currentPosition)
    {
        int length = _getLength();
        if (length == 0) return;

        currentPosition = Math.Clamp(currentPosition, 0, length);

        // Determine word boundaries at the current drag position
        int wordStart = currentPosition;
        int wordEnd = currentPosition;

        if (currentPosition < length)
        {
            char ch = _getChar(currentPosition);
            if (IsWordChar(ch))
            {
                while (wordStart > 0 && IsWordChar(_getChar(wordStart - 1))) wordStart--;
                while (wordEnd < length && IsWordChar(_getChar(wordEnd))) wordEnd++;
            }
            else if (char.IsWhiteSpace(ch) && ch != '\n')
            {
                while (wordStart > 0 && char.IsWhiteSpace(_getChar(wordStart - 1)) && _getChar(wordStart - 1) != '\n') wordStart--;
                while (wordEnd < length && char.IsWhiteSpace(_getChar(wordEnd)) && _getChar(wordEnd) != '\n') wordEnd++;
            }
            else
            {
                while (wordStart > 0 && IsPunctuation(_getChar(wordStart - 1))) wordStart--;
                while (wordEnd < length && IsPunctuation(_getChar(wordEnd))) wordEnd++;
            }
        }

        // Anchor is the original word selection start
        int anchorStart = _selectionStart;
        int anchorEnd = _selectionStart + _selectionLength;

        // Expand selection to cover both the anchor word and the current word
        int selStart = Math.Min(anchorStart, wordStart);
        int selEnd = Math.Max(anchorEnd, wordEnd);

        _selectionStart = selStart;
        _selectionLength = selEnd - selStart;
        CaretPosition = currentPosition < anchorStart ? selStart : selEnd;
    }

    /// <summary>
    /// If <paramref name="pos"/> lands inside a grapheme cluster, nudge it to the
    /// nearest cluster boundary so the caret never splits a combined emoji sequence.
    /// </summary>
    private int AdjustForSurrogate(int pos)
    {
        int length = _getLength();
        if (pos <= 0) return 0;
        if (pos >= length) return length;
        // Fast path: not inside a surrogate pair and not after a ZWJ/VS/skin-tone.
        char c = _getChar(pos);
        if (!char.IsLowSurrogate(c) && c != '\u200D' && c != '\uFE0F' && c != '\uFE0E' && c != '\u20E3')
            return pos;
        // Slow path: walk back to find cluster start, then measure forward.
        return AdjustToClusterBoundary(pos, length);
    }

    private int AdjustToClusterBoundary(int pos, int length)
    {
        // Walk backwards to find a safe cluster start (limit scan for perf).
        int scanBack = Math.Max(0, pos - 32);
        string region = _getSubstring(scanBack, Math.Min(length - scanBack, 64));
        var enumerator = StringInfo.GetTextElementEnumerator(region);
        int clusterStart = scanBack;
        while (enumerator.MoveNext())
        {
            int idx = enumerator.ElementIndex + scanBack;
            int nextIdx = idx + ((string)enumerator.GetTextElement()).Length;
            if (nextIdx > pos) return nextIdx; // snap forward to cluster end
            clusterStart = nextIdx;
        }
        return Math.Min(clusterStart, length);
    }

    /// <summary>
    /// Returns the length of the grapheme cluster starting at <paramref name="pos"/>.
    /// </summary>
    private int GetGraphemeClusterLengthForward(int pos)
    {
        int length = _getLength();
        if (pos >= length) return 0;
        int remaining = Math.Min(length - pos, 32);
        string sub = _getSubstring(pos, remaining);
        return StringInfo.GetNextTextElementLength(sub);
    }

    /// <summary>
    /// Returns the length of the grapheme cluster ending at <paramref name="pos"/>.
    /// </summary>
    private int GetGraphemeClusterLengthBackward(int pos)
    {
        if (pos <= 0) return 0;
        int scanStart = Math.Max(0, pos - 32);
        string sub = _getSubstring(scanStart, pos - scanStart);
        var enumerator = StringInfo.GetTextElementEnumerator(sub);
        int lastClusterLen = 1;
        while (enumerator.MoveNext())
        {
            lastClusterLen = ((string)enumerator.GetTextElement()).Length;
        }
        return lastClusterLen;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    private static bool IsPunctuation(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c != '_';

    public void MoveCaretHorizontal(int direction, bool extendSelection, bool word)
    {

        int length = _getLength();
        if (length == 0)
        {
            SetCaretAndSelection(0, extendSelection);
            return;
        }

        int newPos;
        if (word)
        {
            newPos = direction < 0 ? FindPreviousWordBoundary(CaretPosition) : FindNextWordBoundary(CaretPosition);
        }
        else
        {
            // Move by grapheme cluster (handles surrogate pairs, ZWJ sequences, skin-tone modifiers, etc.).
            if (direction > 0)
            {
                int clusterLen = GetGraphemeClusterLengthForward(CaretPosition);
                newPos = Math.Min(CaretPosition + Math.Max(clusterLen, 1), length);
            }
            else
            {
                int clusterLen = GetGraphemeClusterLengthBackward(CaretPosition);
                newPos = Math.Max(CaretPosition - Math.Max(clusterLen, 1), 0);
            }
        }

        SetCaretAndSelection(newPos, extendSelection);
    }

    public void MoveCaretToDocumentEdge(bool start, bool extendSelection)
    {
        int newPos = start ? 0 : _getLength();
        SetCaretAndSelection(newPos, extendSelection);
    }

    public int FindPreviousWordBoundary(int from)
    {
        if (from <= 0)
        {
            return 0;
        }

        int length = _getLength();
        if (length <= 0)
        {
            return 0;
        }

        int pos = Math.Min(from - 1, length - 1);
        while (pos > 0 && char.IsWhiteSpace(_getChar(pos)))
        {
            pos--;
        }

        while (pos > 0 && !char.IsWhiteSpace(_getChar(pos - 1)))
        {
            pos--;
        }

        return pos;
    }

    public int FindNextWordBoundary(int from)
    {
        int length = _getLength();
        if (from >= length)
        {
            return length;
        }

        int pos = from;
        while (pos < length && !char.IsWhiteSpace(_getChar(pos)))
        {
            pos++;
        }

        while (pos < length && char.IsWhiteSpace(_getChar(pos)))
        {
            pos++;
        }

        return pos;
    }

    public void BackspaceForEdit(bool word)
    {
        if (DeleteSelectionForEdit())
        {
            return;
        }

        if (CaretPosition <= 0)
        {
            return;
        }

        int deleteFrom;
        if (word)
        {
            deleteFrom = FindPreviousWordBoundary(CaretPosition);
        }
        else
        {
            // Delete entire grapheme cluster (handles surrogate pairs, ZWJ sequences, etc.).
            int clusterLen = GetGraphemeClusterLengthBackward(CaretPosition);
            deleteFrom = CaretPosition - Math.Max(clusterLen, 1);
        }
        int deleteLen = CaretPosition - deleteFrom;
        if (deleteLen <= 0)
        {
            return;
        }

        string deleted = _getSubstring(deleteFrom, deleteLen);
        _applyRemove(deleteFrom, deleteLen);
        RecordEdit(new Edit(EditKind.Delete, deleteFrom, deleted));
        SetCaretAndSelection(deleteFrom, false);
        _onEditCommitted();
    }

    public void DeleteForEdit(bool word)
    {
        if (DeleteSelectionForEdit())
        {
            return;
        }

        int length = _getLength();
        if (CaretPosition >= length)
        {
            return;
        }

        int deleteTo;
        if (word)
        {
            deleteTo = FindNextWordBoundary(CaretPosition);
        }
        else
        {
            // Delete entire grapheme cluster (handles surrogate pairs, ZWJ sequences, etc.).
            int clusterLen = GetGraphemeClusterLengthForward(CaretPosition);
            deleteTo = CaretPosition + Math.Max(clusterLen, 1);
        }
        deleteTo = Math.Clamp(deleteTo, CaretPosition, length);

        int deleteLen = deleteTo - CaretPosition;
        if (deleteLen <= 0)
        {
            return;
        }

        string deleted = _getSubstring(CaretPosition, deleteLen);
        _applyRemove(CaretPosition, deleteLen);
        RecordEdit(new Edit(EditKind.Delete, CaretPosition, deleted));
        SetCaretAndSelection(CaretPosition, false);
        _onEditCommitted();
    }

    public bool DeleteSelectionForEdit()
    {
        if (!HasSelection)
        {
            return false;
        }



        var (start, end) = GetSelectionRange();
        int length = end - start;
        string deleted = _getSubstring(start, length);

        _applyRemove(start, length);
        RecordEdit(new Edit(EditKind.Delete, start, deleted));
        CaretPosition = start;
        _selectionStart = start;
        _selectionLength = 0;
        _onEditCommitted();
        return true;
    }

    public void InsertTextAtCaretForEdit(string normalizedText)
    {
        if (string.IsNullOrEmpty(normalizedText))
        {
            return;
        }

        DeleteSelectionForEdit();

        _applyInsert(CaretPosition, normalizedText);
        RecordEdit(new Edit(EditKind.Insert, CaretPosition, normalizedText));
        CaretPosition += normalizedText.Length;
        _selectionStart = CaretPosition;
        _selectionLength = 0;
        _onEditCommitted();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var edit = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        _suppressUndoRecording = true;
        try
        {
            ApplyInverseEdit(edit);
        }
        finally
        {
            _suppressUndoRecording = false;
        }

        _redo.Add(edit);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var edit = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);

        _suppressUndoRecording = true;
        try
        {
            ApplyEdit(edit);
        }
        finally
        {
            _suppressUndoRecording = false;
        }

        _undo.Add(edit);
    }

    public void ClearUndoRedo()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void ApplyInverseEdit(Edit edit)
    {
        if (edit.Kind == EditKind.Insert)
        {
            _applyRemove(edit.Index, edit.Text.Length);
            SetCaretAndSelection(edit.Index, false);
        }
        else
        {
            _applyInsert(edit.Index, edit.Text);
            SetCaretAndSelection(edit.Index + edit.Text.Length, false);
        }

        _selectionStart = CaretPosition;
        _selectionLength = 0;
        _onEditCommitted();
    }

    private void ApplyEdit(Edit edit)
    {
        if (edit.Kind == EditKind.Insert)
        {
            _applyInsert(edit.Index, edit.Text);
            SetCaretAndSelection(edit.Index + edit.Text.Length, false);
        }
        else
        {
            _applyRemove(edit.Index, edit.Text.Length);
            SetCaretAndSelection(edit.Index, false);
        }

        _selectionStart = CaretPosition;
        _selectionLength = 0;
        _onEditCommitted();
    }

    private void RecordEdit(Edit edit)
    {
        if (_suppressUndoRecording)
        {
            return;
        }

        // Merge IME-style replace-at-same-position patterns (macOS Korean IME):
        // Without merging, undo stack has 6 entries for one character.
        // With merging, each replace-at-same-index collapses to a single Insert.
        //
        // Rule: if a Delete at index N exactly cancels the previous Insert at index N
        // (same index, same text), remove both - the next Insert will take the slot.
        if (edit.Kind == EditKind.Delete && _undo.Count >= 1)
        {
            var prev = _undo[^1];
            if (prev.Kind == EditKind.Insert
                && prev.Index == edit.Index
                && prev.Text == edit.Text)
            {
                // Delete cancels previous Insert at same position - remove the Insert,
                // skip recording this Delete. Net effect: slot is empty for next Insert.
                _undo.RemoveAt(_undo.Count - 1);
                _redo.Clear();
                return;
            }
        }

        _undo.Add(edit);
        if (_undo.Count > MAX_UNDO_ENTRIES)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    private enum EditKind { Insert, Delete }

    private readonly record struct Edit(EditKind Kind, int Index, string Text);
}

