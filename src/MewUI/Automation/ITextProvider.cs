using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Automation;

/// <summary>A span of text a reader moves by.</summary>
public enum TextUnit
{
    Character,
    Word,
    Line,
    Paragraph,
    Document,
}

/// <summary>A control whose text a reader moves through by unit, with a caret and a selection.</summary>
public interface ITextProvider
{
    /// <summary>The whole text.</summary>
    string Text { get; }

    /// <summary>The caret's offset in <see cref="Text"/>, or -1 when the control shows no caret.</summary>
    int CaretOffset { get; }

    /// <summary>The selected ranges; empty when nothing is selected.</summary>
    IReadOnlyList<TextRange> GetSelection();

    /// <summary>The <paramref name="unit"/> of text that encloses <paramref name="offset"/>.</summary>
    TextRange GetUnitAt(int offset, TextUnit unit);

    /// <summary>The bounds of the given characters in the window's client area, in DIPs, one rectangle per line.</summary>
    IReadOnlyList<Rect> GetBounds(int start, int length);

    /// <summary>The offset of the character nearest <paramref name="clientPoint"/>, a point in the window's client area.</summary>
    int GetOffsetAt(Point clientPoint);

    /// <summary>Selects the given characters; throws when the control cannot select.</summary>
    void Select(int start, int length);
}
