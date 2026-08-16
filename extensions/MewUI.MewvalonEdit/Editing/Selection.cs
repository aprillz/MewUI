using System.Text;
using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit.Editing;

/// <summary>
/// A selection of a text area. A selection is a value: changing one produces another rather than
/// mutating this one, which is what lets a caller hold one across an edit and compare the two.
/// </summary>
public abstract class Selection
{
    protected Selection(TextArea textArea)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        TextArea = textArea;
    }

    protected TextArea TextArea { get; }

    /// <summary>Selects from one offset to another. Equal offsets give the empty selection.</summary>
    public static Selection Create(TextArea textArea, int startOffset, int endOffset)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        return startOffset == endOffset
            ? textArea.EmptySelection
            : new SimpleSelection(
                textArea,
                new TextViewPosition(textArea.Document.GetLocation(startOffset)),
                new TextViewPosition(textArea.Document.GetLocation(endOffset)));
    }

    public static Selection Create(TextArea textArea, ISegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return Create(textArea, segment.Offset, segment.EndOffset);
    }

    internal static Selection Create(TextArea textArea, TextViewPosition start, TextViewPosition end)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        var document = textArea.Document;
        return document.GetOffset(start.Line, start.Column) == document.GetOffset(end.Line, end.Column)
            && start.VisualColumn == end.VisualColumn
                ? textArea.EmptySelection
                : new SimpleSelection(textArea, start, end);
    }

    public abstract TextViewPosition StartPosition { get; }

    public abstract TextViewPosition EndPosition { get; }

    /// <summary>The selected ranges. One for a simple selection, none when empty.</summary>
    public abstract IEnumerable<SelectionSegment> Segments { get; }

    /// <summary>Smallest range containing every segment, or null when the selection is empty.</summary>
    public abstract ISegment? SurroundingSegment { get; }

    public abstract int Length { get; }

    /// <summary>Replaces the selected text, leaving the caret where the replacement ends.</summary>
    public abstract void ReplaceSelectionWithText(string newText);

    /// <summary>
    /// Prefixes the text with the spaces a virtual-space position implies: writing at a column past
    /// the line end first has to create the columns in between. Tabs fill first on a line that is
    /// tabs only and keeps them.
    /// </summary>
    internal string AddSpacesIfRequired(string newText, TextViewPosition start, TextViewPosition end)
    {
        if (EnableVirtualSpace && InsertVirtualSpaces(newText, start, end))
        {
            var line = TextArea.Document.GetLineByNumber(start.Line);
            string lineText = TextArea.Document.GetText(line.Offset, line.Length);
            var visualLine = TextArea.TextView.GetOrConstructVisualLine(line);
            if (visualLine is null)
            {
                return newText;
            }
            // Measured to the end of the text, not of the laid-out line: the end-of-line marker
            // holds a column now but moves along with what is typed, so counting it would leave the
            // text one column short of where the caller aimed.
            int columnDiff = start.VisualColumn - visualLine.VisualLength;
            if (columnDiff > 0)
            {
                string additionalSpaces = string.Empty;
                // An empty line has no indentation to continue, so it fills with spaces like a line
                // that holds text does.
                if (!TextArea.Options.ConvertTabsToSpaces
                    && lineText.Length > 0
                    && lineText.Trim('\t').Length == 0)
                {
                    int tabCount = columnDiff / TextArea.Options.IndentationSize;
                    additionalSpaces = new string('\t', tabCount);
                    columnDiff -= tabCount * TextArea.Options.IndentationSize;
                }
                additionalSpaces += new string(' ', columnDiff);
                return additionalSpaces + newText;
            }
        }
        return newText;
    }

    private bool InsertVirtualSpaces(string newText, TextViewPosition start, TextViewPosition end)
        => (!string.IsNullOrEmpty(newText) || !(IsInVirtualSpace(start) && IsInVirtualSpace(end)))
            && newText != "\r\n"
            && newText != "\n"
            && newText != "\r";

    private bool IsInVirtualSpace(TextViewPosition position)
    {
        var visualLine = TextArea.TextView.GetOrConstructVisualLine(
            TextArea.Document.GetLineByNumber(position.Line));
        return visualLine is not null && position.VisualColumn > visualLine.VisualLength;
    }

    /// <summary>The selection this one becomes after the change, with its ends carried across it.</summary>
    public abstract Selection UpdateOnDocumentChange(DocumentChangeEventArgs e);

    /// <summary>The same selection with a different end.</summary>
    /// <exception cref="NotSupportedException">The selection is empty, so it has no end to move.</exception>
    public abstract Selection SetEndpoint(TextViewPosition endPosition);

    /// <summary>
    /// Moves the end when there is a selection, and starts one from <paramref name="startPosition"/>
    /// when there is not.
    /// </summary>
    public abstract Selection StartSelectionOrSetEndpoint(
        TextViewPosition startPosition, TextViewPosition endPosition);

    /// <summary>
    /// Whether this selection reaches past the end of a line. It follows the option, except for a
    /// rectangular selection, which spans columns and so needs virtual space whatever it says.
    /// </summary>
    public virtual bool EnableVirtualSpace => TextArea.Options.EnableVirtualSpace;

    public virtual bool IsEmpty => Length == 0;

    /// <summary>Whether the selection reaches across more than one line.</summary>
    public virtual bool IsMultiline
    {
        get
        {
            if (SurroundingSegment is not ISegment segment)
            {
                return false;
            }
            var document = TextArea.Document;
            return document.GetLineByOffset(segment.Offset).LineNumber
                != document.GetLineByOffset(segment.Offset + segment.Length).LineNumber;
        }
    }

    /// <summary>The selected text, segments joined in order.</summary>
    public virtual string GetText()
    {
        var document = TextArea.Document;
        // One segment is the common case and needs no builder, which is worth keeping for a
        // selection the size of a document.
        StringBuilder? builder = null;
        string first = string.Empty;
        int count = 0;
        foreach (var segment in Segments)
        {
            string text = document.GetText(segment.StartOffset, segment.Length);
            if (count++ == 0)
            {
                first = text;
                continue;
            }
            builder ??= new StringBuilder(first);
            builder.Append(text);
        }
        return builder?.ToString() ?? first;
    }

    /// <summary>Whether the offset falls in the selection, its borders included.</summary>
    public virtual bool Contains(int offset)
    {
        if (IsEmpty || SurroundingSegment is not ISegment surrounding ||
            offset < surrounding.Offset || offset > surrounding.Offset + surrounding.Length)
        {
            return false;
        }
        foreach (var segment in Segments)
        {
            if (offset >= segment.StartOffset && offset <= segment.EndOffset)
            {
                return true;
            }
        }
        return false;
    }

    public abstract override bool Equals(object? obj);

    public abstract override int GetHashCode();
}

/// <summary>The selection of a text area with nothing selected. One instance per text area.</summary>
public sealed class EmptySelection(TextArea textArea) : Selection(textArea)
{
    public override TextViewPosition StartPosition => new(TextLocation.Empty);

    public override TextViewPosition EndPosition => new(TextLocation.Empty);

    public override IEnumerable<SelectionSegment> Segments => [];

    public override ISegment? SurroundingSegment => null;

    public override int Length => 0;

    public override string GetText() => string.Empty;

    public override Selection UpdateOnDocumentChange(DocumentChangeEventArgs e) => this;

    public override void ReplaceSelectionWithText(string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        // Straight to the editor's insertion, not back through PerformTextInput, which routes
        // empty-selection input here. The original edits the document directly at this spot.
        newText = AddSpacesIfRequired(newText, TextArea.Caret.Position, TextArea.Caret.Position);
        if (newText.Length > 0)
        {
            TextArea.Editor.InsertTextInput(newText);
        }
    }

    public override Selection SetEndpoint(TextViewPosition endPosition)
        => throw new NotSupportedException("An empty selection has no endpoint to move.");

    public override Selection StartSelectionOrSetEndpoint(
        TextViewPosition startPosition, TextViewPosition endPosition)
        => Create(TextArea, startPosition, endPosition);

    // One per text area, so identity is the whole comparison.
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

/// <summary>A selection of one continuous range.</summary>
public sealed class SimpleSelection : Selection
{
    private readonly TextViewPosition _start;
    private readonly TextViewPosition _end;
    private readonly int _startOffset;
    private readonly int _endOffset;

    internal SimpleSelection(TextArea textArea, TextViewPosition start, TextViewPosition end)
        : base(textArea)
    {
        _start = start;
        _end = end;
        _startOffset = textArea.Document.GetOffset(start.Line, start.Column);
        _endOffset = textArea.Document.GetOffset(end.Line, end.Column);
    }

    public override TextViewPosition StartPosition => _start;

    public override TextViewPosition EndPosition => _end;

    public override IEnumerable<SelectionSegment> Segments
        => [new SelectionSegment(_startOffset, _start.VisualColumn, _endOffset, _end.VisualColumn)];

    public override ISegment? SurroundingSegment => new SelectionSegment(_startOffset, _endOffset);

    public override int Length => Math.Abs(_endOffset - _startOffset);

    /// <summary>Empty only when both ends land on the same offset and the same visual column.</summary>
    public override bool IsEmpty => _startOffset == _endOffset && _start.VisualColumn == _end.VisualColumn;

    public override void ReplaceSelectionWithText(string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        // Through the editing path rather than the document, so a read-only section still refuses.
        newText = AddSpacesIfRequired(newText, _start, _end);
        TextArea.Selection = this;
        TextArea.ReplaceSelection(newText);
    }

    /// <summary>
    /// The start moves as an insertion at it pushes it along; the end stays put, so text typed at
    /// the end of a selection lands outside it rather than joining it.
    /// </summary>
    public override Selection UpdateOnDocumentChange(DocumentChangeEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        int newStartOffset;
        int newEndOffset;
        if (_startOffset <= _endOffset)
        {
            newStartOffset = e.GetNewOffset(_startOffset, AnchorMovementType.Default);
            newEndOffset = Math.Max(newStartOffset, e.GetNewOffset(_endOffset, AnchorMovementType.BeforeInsertion));
        }
        else
        {
            newEndOffset = e.GetNewOffset(_endOffset, AnchorMovementType.Default);
            newStartOffset = Math.Max(newEndOffset, e.GetNewOffset(_startOffset, AnchorMovementType.BeforeInsertion));
        }
        var document = TextArea.Document;
        return Create(
            TextArea,
            new TextViewPosition(document.GetLocation(newStartOffset), _start.VisualColumn),
            new TextViewPosition(document.GetLocation(newEndOffset), _end.VisualColumn));
    }

    public override Selection SetEndpoint(TextViewPosition endPosition)
        => Create(TextArea, _start, endPosition);

    public override Selection StartSelectionOrSetEndpoint(
        TextViewPosition startPosition, TextViewPosition endPosition)
        => Create(TextArea, _start, endPosition);

    public override bool Equals(object? obj)
        => obj is SimpleSelection other
            && _start.Equals(other._start)
            && _end.Equals(other._end)
            && _startOffset == other._startOffset
            && _endOffset == other._endOffset
            && ReferenceEquals(TextArea, other.TextArea);

    public override int GetHashCode() => HashCode.Combine(_startOffset, _endOffset, TextArea);

    public override string ToString() => $"[SimpleSelection Start={_start} End={_end}]";
}
