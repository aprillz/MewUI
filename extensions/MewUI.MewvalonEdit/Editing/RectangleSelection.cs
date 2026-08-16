using System.Text;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Rendering;

namespace Aprillz.MewUI.MewvalonEdit.Editing;

/// <summary>
/// A selection that spans a column range over several lines rather than a run of offsets. It is
/// built from two x positions rather than two offsets, so lines of different length all give up the
/// same columns, and it uses virtual space whatever <see cref="TextEditorOptions.EnableVirtualSpace"/>
/// says: a column past the end of a short line is still part of the rectangle.
/// </summary>
public sealed class RectangleSelection : Selection
{
    private static readonly string[] _newlineStrings = ["\r\n", "\r", "\n"];

    private readonly TextDocument _document;
    private readonly int _startLine;
    private readonly int _endLine;
    private readonly double _startX;
    private readonly double _endX;
    private readonly int _topLeftOffset;
    private readonly int _bottomRightOffset;
    private readonly TextViewPosition _start;
    private readonly TextViewPosition _end;
    private readonly List<SelectionSegment> _segments = [];

    public RectangleSelection(TextArea textArea, TextViewPosition start, TextViewPosition end)
        : base(textArea)
    {
        _document = textArea.Document;
        _startLine = start.Line;
        _endLine = end.Line;
        _startX = GetX(textArea, start);
        _endX = GetX(textArea, end);
        CalculateSegments();
        (_topLeftOffset, _bottomRightOffset) = ResolveCornerOffsets();
        _start = start;
        _end = end;
    }

    // The drag constructor: the left border keeps the stored x pixel rather than re-deriving it
    // from a position, so it does not drift while the end is dragged across lines.
    private RectangleSelection(TextArea textArea, int startLine, double startX, TextViewPosition end)
        : base(textArea)
    {
        _document = textArea.Document;
        _startLine = startLine;
        _endLine = end.Line;
        _startX = startX;
        _endX = GetX(textArea, end);
        CalculateSegments();
        (_topLeftOffset, _bottomRightOffset) = ResolveCornerOffsets();
        _start = GetStart();
        _end = end;
    }

    // The paste constructor: the block's height and right border are known, the end position not.
    private RectangleSelection(TextArea textArea, TextViewPosition start, int endLine, double endX)
        : base(textArea)
    {
        _document = textArea.Document;
        _startLine = start.Line;
        _endLine = endLine;
        _startX = GetX(textArea, start);
        _endX = endX;
        CalculateSegments();
        (_topLeftOffset, _bottomRightOffset) = ResolveCornerOffsets();
        _start = start;
        _end = GetEnd();
    }

    /// <summary>Where the rectangle was started from, which is the corner the caret left behind.</summary>
    public override TextViewPosition StartPosition => _start;

    /// <summary>Where the rectangle currently reaches, which is the corner the caret is at.</summary>
    public override TextViewPosition EndPosition => _end;

    /// <summary>One range per line the rectangle covers, in line order.</summary>
    public override IEnumerable<SelectionSegment> Segments => _segments;

    /// <summary>
    /// Everything from the top-left corner to the bottom-right one, which is the range the
    /// rectangle is contained in rather than the range it selects.
    /// </summary>
    public override ISegment? SurroundingSegment
        => _segments.Count == 0
            ? null
            : new SimpleSegment(_topLeftOffset, _bottomRightOffset - _topLeftOffset);

    /// <summary>Always true: a rectangle selects columns, and a short line has to give up the same ones.</summary>
    public override bool EnableVirtualSpace => true;

    public override int Length => _segments.Sum(static segment => segment.Length);

    public override string GetText()
    {
        var text = new StringBuilder();
        foreach (var segment in _segments)
        {
            if (text.Length > 0)
            {
                text.AppendLine();
            }
            text.Append(_document.GetText(segment.StartOffset, segment.Length));
        }
        return text.ToString();
    }

    public override Selection SetEndpoint(TextViewPosition endPosition)
        => new RectangleSelection(TextArea, _startLine, _startX, endPosition);

    public override Selection StartSelectionOrSetEndpoint(
        TextViewPosition startPosition, TextViewPosition endPosition)
        => SetEndpoint(endPosition);

    /// <summary>
    /// The rectangle over the changed document. The top-left corner rides after an insertion at it
    /// and the bottom-right stays before one, and each corner's column is read again from the
    /// stored x, so the rectangle stays visually where it was.
    /// </summary>
    public override Selection UpdateOnDocumentChange(DocumentChangeEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var newStartLocation = _document.GetLocation(
            e.GetNewOffset(_topLeftOffset, AnchorMovementType.AfterInsertion));
        var newEndLocation = _document.GetLocation(
            e.GetNewOffset(_bottomRightOffset, AnchorMovementType.BeforeInsertion));
        return new RectangleSelection(
            TextArea,
            new TextViewPosition(newStartLocation, GetVisualColumnFromX(newStartLocation.Line, _startX)),
            new TextViewPosition(newEndLocation, GetVisualColumnFromX(newEndLocation.Line, _endX)));
    }

    /// <summary>
    /// Replaces every line's range as one undo step. Text without a newline goes into every line
    /// and the rectangle survives for further typing; multi-line text is distributed one line per
    /// segment and ends the selection, which is how a block paste lands.
    /// </summary>
    public override void ReplaceSelectionWithText(string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        using var group = _document.UndoStack.OpenUndoGroup();
        int firstInsertionLength = 0;
        int editOffset = Math.Min(_topLeftOffset, _bottomRightOffset);
        TextViewPosition pos;
        if (newText.AsSpan().IndexOfAny('\r', '\n') < 0)
        {
            // Bottom up, so replacing one line does not move the ranges of the lines still to come.
            for (int index = _segments.Count - 1; index >= 0; index--)
            {
                ReplaceSingleLineText(_segments[index], newText, out int insertionLength);
                firstInsertionLength = insertionLength;
            }
            pos = new TextViewPosition(_document.GetLocation(editOffset + firstInsertionLength));
            TextArea.Selection = new RectangleSelection(
                TextArea, pos, Math.Max(_startLine, _endLine), GetX(TextArea, pos));
        }
        else
        {
            string[] lines = newText.Split(_newlineStrings, _segments.Count, StringSplitOptions.None);
            for (int index = lines.Length - 1; index >= 0; index--)
            {
                ReplaceSingleLineText(_segments[index], lines[index], out int insertionLength);
                firstInsertionLength = insertionLength;
            }
            pos = new TextViewPosition(_document.GetLocation(editOffset + firstInsertionLength));
            TextArea.ClearSelection();
        }
        // The original substitutes a default position when the point resolves to nothing; here a
        // default would address line 0, so the caret only moves when the point resolves.
        if (TextArea.TextView.GetPosition(new Point(
                GetX(TextArea, pos),
                TextArea.TextView.GetVisualTopByDocumentLine(Math.Max(_startLine, _endLine))))
            is TextViewPosition caretPosition)
        {
            TextArea.Caret.Position = caretPosition;
        }
    }

    /// <summary>
    /// Pastes multi-line text as a column at <paramref name="startPosition"/>, one text line per
    /// document line downward. Returns false when the block does not fit the document (or, without
    /// virtual space, the lines are too short), which sends the caller back to a plain paste.
    /// </summary>
    public static bool PerformRectangularPaste(
        TextArea textArea, TextViewPosition startPosition, string text, bool selectInsertedText)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        ArgumentNullException.ThrowIfNull(text);
        // Counting '\n' misses lone-'\r' endings; the original carries the same known limit.
        int newLineCount = text.Count(static character => character == '\n');
        var endLocation = new TextLocation(startPosition.Line + newLineCount, startPosition.Column);
        if (endLocation.Line <= textArea.Document.LineCount)
        {
            int endOffset = textArea.Document.GetOffset(endLocation.Line, endLocation.Column);
            if (textArea.Selection.EnableVirtualSpace
                || textArea.Document.GetLocation(endOffset) == endLocation)
            {
                var pasteSelection = new RectangleSelection(
                    textArea, startPosition, endLocation.Line, GetX(textArea, startPosition));
                pasteSelection.ReplaceSelectionWithText(text);
                if (selectInsertedText && textArea.Selection is RectangleSelection inserted)
                {
                    textArea.Selection = new RectangleSelection(
                        textArea, startPosition, inserted._endLine, inserted._endX);
                }
                return true;
            }
        }
        return false;
    }

    public override bool Equals(object? obj)
        => obj is RectangleSelection other
            && other._topLeftOffset == _topLeftOffset
            && other._bottomRightOffset == _bottomRightOffset
            && other._startLine == _startLine
            && other._endLine == _endLine
            && other._startX.Equals(_startX)
            && other._endX.Equals(_endX)
            && ReferenceEquals(other.TextArea, TextArea);

    public override int GetHashCode() => HashCode.Combine(_topLeftOffset, _bottomRightOffset);

    // Offsets may be stale when this is printed for an old selection; locations are not resolved
    // so the message cannot crash on them.
    public override string ToString()
        => $"[RectangleSelection {_startLine} {_topLeftOffset} {_startX} to {_endLine} {_bottomRightOffset} {_endX}]";

    private void ReplaceSingleLineText(SelectionSegment lineSegment, string newText, out int insertionLength)
    {
        if (lineSegment.Length == 0)
        {
            if (newText.Length > 0 && CanInsert(lineSegment.StartOffset))
            {
                newText = AddSpacesIfRequired(
                    newText,
                    new TextViewPosition(_document.GetLocation(lineSegment.StartOffset), lineSegment.StartVisualColumn),
                    new TextViewPosition(_document.GetLocation(lineSegment.EndOffset), lineSegment.EndVisualColumn));
                _document.Insert(lineSegment.StartOffset, newText);
            }
        }
        else
        {
            var segmentsToDelete = GetDeletableSegments(lineSegment);
            var surrounding = SurroundingSegment;
            for (int index = segmentsToDelete.Length - 1; index >= 0; index--)
            {
                if (index == segmentsToDelete.Length - 1)
                {
                    if (surrounding is not null &&
                        segmentsToDelete[index].Offset == surrounding.Offset &&
                        segmentsToDelete[index].Length == surrounding.Length)
                    {
                        newText = AddSpacesIfRequired(
                            newText,
                            new TextViewPosition(_document.GetLocation(lineSegment.StartOffset), lineSegment.StartVisualColumn),
                            new TextViewPosition(_document.GetLocation(lineSegment.EndOffset), lineSegment.EndVisualColumn));
                    }
                    _document.Replace(segmentsToDelete[index], newText);
                }
                else
                {
                    _document.Remove(segmentsToDelete[index].Offset, segmentsToDelete[index].Length);
                }
            }
        }
        insertionLength = newText.Length;
    }

    private bool CanInsert(int offset)
        => TextArea.ReadOnlySectionProvider?.CanInsert(offset) ?? true;

    private ISegment[] GetDeletableSegments(ISegment segment)
    {
        if (TextArea.ReadOnlySectionProvider is IReadOnlySectionProvider provider)
        {
            return provider.GetDeletableSegments(segment).ToArray();
        }
        return [segment];
    }

    /// <summary>
    /// Where a position sits across the view, which is what a rectangle is made of. Two positions on
    /// different lines with the same x belong to the same column of the rectangle however much tab
    /// or marker width lies before them. The row is chosen explicitly so a wrap seam answers with
    /// the caret's row rather than always the first.
    /// </summary>
    private static double GetX(TextArea textArea, TextViewPosition position)
    {
        // By offset rather than by line: a line laid out in slices has to answer with the one the
        // position stands in.
        var visualLine = textArea.TextView.GetOrConstructVisualLine(
            textArea.Document.GetOffset(position.Line, position.Column));
        if (visualLine is null)
        {
            return 0;
        }
        int visualColumn = visualLine.ValidateVisualColumn(position, allowVirtualSpace: true);
        var row = visualLine.GetTextLine(visualColumn, position.IsAtEndOfLine);
        return visualLine.GetTextLineVisualXPosition(row, visualColumn);
    }

    private int GetVisualColumnFromX(int line, double x)
    {
        var visualLine = TextArea.TextView.GetOrConstructVisualLine(_document.GetLineByNumber(line));
        return visualLine is null ? 0 : visualLine.GetVisualColumn(new Point(x, 0), allowVirtualSpace: true);
    }

    private void CalculateSegments()
    {
        foreach (var line in CoveredLines())
        {
            (int startOffset, int startColumn) = Resolve(line, _startX);
            (int endOffset, int endColumn) = Resolve(line, _endX);
            _segments.Add(new SelectionSegment(startOffset, startColumn, endOffset, endColumn));
        }
    }

    /// <summary>
    /// Where an x stands on a line. A line long enough to be laid out in slices is read in the slice
    /// the x falls in, so each edge of the rectangle answers from where it actually is. The offset
    /// and the column belong together and are only ever used as a pair.
    /// </summary>
    private (int Offset, int VisualColumn) Resolve(DocumentLine line, double x)
    {
        var visualLine = TextArea.TextView.GetOrConstructVisualLine(line, x);
        if (visualLine is null)
        {
            return (line.Offset, 0);
        }
        int column = visualLine.GetVisualColumn(new Point(x, 0), allowVirtualSpace: true);
        return (visualLine.StartOffset + visualLine.GetRelativeOffset(column), column);
    }

    /// <summary>
    /// The document lines the rectangle crosses, each laid-out line visited once. A collapsed folding
    /// puts several document lines on one laid-out line, so the walk steps by the line it just
    /// visited rather than by one document line, as the original does.
    /// </summary>
    private IEnumerable<DocumentLine> CoveredLines()
    {
        int last = Math.Min(Math.Max(_startLine, _endLine), _document.LineCount);
        var line = _document.GetLineByNumber(Math.Min(_startLine, _endLine));
        while (line is not null && line.LineNumber <= last)
        {
            var visualLine = TextArea.TextView.GetOrConstructVisualLine(line.Offset);
            if (visualLine is null)
            {
                line = line.NextLine;
                continue;
            }
            yield return visualLine.FirstDocumentLine;
            line = visualLine.LastDocumentLine.NextLine;
        }
    }

    /// <summary>
    /// Where a caret belongs on each line the rectangle crosses: the moving edge, which is the one
    /// the caret was walked to. A column past the end of its line has no character, so the offset is
    /// the line's end and the column says how far past it the caret stands.
    /// </summary>
    internal IEnumerable<(int Offset, int VisualColumn)> CaretEdges()
    {
        foreach (var line in CoveredLines())
        {
            yield return Resolve(line, _endX);
        }
    }

    private (int topLeft, int bottomRight) ResolveCornerOffsets()
        => _segments.Count == 0 ? (0, 0) : (_segments[0].StartOffset, _segments[^1].EndOffset);

    private TextViewPosition GetStart()
    {
        if (_segments.Count == 0)
        {
            return default;
        }
        var segment = _startLine < _endLine ? _segments[0] : _segments[^1];
        if (_startX < _endX)
        {
            return new TextViewPosition(_document.GetLocation(segment.StartOffset), segment.StartVisualColumn);
        }
        else
        {
            return new TextViewPosition(_document.GetLocation(segment.EndOffset), segment.EndVisualColumn);
        }
    }

    private TextViewPosition GetEnd()
    {
        if (_segments.Count == 0)
        {
            return default;
        }
        var segment = _startLine < _endLine ? _segments[^1] : _segments[0];
        if (_startX < _endX)
        {
            return new TextViewPosition(_document.GetLocation(segment.EndOffset), segment.EndVisualColumn);
        }
        else
        {
            return new TextViewPosition(_document.GetLocation(segment.StartOffset), segment.StartVisualColumn);
        }
    }
}
