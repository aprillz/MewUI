using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit;

/// <summary>A document location together with the visual column it lands on.</summary>
/// <param name="Line">One-based line.</param>
/// <param name="Column">One-based column.</param>
/// <param name="VisualColumn">Visual column, or -1 when it is not known.</param>
/// <param name="IsAtEndOfLine">
/// Which side of a wrap this position is on. Where a line wraps without a space, the end of one row
/// and the start of the next share an offset and a visual column; true is the earlier row. Has no
/// effect at any other position.
/// </param>
public readonly record struct TextViewPosition(int Line, int Column, int VisualColumn, bool IsAtEndOfLine)
    : IComparable<TextViewPosition>
{
    /// <summary>Initializes a position at a line and column with a known visual column.</summary>
    public TextViewPosition(int line, int column, int visualColumn)
        : this(line, column, visualColumn, false)
    {
    }

    /// <summary>Initializes a position at a line and column; the visual column is unknown (-1).</summary>
    public TextViewPosition(int line, int column)
        : this(line, column, -1, false)
    {
    }

    /// <summary>Initializes a position at a document location with a known visual column.</summary>
    public TextViewPosition(TextLocation location, int visualColumn)
        : this(location.Line, location.Column, visualColumn, false)
    {
    }

    /// <summary>Initializes a position at a document location; the visual column is unknown (-1).</summary>
    public TextViewPosition(TextLocation location)
        : this(location, -1)
    {
    }

    /// <summary>Gets the line and column as a <see cref="TextLocation"/>.</summary>
    public TextLocation Location => new(Line, Column);

    /// <inheritdoc/>
    public override string ToString()
        => $"[TextViewPosition Line={Line} Column={Column} VisualColumn={VisualColumn} IsAtEndOfLine={IsAtEndOfLine}]";

    /// <summary>Orders by location, then visual column, then the end of a wrap before its start.</summary>
    public int CompareTo(TextViewPosition other)
    {
        int result = Location.CompareTo(other.Location);
        if (result != 0)
        {
            return result;
        }
        result = VisualColumn.CompareTo(other.VisualColumn);
        if (result != 0)
        {
            return result;
        }
        if (IsAtEndOfLine == other.IsAtEndOfLine)
        {
            return 0;
        }
        return IsAtEndOfLine ? -1 : 1;
    }
}
