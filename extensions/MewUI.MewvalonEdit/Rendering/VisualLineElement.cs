using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>Cursor query an element answers while the pointer is over it.</summary>
public sealed class QueryCursorEventArgs(Point position, ModifierKeys modifiers)
{
    public Point Position { get; } = position;
    public ModifierKeys Modifiers { get; } = modifiers;

    /// <summary>Cursor to show. Leave null to keep the editor's own text cursor.</summary>
    public CursorType? Cursor { get; set; }
}

/// <summary>Font selection of a text run. Replaces WPF's Typeface.</summary>
public sealed record Typeface
{
    public Typeface(string fontFamily, FontWeight weight = FontWeight.Normal, bool italic = false)
    {
        // A run with no family cannot be measured, and the failure surfaces a frame later in the
        // text engine rather than at whoever asked for it.
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        FontFamily = fontFamily;
        Weight = weight;
        Italic = italic;
    }

    public string FontFamily { get; }
    public FontWeight Weight { get; }
    public bool Italic { get; }
}

/// <summary>
/// Paint and font overrides a transformer applies to a range. Mirrors AvalonEdit's type of the same
/// name; brush parameters are <see cref="Color"/> following the MewUI convention.
/// </summary>
public sealed class VisualLineElementTextRunProperties
{
    public Color? ForegroundBrush { get; private set; }
    public Color? BackgroundBrush { get; private set; }
    public string? FontFamily { get; private set; }
    public double? FontRenderingEmSize { get; private set; }
    public FontWeight? FontWeight { get; private set; }
    public bool? Italic { get; private set; }
    public TextDecoration TextDecorations { get; private set; }

    public void SetForegroundBrush(Color value) => ForegroundBrush = value;

    public void SetBackgroundBrush(Color value) => BackgroundBrush = value;

    public void SetFontRenderingEmSize(double value) => FontRenderingEmSize = value;

    public void SetTextDecorations(TextDecoration value) => TextDecorations = value;

    public void SetTypeface(Typeface value)
    {
        ArgumentNullException.ThrowIfNull(value);
        FontFamily = value.FontFamily;
        FontWeight = value.Weight;
        Italic = value.Italic;
    }

    /// <summary>Overrides the family alone, leaving weight and slant inherited.</summary>
    public void SetFontFamily(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        FontFamily = value;
    }

    /// <inheritdoc cref="SetFontFamily"/>
    public void SetFontWeight(FontWeight value) => FontWeight = value;

    /// <inheritdoc cref="SetFontFamily"/>
    public void SetItalic(bool value) => Italic = value;

    internal bool HasFont => FontFamily is not null || FontRenderingEmSize.HasValue || FontWeight.HasValue || Italic.HasValue;
}

/// <summary>
/// A range of a visual line. Transformers restyle one through <see cref="TextRunProperties"/>;
/// element generators produce one that measures and draws itself in place of the document text.
/// </summary>
public class VisualLineElement
{
    protected VisualLineElement(int visualLength, int documentLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(visualLength, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(documentLength);
        VisualLength = visualLength;
        DocumentLength = documentLength;
    }

    public int VisualLength { get; }

    public int DocumentLength { get; }

    /// <summary>Offset from the start of the visual line. Assigned while the line is built.</summary>
    public int RelativeTextOffset { get; internal set; }

    /// <summary>
    /// Where the element starts on the visual surface. Later than <see cref="RelativeTextOffset"/>
    /// once an earlier element stands more columns in for its text. Assigned once the line's
    /// projection is known, so it is not yet valid inside
    /// <see cref="VisualLineElementGenerator.ConstructElement"/>.
    /// </summary>
    public int VisualColumn { get; internal set; }

    public VisualLineElementTextRunProperties TextRunProperties { get; } = new();

    /// <summary>Background painted behind the range. Equivalent to setting it through <see cref="TextRunProperties"/>.</summary>
    public Color? BackgroundBrush { get; set; }

    /// <summary>Colour of the range. Equivalent to setting it through <see cref="TextRunProperties"/>.</summary>
    public Color? Foreground { get; set; }

    /// <summary>
    /// True when the element paints in place of the document text, making the range one indivisible
    /// unit; false decorates the text and leaves every caret position inside it.
    /// </summary>
    protected internal virtual bool ReplacesText => true;

    /// <summary>
    /// Visual columns this element paints itself, counted from its start. The columns beyond it are
    /// laid out from <see cref="GetVisualText"/> as ordinary text, which is how the tab marker paints
    /// a glyph and still lets a real tab reach its tab stop. Ignored unless <see cref="ReplacesText"/>.
    /// </summary>
    protected internal virtual int PaintedVisualLength => VisualLength;

    /// <summary>
    /// Whether a line may break after this element. An element that stands in for whitespace has to
    /// say so: it paints its own columns, and the breaker no longer sees the space it replaced.
    /// AvalonEdit says the same through the break conditions of its text run.
    /// </summary>
    protected internal virtual bool BreaksLine => false;

    /// <summary>
    /// Called before the element is painted, for appearance the view owns rather than the element.
    /// </summary>
    /// <summary>
    /// Next caret stop at or past <paramref name="visualColumn"/> inside this element, or -1 when it
    /// offers none. The default makes an element atomic: the caret stops at its edges and never
    /// inside it, which is what keeps it out of a folded region's placeholder.
    /// </summary>
    public virtual int GetNextCaretPosition(
        int visualColumn, LogicalDirection direction, CaretPositioningMode mode)
    {
        int start = VisualColumn;
        int end = VisualColumn + VisualLength;
        bool stopsAtEnds = mode is not CaretPositioningMode.WordStart
            and not CaretPositioningMode.WordStartOrSymbol;
        if (direction == LogicalDirection.Backward)
        {
            if (visualColumn > end && stopsAtEnds)
            {
                return end;
            }
            if (visualColumn > start)
            {
                return start;
            }
        }
        else
        {
            if (visualColumn < start)
            {
                return start;
            }
            if (visualColumn < end && stopsAtEnds)
            {
                return end;
            }
        }
        return -1;
    }

    protected internal virtual void PrepareForPaint(TextView textView)
    {
    }

    /// <summary>
    /// Size this element occupies, measured at the density the view lays out at. Generated elements
    /// override it; a restyled range keeps the document text.
    /// </summary>
    public virtual InlineMetrics Measure(uint dpi) => default;

    /// <summary>Paints this element. Generated elements override it.</summary>
    public virtual void Draw(ITextRenderContext context, Point origin, uint dpi)
    {
    }

    /// <summary>
    /// Text this element occupies on the visual surface. Differs from the document text only when
    /// <see cref="VisualLength"/> and <see cref="DocumentLength"/> differ; the default fills with
    /// object replacement characters since the element paints over them anyway.
    /// </summary>
    protected internal virtual string GetVisualText() => new('￼', VisualLength);

    /// <summary>
    /// Called when the pointer is pressed over this element, before the editor moves the caret.
    /// Setting <see cref="MouseEventArgs.Handled"/> claims the press and skips caret placement.
    /// </summary>
    protected internal virtual void OnMouseDown(MouseEventArgs e)
    {
    }

    /// <summary>Called while the pointer is over this element to pick the cursor.</summary>
    protected internal virtual void OnQueryCursor(QueryCursorEventArgs e)
    {
    }
}

/// <summary>Range whose only purpose is to carry a transformer's overrides.</summary>
internal sealed class StyleOverrideElement : VisualLineElement
{
    public StyleOverrideElement(int relativeTextOffset, int length) : base(Math.Max(1, length), length)
        => RelativeTextOffset = relativeTextOffset;
}
