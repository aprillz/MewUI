namespace Aprillz.MewUI.Markdown;

/// <summary>A realized text element that takes part in the document selection owned by the presenter.</summary>
internal interface ISelectableText
{
    /// <summary>Index into <see cref="ParsedMarkdown.TextUnits"/>, or -1 when the element has no unit.</summary>
    int TextUnit { get; set; }

    /// <summary>Length of the laid-out text, which matches the unit text.</summary>
    int TextLength { get; }

    /// <summary>Layout bounds in window coordinates.</summary>
    Rect Bounds { get; }

    /// <summary>Nearest insertion offset for a window point; points above or below clamp to the ends.</summary>
    int OffsetAt(Point windowPoint);

    /// <summary>Word boundaries around an offset.</summary>
    (int Start, int End) WordAt(int offset);

    /// <summary>Highlights the range; an empty or inverted range clears it.</summary>
    void SetSelection(int start, int end);
}
