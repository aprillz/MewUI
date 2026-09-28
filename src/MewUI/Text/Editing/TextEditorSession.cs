using System.Globalization;

namespace Aprillz.MewUI.Text.Editing;

/// <summary>Editing state layered over <see cref="EditableTextDocument"/>.</summary>
public sealed class TextEditorSession
{
    private CompositionState? _composition;
    private long _textElementVersion = -1;
    private int _textElementLineOffset = -1;
    private int _textElementLineTotalLength = -1;
    private int[] _textElementStarts = [];

    public TextEditorSession(EditableTextDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Document.SelectionRestored += OnSelectionRestored;
    }

    public EditableTextDocument Document { get; }
    public int CaretPosition { get; private set; }
    public int AnchorPosition { get; private set; }
    public bool CanUndo => Document.History.CanUndo;
    public bool CanRedo => Document.History.CanRedo;
    public bool IsComposing => _composition is not null;
    public TextRange Selection => new(
        Math.Min(CaretPosition, AnchorPosition),
        Math.Abs(CaretPosition - AnchorPosition));

    public event Action? StateChanged;

    public void SetCaret(int position, bool extendSelection = false)
    {
        position = Math.Clamp(position, 0, Document.TextLength);
        if (CaretPosition == position && (extendSelection || AnchorPosition == position))
        {
            return;
        }
        CaretPosition = position;
        if (!extendSelection)
        {
            AnchorPosition = position;
        }
        StateChanged?.Invoke();
    }

    public void SetSelection(int start, int length)
    {
        if (start < 0 || length < 0 || start > Document.TextLength - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        AnchorPosition = start;
        CaretPosition = start + length;
        StateChanged?.Invoke();
    }

    public void SelectAll()
    {
        AnchorPosition = 0;
        CaretPosition = Document.TextLength;
        StateChanged?.Invoke();
    }

    public void SelectWordAt(int position)
    {
        position = Math.Clamp(position, 0, Document.TextLength);
        if (Document.TextLength == 0)
        {
            SetCaret(0);
            return;
        }
        if (position == Document.TextLength) position--;
        char value = Document.GetCharAt(position);
        if (!IsWordCharacter(value))
        {
            int end = NextTextElement(position);
            SetSelection(position, Math.Max(1, end - position));
            return;
        }

        int start = position;
        int wordEnd = position + 1;
        while (start > 0 && IsWordCharacter(Document.GetCharAt(start - 1))) start--;
        while (wordEnd < Document.TextLength && IsWordCharacter(Document.GetCharAt(wordEnd))) wordEnd++;
        SetSelection(start, wordEnd - start);
    }

    public void MoveLogical(LogicalDirection direction, bool extendSelection, bool byWord = false)
    {
        if (!extendSelection && Selection.Length > 0)
        {
            SetCaret(direction == LogicalDirection.Backward ? Selection.Start : Selection.End);
            return;
        }

        int target = byWord
            ? direction == LogicalDirection.Backward
                ? FindPreviousWordBoundary(CaretPosition)
                : FindNextWordBoundary(CaretPosition)
            : direction == LogicalDirection.Backward
                ? PreviousTextElement(CaretPosition)
                : NextTextElement(CaretPosition);
        SetCaret(target, extendSelection);
    }

    internal int GetPreviousCaretPosition(int position)
        => PreviousTextElement(Math.Clamp(position, 0, Document.TextLength));

    public void ReplaceSelection(string? text)
    {
        string normalized = Document.Normalize(text);
        var selection = Selection;
        ApplyAndRecord(selection.Start, selection.Length, normalized);
    }

    /// <summary>
    /// Replaces a document range the way a programmatic edit does: the caret and selection ride
    /// along with the surrounding text instead of landing on the edit, and the change stays
    /// undoable. Editable-region policy is not consulted, which is the caller's to enforce.
    /// </summary>
    public void ReplaceRange(int start, int length, string? text)
    {
        CommitComposition();
        string normalized = Document.Normalize(text);
        int anchorAfter = ShiftPosition(AnchorPosition, start, length, normalized.Length);
        int caretAfter = ShiftPosition(CaretPosition, start, length, normalized.Length);
        if (!Document.History.RecordReplace(
            start, length, normalized, AnchorPosition, CaretPosition, anchorAfter, caretAfter))
        {
            return;
        }
        AnchorPosition = anchorAfter;
        CaretPosition = caretAfter;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Where a position lands after a replace: text inserted at the position pushes it along, and a
    /// position inside the replaced range lands at the end of what replaced it.
    /// </summary>
    private static int ShiftPosition(int position, int start, int removedLength, int insertedLength)
    {
        // An insertion at the position itself satisfies both tests below, so it falls through.
        if (removedLength != 0 || position != start)
        {
            if (position <= start)
            {
                return position;
            }
            if (position >= start + removedLength)
            {
                return position + insertedLength - removedLength;
            }
        }
        return start + insertedLength;
    }

    public void Backspace(bool byWord = false)
    {
        if (Selection.Length > 0)
        {
            ReplaceSelection(string.Empty);
            return;
        }
        int start = byWord ? FindPreviousWordBoundary(CaretPosition) : PreviousTextElement(CaretPosition);
        if (start < CaretPosition)
        {
            ApplyAndRecord(start, CaretPosition - start, string.Empty);
        }
    }

    public void Delete(bool byWord = false)
    {
        if (Selection.Length > 0)
        {
            ReplaceSelection(string.Empty);
            return;
        }
        int end = byWord ? FindNextWordBoundary(CaretPosition) : NextTextElement(CaretPosition);
        if (end > CaretPosition)
        {
            ApplyAndRecord(CaretPosition, end - CaretPosition, string.Empty);
        }
    }

    public void Undo()
    {
        CommitComposition();
        Document.Undo();
    }

    public void Redo()
    {
        CommitComposition();
        Document.Redo();
    }

    public void ClearHistory() => Document.ClearUndoHistory();

    /// <summary>
    /// Adopts the anchor and caret an undo or redo restored, whichever session replayed it: the
    /// positions belong to the edit, and a session left on its old caret would point into text the
    /// replay has already moved.
    /// </summary>
    private void OnSelectionRestored(int anchor, int caret)
    {
        AnchorPosition = anchor;
        CaretPosition = caret;
        StateChanged?.Invoke();
    }

    public void BeginComposition()
    {
        if (_composition is not null)
        {
            return;
        }
        var selection = Selection;
        _composition = new CompositionState(
            selection.Start,
            Document.GetText(selection.Start, selection.Length),
            AnchorPosition,
            CaretPosition);
        if (selection.Length > 0)
        {
            Document.History.ReplaceTransient(selection.Start, selection.Length, string.Empty);
        }
        AnchorPosition = CaretPosition = selection.Start;
        StateChanged?.Invoke();
    }

    public void UpdateComposition(string? text)
    {
        BeginComposition();
        var state = _composition!;
        string normalized = Document.Normalize(text);
        Document.History.ReplaceTransient(state.Start, state.CurrentLength, normalized);
        state.CurrentLength = normalized.Length;
        AnchorPosition = CaretPosition = state.Start + normalized.Length;
        StateChanged?.Invoke();
    }

    public void CommitComposition()
    {
        if (_composition is not CompositionState state)
        {
            return;
        }
        string inserted = Document.GetText(state.Start, state.CurrentLength);
        _composition = null;
        if (state.Removed != inserted)
        {
            Document.History.Push(
                state.Start,
                state.Removed,
                inserted,
                state.AnchorBefore,
                state.CaretBefore,
                AnchorPosition,
                CaretPosition);
        }
        StateChanged?.Invoke();
        if (inserted.Length > 0)
        {
            // Once per commit: the intermediates are transient replaces, so reporting them would
            // fire on every keystroke of a composition.
            TextCommitted?.Invoke(inserted);
        }
    }

    public void CancelComposition()
    {
        if (_composition is not CompositionState state)
        {
            return;
        }
        Document.History.ReplaceTransient(state.Start, state.CurrentLength, state.Removed);
        AnchorPosition = state.AnchorBefore;
        CaretPosition = state.CaretBefore;
        _composition = null;
        StateChanged?.Invoke();
    }

    /// <summary>Consulted before every edit. Null leaves the document fully editable.</summary>
    public IEditableRegionProvider? EditableRegions { get; set; }

    /// <summary>Raised after typed or composed text reached the document, once per commit.</summary>
    public event Action<string>? TextCommitted;

    /// <summary>Applies typed text and reports it once it is in the document.</summary>
    public void EnterText(string? text)
    {
        string normalized = Document.Normalize(text);
        ReplaceSelection(normalized);
        if (normalized.Length > 0)
        {
            TextCommitted?.Invoke(normalized);
        }
    }

    /// <summary>
    /// Applies typed text over the given range instead of the selection: the caret lands at the end
    /// of what was inserted, undo returns to where the caret was, and editable regions are honored.
    /// <see cref="ReplaceRange"/> is the programmatic counterpart whose caret rides along instead.
    /// </summary>
    public void EnterText(int start, int length, string? text)
    {
        string normalized = Document.Normalize(text);
        ApplyAndRecord(start, length, normalized);
        if (normalized.Length > 0)
        {
            TextCommitted?.Invoke(normalized);
        }
    }

    private void ApplyAndRecord(int start, int removeLength, string inserted)
    {
        CommitComposition();
        if (EditableRegions is IEditableRegionProvider regions)
        {
            (removeLength, inserted) = ResolveAgainstEditableRegions(regions, start, removeLength, inserted);
        }
        int caretAfter = start + inserted.Length;
        if (!Document.History.RecordReplace(
            start, removeLength, inserted, AnchorPosition, CaretPosition, caretAfter, caretAfter))
        {
            SetCaret(caretAfter);
            return;
        }
        AnchorPosition = CaretPosition = caretAfter;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Rewrites an edit so it only removes what the provider allows. The protected text is folded
    /// back into the replacement, which keeps the whole edit one contiguous replace and so one undo
    /// step even when the deletable parts are not adjacent.
    /// </summary>
    private (int RemoveLength, string Inserted) ResolveAgainstEditableRegions(
        IEditableRegionProvider regions,
        int start,
        int removeLength,
        string inserted)
    {
        if (inserted.Length > 0 && !regions.CanInsert(start))
        {
            inserted = string.Empty;
        }
        if (removeLength == 0)
        {
            return (0, inserted);
        }

        var deletable = new List<TextRange>();
        regions.GetDeletableRanges(new TextRange(start, removeLength), deletable);

        var kept = new System.Text.StringBuilder();
        int cursor = start;
        foreach (var segment in deletable)
        {
            int segmentStart = Math.Clamp(segment.Start, start, start + removeLength);
            int segmentEnd = Math.Clamp(segment.Start + segment.Length, segmentStart, start + removeLength);
            if (segmentStart > cursor)
            {
                kept.Append(Document.GetText(cursor, segmentStart - cursor));
            }
            cursor = Math.Max(cursor, segmentEnd);
        }
        if (cursor < start + removeLength)
        {
            kept.Append(Document.GetText(cursor, start + removeLength - cursor));
        }

        return (removeLength, inserted + kept.ToString());
    }

    private int PreviousTextElement(int position)
    {
        if (position <= 0)
        {
            return 0;
        }
        int[] starts = GetTextElementStarts(position, backward: true);
        int index = Array.BinarySearch(starts, position);
        if (index >= 0)
        {
            return index == 0 ? 0 : starts[index - 1];
        }
        index = ~index;
        return index == 0 ? 0 : starts[index - 1];
    }

    private int NextTextElement(int position)
    {
        if (position >= Document.TextLength)
        {
            return Document.TextLength;
        }
        int[] starts = GetTextElementStarts(position, backward: false);
        int index = Array.BinarySearch(starts, position);
        index = index >= 0 ? index + 1 : ~index;
        return index < starts.Length
            ? starts[index]
            : Math.Min(Document.TextLength, _textElementLineOffset + _textElementLineTotalLength);
    }

    private int FindPreviousWordBoundary(int position)
    {
        int current = position;
        while (current > 0 && char.IsWhiteSpace(Document.GetCharAt(current - 1)))
        {
            current--;
        }

        // A run of symbols is a word of its own, as a run of letters is; stepping over neither would leave the caret stuck.
        if (current > 0)
        {
            bool word = IsWordCharacter(Document.GetCharAt(current - 1));
            while (current > 0 && IsInRun(Document.GetCharAt(current - 1), word))
            {
                current--;
            }
        }
        return current;
    }

    private int[] GetTextElementStarts(int position, bool backward)
    {
        int probe = backward && position > 0 ? position - 1 : position;
        var line = Document.GetLineByOffset(Math.Clamp(probe, 0, Document.TextLength));
        if (_textElementVersion == Document.Version &&
            _textElementLineOffset == line.Offset &&
            _textElementLineTotalLength == line.TotalLength)
        {
            return _textElementStarts;
        }

        string lineText = Document.GetText(line.Offset, line.TotalLength);
        int[] localStarts = StringInfo.ParseCombiningCharacters(lineText);
        for (int index = 0; index < localStarts.Length; index++)
        {
            localStarts[index] += line.Offset;
        }
        _textElementStarts = localStarts;
        _textElementVersion = Document.Version;
        _textElementLineOffset = line.Offset;
        _textElementLineTotalLength = line.TotalLength;
        return _textElementStarts;
    }

    private int FindNextWordBoundary(int position)
    {
        int current = position;
        if (current < Document.TextLength && !char.IsWhiteSpace(Document.GetCharAt(current)))
        {
            bool word = IsWordCharacter(Document.GetCharAt(current));
            while (current < Document.TextLength && IsInRun(Document.GetCharAt(current), word))
            {
                current++;
            }
        }
        while (current < Document.TextLength && char.IsWhiteSpace(Document.GetCharAt(current)))
        {
            current++;
        }
        return current;
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    /// <summary>Whether a character continues a run of word characters, or of symbols when <paramref name="word"/> is false.</summary>
    private static bool IsInRun(char value, bool word) =>
        word ? IsWordCharacter(value) : !IsWordCharacter(value) && !char.IsWhiteSpace(value);

    private sealed class CompositionState(int start, string removed, int anchorBefore, int caretBefore)
    {
        public int Start { get; } = start;
        public string Removed { get; } = removed;
        public int AnchorBefore { get; } = anchorBefore;
        public int CaretBefore { get; } = caretBefore;
        public int CurrentLength { get; set; }
    }
}
