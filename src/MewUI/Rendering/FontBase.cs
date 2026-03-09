namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Base class for <see cref="IFont"/> implementations. Stores common font
/// properties and provides derived metric helpers.
/// </summary>
internal abstract class FontBase : IFont
{
    public string Family { get; }
    public double Size { get; }
    public FontWeight Weight { get; }
    public bool IsItalic { get; }
    public bool IsUnderline { get; }
    public bool IsStrikethrough { get; }
    public double Ascent { get; protected set; }
    public double Descent { get; protected set; }
    public double InternalLeading { get; protected set; }

    protected FontBase(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        Family = family;
        Size = size;
        Weight = weight;
        IsItalic = italic;
        IsUnderline = underline;
        IsStrikethrough = strikethrough;
    }

    public virtual void Dispose() { }
}
