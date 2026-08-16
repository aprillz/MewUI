using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// One laid-out line of the view: the document line, the elements generators built on it, and the
/// offset-to-visual-column mapping. Mirrors AvalonEdit's type over a materialized engine line;
/// visual columns are projected offsets of the engine's offset map.
/// </summary>
public sealed class VisualLine
{
    private readonly TextLineLayout _layout;
    private readonly TextView _textView;

    internal VisualLine(
        TextView textView,
        TextLineLayout layout,
        DocumentLine firstDocumentLine,
        IReadOnlyList<VisualLineElement> elements)
    {
        _textView = textView;
        _layout = layout;
        FirstDocumentLine = firstDocumentLine;
        Elements = elements;
    }

    public DocumentLine FirstDocumentLine { get; }

    /// <summary>
    /// Last document line this one covers. Later than <see cref="FirstDocumentLine"/> where a
    /// collapsed folding hid the lines in between, which is what a walk over document lines has to
    /// skip past to visit each laid-out line once.
    /// </summary>
    public DocumentLine LastDocumentLine
        => Document.GetLineByOffset(StartOffset + DocumentLength);

    /// <summary>Document this line was laid out from.</summary>
    public TextDocument Document => _textView.Document;

    /// <summary>Elements the generators produced on this line, in document order.</summary>
    public IReadOnlyList<VisualLineElement> Elements { get; }

    /// <summary>Document offset the laid-out range starts at. Mid-line for a virtualized slice.</summary>
    public int StartOffset => _layout.LogicalLine.Offset;

    /// <summary>Length of the laid-out document range.</summary>
    public int DocumentLength => _layout.LogicalLine.Length;

    /// <summary>
    /// Length of the line on the visual surface. Longer than <see cref="DocumentLength"/> where a
    /// projection stands more columns in for the document text, shorter where it stands in fewer.
    /// </summary>
    public int VisualLength => _layout.MapSourceOffsetToProjected(DocumentLength);

    /// <summary>Top of the line in document coordinates.</summary>
    public double VisualTop => _layout.DocumentY;

    public double Height => _layout.Height;

    /// <summary>The rows this line wraps into, in order. A line that does not wrap has one.</summary>
    public IReadOnlyList<VisualTextLine> TextLines => _layout.VisualLines;

    /// <summary>
    /// <see cref="VisualLength"/> plus the end-of-line marker's column when the options show one.
    /// The laid-out line ends here, so virtual space starts at the column after it.
    /// </summary>
    public int VisualLengthWithEndOfLineMarker
        => VisualLength + (_textView.Options.ShowEndOfLine && LastDocumentLine.NextLine is not null ? 1 : 0);

    /// <summary>First visual column of <paramref name="row"/>.</summary>
    public int GetTextLineVisualStartColumn(VisualTextLine row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.LogicalStart;
    }

    /// <summary>
    /// Next caret stop after <paramref name="visualColumn"/> in <paramref name="direction"/>, or -1
    /// when this line has none. Stops follow the document text of the line rather than projected
    /// elements; virtual-space columns step one by one where <paramref name="allowVirtualSpace"/>
    /// permits, which only the grapheme modes do.
    /// </summary>
    public int GetNextCaretPosition(
        int visualColumn, LogicalDirection direction, CaretPositioningMode mode, bool allowVirtualSpace)
    {
        if (mode is not CaretPositioningMode.Normal and not CaretPositioningMode.EveryCodepoint)
        {
            allowVirtualSpace = false;
        }
        int lineStart = StartOffset;
        // The laid-out range, not the first document line: a collapsed folding puts several document
        // lines on this one, and a scan bounded by the first line's length would end inside it.
        int lineEnd = StartOffset + DocumentLength;
        if (direction == LogicalDirection.Backward)
        {
            if (visualColumn > VisualLength)
            {
                return allowVirtualSpace ? visualColumn - 1 : VisualLength;
            }
            if (StopFromElement(visualColumn, direction, mode) is int elementStop)
            {
                return elementStop;
            }
            int offset = lineStart + GetRelativeOffset(Math.Clamp(visualColumn, 0, VisualLength));
            int next = TextUtilities.GetNextCaretPosition(Document, offset, direction, mode);
            if (next >= lineStart && next <= lineEnd)
            {
                return GetVisualColumn(next - lineStart);
            }
            // The scan left the line; the line start is still an implicit stop in grapheme modes.
            if (visualColumn > 0 &&
                mode is CaretPositioningMode.Normal or CaretPositioningMode.EveryCodepoint)
            {
                return 0;
            }
            return -1;
        }
        else
        {
            if (visualColumn >= VisualLength)
            {
                return allowVirtualSpace ? visualColumn + 1 : -1;
            }
            if (StopFromElement(visualColumn, direction, mode) is int elementStop)
            {
                return elementStop;
            }
            int offset = lineStart + GetRelativeOffset(Math.Max(visualColumn, 0));
            int next = TextUtilities.GetNextCaretPosition(Document, offset, direction, mode);
            if (next >= lineStart && next <= lineEnd)
            {
                return GetVisualColumn(next - lineStart);
            }
            // The scan left the line; the line end is always an implicit stop.
            if (visualColumn < VisualLength)
            {
                return VisualLength;
            }
            return -1;
        }
    }

    /// <summary>
    /// The stop the element covering <paramref name="visualColumn"/> gives, or null where no element
    /// covers it and the document text answers instead. An element speaks for its own columns: a
    /// folded region's placeholder stands for text the caret must step over rather than into.
    /// </summary>
    private int? StopFromElement(int visualColumn, LogicalDirection direction, CaretPositioningMode mode)
    {
        foreach (var element in Elements)
        {
            if (visualColumn < element.VisualColumn ||
                visualColumn >= element.VisualColumn + element.VisualLength)
            {
                continue;
            }
            int stop = element.GetNextCaretPosition(visualColumn, direction, mode);
            return stop < 0 ? null : stop;
        }
        return null;
    }

    /// <summary>
    /// The row a visual column falls on. At a wrap seam the column ends one row and starts the
    /// next; <paramref name="isAtEndOfLine"/> picks the earlier row, as a caret at a row end does.
    /// </summary>
    public VisualTextLine GetTextLine(int visualColumn, bool isAtEndOfLine = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visualColumn);
        int lookupColumn = isAtEndOfLine && visualColumn > 0 ? visualColumn - 1 : visualColumn;
        foreach (var row in TextLines)
        {
            if (lookupColumn < row.LogicalStart + row.LogicalLength)
            {
                return row;
            }
        }
        return TextLines[^1];
    }

    /// <summary>Metrics the engine measured for <paramref name="row"/>.</summary>
    public TextLayoutLineMetrics GetTextLineMetrics(VisualTextLine row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Layout.Lines[row.LayoutLineIndex];
    }

    /// <summary>Document-space top of <paramref name="row"/>, measured as <paramref name="yPositionMode"/> asks.</summary>
    public double GetTextLineVisualYPosition(VisualTextLine row, VisualYPosition yPositionMode)
    {
        ArgumentNullException.ThrowIfNull(row);
        return GetRowVisualYPosition(row, yPositionMode);
    }

    /// <summary>
    /// Document-space x of a visual column, read on <paramref name="row"/> rather than wherever the
    /// column resolves. At the seam of a wrap the column belongs to two rows and they answer with
    /// different x, which is why the caller names the row.
    /// </summary>
    public double GetTextLineVisualXPosition(VisualTextLine row, int visualColumn)
    {
        ArgumentNullException.ThrowIfNull(row);
        int rowEnd = row.LogicalStart + row.LogicalLength;
        if (visualColumn >= rowEnd && !ReferenceEquals(row, TextLines[^1]))
        {
            return row.Bounds.Right;
        }
        double x = _layout.DocumentX
            + _layout.GetCaretBounds(new CharacterHit(Math.Min(visualColumn, VisualLengthWithEndOfLineMarker), 0)).X;
        if (visualColumn > VisualLengthWithEndOfLineMarker)
        {
            x += (visualColumn - VisualLengthWithEndOfLineMarker) * _textView.WideSpaceWidth;
        }
        return x;
    }

    /// <summary>Bounds of a visual column range that lies inside <paramref name="row"/>.</summary>
    public void GetTextBounds(VisualTextLine row, int startColumn, int length, IList<Rect> output)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(output);
        int rowEnd = row.LogicalStart + row.LogicalLength;
        int start = Math.Clamp(startColumn, row.LogicalStart, rowEnd);
        int end = Math.Clamp(startColumn + length, start, rowEnd);
        if (end > start)
        {
            _layout.GetRangeBounds(new TextRange(start, end - start), output);
        }
    }

    /// <summary>Visual column of a document offset relative to <see cref="StartOffset"/>.</summary>
    public int GetVisualColumn(int relativeTextOffset)
        => _layout.MapSourceOffsetToProjected(Math.Clamp(relativeTextOffset, 0, DocumentLength));

    /// <summary>Document offset (relative to <see cref="StartOffset"/>) of a visual column.</summary>
    public int GetRelativeOffset(int visualColumn)
        => _layout.MapProjectedOffsetToSource(ValidateVisualColumn(visualColumn));

    /// <summary>Clamps a visual column into this line.</summary>
    public int ValidateVisualColumn(int visualColumn)
        => Math.Clamp(visualColumn, 0, VisualLength);

    /// <summary>
    /// The visual column of <paramref name="position"/>, worked out from its location when the
    /// column is unknown or no longer matches the offset it claims.
    /// </summary>
    public int ValidateVisualColumn(TextViewPosition position, bool allowVirtualSpace)
        => ValidateVisualColumn(Document.GetOffset(position.Line, position.Column), position.VisualColumn, allowVirtualSpace);

    public int ValidateVisualColumn(int offset, int visualColumn, bool allowVirtualSpace)
    {
        if (visualColumn < 0)
        {
            return GetVisualColumn(offset - StartOffset);
        }
        if (GetRelativeOffset(visualColumn) + StartOffset != offset)
        {
            return GetVisualColumn(offset - StartOffset);
        }
        return visualColumn > VisualLength && !allowVirtualSpace ? VisualLength : visualColumn;
    }

    /// <summary>Document-space position of a visual column.</summary>
    public Point GetVisualPosition(int visualColumn, VisualYPosition yPositionMode)
        => GetVisualPosition(visualColumn, false, yPositionMode);

    internal Point GetVisualPosition(int visualColumn, bool isAtEndOfLine, VisualYPosition yPositionMode)
    {
        var row = GetRow(visualColumn, isAtEndOfLine);
        return new Point(GetVisualXPosition(visualColumn), GetRowVisualYPosition(row, yPositionMode));
    }

    /// <summary>Document-space distance from the left of the text to a visual column.</summary>
    public double GetVisualXPosition(int visualColumn)
    {
        double x = _layout.DocumentX
            + _layout.GetCaretBounds(new CharacterHit(Math.Min(visualColumn, VisualLengthWithEndOfLineMarker), 0)).X;
        if (visualColumn > VisualLengthWithEndOfLineMarker)
        {
            x += (visualColumn - VisualLengthWithEndOfLineMarker) * _textView.WideSpaceWidth;
        }
        return x;
    }

    /// <summary>Position at a visual column, with its location taken from the document.</summary>
    public TextViewPosition GetTextViewPosition(int visualColumn)
        => new(Document.GetLocation(GetRelativeOffset(visualColumn) + StartOffset), visualColumn);

    /// <summary>
    /// Position at a document-space point, rounded to the nearest character boundary.
    /// </summary>
    public TextViewPosition GetTextViewPosition(Point documentPoint, bool allowVirtualSpace)
        => CreatePosition(GetVisualColumn(documentPoint, allowVirtualSpace, out bool isAtEndOfLine), isAtEndOfLine);

    /// <summary>
    /// Position at a document-space point, rounded down to the character the point is inside. Past
    /// the end of the line it truncates rather than rounds, so a point short of the next column
    /// does not reach it.
    /// </summary>
    public TextViewPosition GetTextViewPositionFloor(Point documentPoint, bool allowVirtualSpace)
        => CreatePosition(
            GetVisualColumnFloor(documentPoint, allowVirtualSpace, out bool isAtEndOfLine), isAtEndOfLine);

    /// <summary>Visual column at a document-space point, rounded to the nearest boundary.</summary>
    public int GetVisualColumn(Point documentPoint, bool allowVirtualSpace)
        => GetVisualColumn(documentPoint, allowVirtualSpace, out _);

    internal int GetVisualColumn(Point documentPoint, bool allowVirtualSpace, out bool isAtEndOfLine)
    {
        var row = GetRowByY(documentPoint.Y);
        int column = TryGetVirtualColumn(documentPoint, row, allowVirtualSpace, out int virtualColumn)
            ? virtualColumn
            : HitTest(documentPoint).InsertionIndex;
        isAtEndOfLine = column >= row.LogicalStart + row.LogicalLength;
        return column;
    }

    internal int GetVisualColumnFloor(Point documentPoint, bool allowVirtualSpace, out bool isAtEndOfLine)
    {
        var row = GetRowByY(documentPoint.Y);
        double x = documentPoint.X - _layout.DocumentX;
        if (x > row.Bounds.Width)
        {
            isAtEndOfLine = true;
            if (allowVirtualSpace && ReferenceEquals(row, _layout.VisualLines[^1]))
            {
                // Truncated, not rounded: a floor may not answer with a column the point has not
                // reached. This is the one place the two lookups part.
                return VisualLengthWithEndOfLineMarker
                    + (int)((x - row.Bounds.Width) / _textView.WideSpaceWidth);
            }
            // Past the row with nowhere to go, the row's end is the answer; the hit test would name
            // the last character instead.
            return row.LogicalStart + row.LogicalLength;
        }
        isAtEndOfLine = false;
        return HitTest(documentPoint).FirstCharacterIndex;
    }

    private TextViewPosition CreatePosition(int visualColumn, bool isAtEndOfLine)
        => GetTextViewPosition(visualColumn) with { IsAtEndOfLine = isAtEndOfLine };

    private CharacterHit HitTest(Point documentPoint)
        => _layout.HitTest(new Point(documentPoint.X - _layout.DocumentX, documentPoint.Y - _layout.DocumentY));

    /// <summary>
    /// Columns past the end of the last row, which exist only where virtual space is allowed.
    /// </summary>
    private bool TryGetVirtualColumn(
        Point documentPoint, VisualTextLine row, bool allowVirtualSpace, out int visualColumn)
    {
        double x = documentPoint.X - _layout.DocumentX;
        var rows = _layout.VisualLines;
        if (!allowVirtualSpace || row != rows[^1] || x <= row.Bounds.Width)
        {
            visualColumn = 0;
            return false;
        }
        int virtualColumns = (int)Math.Round(
            (x - row.Bounds.Width) / _textView.WideSpaceWidth, MidpointRounding.AwayFromZero);
        visualColumn = VisualLengthWithEndOfLineMarker + virtualColumns;
        return true;
    }

    private VisualTextLine GetRow(int visualColumn, bool isAtEndOfLine)
    {
        var rows = _layout.VisualLines;
        for (int index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            int end = row.LogicalStart + row.LogicalLength;
            if (visualColumn < end)
            {
                return row;
            }
            // At the seam of a wrap the column belongs to both rows; the flag picks the earlier one.
            if (visualColumn == end && isAtEndOfLine)
            {
                return row;
            }
        }
        return rows[^1];
    }

    private VisualTextLine GetRowByY(double documentY)
    {
        var rows = _layout.VisualLines;
        foreach (var row in rows)
        {
            if (documentY < row.Bounds.Bottom)
            {
                return row;
            }
        }
        return rows[^1];
    }

    private double GetRowVisualYPosition(VisualTextLine row, VisualYPosition yPositionMode)
    {
        double top = row.Bounds.Y;
        double textTop = top + row.Baseline - _textView.DefaultBaseline;
        return yPositionMode switch
        {
            VisualYPosition.LineTop => top,
            VisualYPosition.LineMiddle => top + (row.Bounds.Height / 2),
            VisualYPosition.LineBottom => top + row.Bounds.Height,
            VisualYPosition.TextTop => textTop,
            VisualYPosition.TextBottom => textTop + _textView.DefaultLineHeight,
            VisualYPosition.TextMiddle => textTop + (_textView.DefaultLineHeight / 2),
            VisualYPosition.Baseline => top + row.Baseline,
            _ => throw new ArgumentOutOfRangeException(nameof(yPositionMode), yPositionMode, null)
        };
    }
}
