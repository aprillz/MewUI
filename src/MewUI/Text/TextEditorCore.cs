namespace Aprillz.MewUI.Text;

internal sealed class TextEditorCore
{
    private readonly Func<int> _getLength;
    private readonly Func<int, char> _getChar;
    private readonly Func<int, int, string> _getSubstring;
    private readonly Action<int, string> _applyInsert;
    private readonly Action<int, int> _applyRemove;
    private readonly Action _onEditCommitted;

    private int _selectionStart;
    private int _selectionLength;

    private readonly List<Edit> _undo = new();
    private readonly List<Edit> _redo = new();
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
        CaretPosition = Math.Clamp(value, 0, _getLength());
    }

    public void SetCaretAndSelection(int newPos, bool extendSelection)
    {
        newPos = Math.Clamp(newPos, 0, _getLength());
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
            newPos = Math.Clamp(CaretPosition + direction, 0, length);
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

        int deleteFrom = word ? FindPreviousWordBoundary(CaretPosition) : CaretPosition - 1;
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

        int deleteTo = word ? FindNextWordBoundary(CaretPosition) : CaretPosition + 1;
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

        _undo.Add(edit);
        _redo.Clear();
    }

    private enum EditKind { Insert, Delete }

    private readonly record struct Edit(EditKind Kind, int Index, string Text);
}

