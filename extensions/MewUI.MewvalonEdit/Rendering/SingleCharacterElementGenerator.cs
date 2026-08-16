using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Builds the elements that stand in for single characters: a dot for a space, a guillemet over a
/// tab, and a box naming a control character. One generator decides all three because a tab is
/// itself a control character, so which marker wins has to be settled in one place.
/// </summary>
public sealed class SingleCharacterElementGenerator : VisualLineElementGenerator, IBuiltinElementGenerator
{
    private const char SPACE_MARKER = '·';
    private const char TAB_MARKER = '»';
    private const char END_OF_LINE_MARKER = '¶';

    /// <summary>Marks a space with a dot.</summary>
    public bool ShowSpaces { get; set; } = true;

    /// <summary>Marks a tab with a guillemet, keeping the tab and its stop.</summary>
    public bool ShowTabs { get; set; } = true;

    /// <summary>Marks the end of a line that has one after it.</summary>
    public bool ShowEndOfLine { get; set; } = true;

    /// <summary>Boxes a control character with its name.</summary>
    public bool ShowBoxForControlCharacters { get; set; } = true;

    void IBuiltinElementGenerator.FetchOptions(TextEditorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ShowSpaces = options.ShowSpaces;
        ShowTabs = options.ShowTabs;
        ShowEndOfLine = options.ShowEndOfLine;
        ShowBoxForControlCharacters = options.ShowBoxForControlCharacters;
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var context = CurrentContext;
        if (context is null)
        {
            return -1;
        }
        var line = context.CurrentDocumentLine;
        int end = line.Offset + line.Length;
        for (int offset = startOffset; offset < end; offset++)
        {
            if (WantsCharacter(context.Document.GetCharAt(offset)))
            {
                return offset;
            }
        }
        // The end-of-line marker stands at the line end rather than over a character, so it is asked
        // for at the one offset the character scan above cannot reach.
        return startOffset <= end && WantsEndOfLine(line) ? end : -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var context = CurrentContext;
        if (context is null)
        {
            return null;
        }
        var style = context.DefaultStyle;
        var currentLine = context.CurrentDocumentLine;
        if (offset == currentLine.Offset + currentLine.Length)
        {
            return WantsEndOfLine(currentLine)
                ? new EndOfLineMarkerElement(END_OF_LINE_MARKER.ToString(), style)
                : null;
        }
        char character = context.Document.GetCharAt(offset);
        if (character == ' ' && ShowSpaces)
        {
            return new WhitespaceMarkerElement(SPACE_MARKER.ToString(), " ", style);
        }
        if (character == '\t' && ShowTabs)
        {
            return new TabMarkerElement(TAB_MARKER.ToString(), style);
        }
        if (ShowBoxForControlCharacters && char.IsControl(character))
        {
            return new ControlCharacterBoxElement(TextUtilities.GetControlCharacterName(character), style);
        }
        return null;
    }

    /// <summary>
    /// A tab is a control character, but the original settles the tab case before the box is
    /// reached, so a tab is never boxed and turning tab markers off leaves it unmarked.
    /// </summary>
    private bool WantsCharacter(char character) => character switch
    {
        ' ' => ShowSpaces,
        '\t' => ShowTabs,
        _ => ShowBoxForControlCharacters && char.IsControl(character)
    };

    // The last line of a document has no line after it, so it has no line end to mark.
    private bool WantsEndOfLine(DocumentLine line) => ShowEndOfLine && line.NextLine is not null;
}

/// <summary>
/// The end-of-line marker, standing at the line end rather than over a character: one visual column
/// for no document text. The column is what keeps virtual space starting after the glyph instead of
/// on top of it.
/// </summary>
internal sealed class EndOfLineMarkerElement(string glyph, TextRunStyle style)
    : VisualLineElement(1, 0)
{
    protected internal override string GetVisualText() => glyph;

    protected internal override void PrepareForPaint(TextView textView)
    {
        ArgumentNullException.ThrowIfNull(textView);
        Foreground = textView.ResolvedNonPrintableCharacter;
    }

    public override InlineMetrics Measure(uint dpi)
    {
        var layout = MarkerLayout.For(glyph, style, dpi);
        return new InlineMetrics(
            layout.MeasuredSize.Width,
            layout.MeasuredSize.Height,
            layout.Lines[0].Baseline);
    }

    public override void Draw(ITextRenderContext context, Point origin, uint dpi)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = new TextDrawOptions(Foreground ?? Color.FromRgb(0x80, 0x80, 0x80));
        context.Draw(MarkerLayout.For(glyph, style, dpi), origin, in options);
    }
}

/// <summary>
/// A character drawn as a marker glyph in its place, as the original's space element does. It takes
/// the width of the character it stands in for rather than its own: a marker that measured itself
/// would move the rest of the line whenever it was turned on, by however much the two glyphs round
/// apart at the current density.
/// </summary>
internal sealed class WhitespaceMarkerElement(string glyph, string replaced, TextRunStyle style)
    : VisualLineElement(1, 1)
{
    protected internal override string GetVisualText() => glyph;

    protected internal override void PrepareForPaint(TextView textView)
    {
        ArgumentNullException.ThrowIfNull(textView);
        Foreground = textView.ResolvedNonPrintableCharacter;
    }

    /// <summary>The space it stands in for is where a line breaks, and it still is.</summary>
    protected internal override bool BreaksLine => replaced == " ";

    public override InlineMetrics Measure(uint dpi)
    {
        var layout = MarkerLayout.For(glyph, style, dpi);
        return new InlineMetrics(
            MarkerLayout.For(replaced, style, dpi).MeasuredSize.Width,
            layout.MeasuredSize.Height,
            layout.Lines[0].Baseline);
    }

    public override void Draw(ITextRenderContext context, Point origin, uint dpi)
    {
        ArgumentNullException.ThrowIfNull(context);
        var layout = MarkerLayout.For(glyph, style, dpi);
        double cell = MarkerLayout.For(replaced, style, dpi).MeasuredSize.Width;
        var options = new TextDrawOptions(Foreground ?? Color.FromRgb(0x80, 0x80, 0x80));
        // Centred in the cell it stands in for, so a narrower glyph does not sit against its left edge.
        context.Draw(
            layout,
            new Point(origin.X + ((cell - layout.MeasuredSize.Width) / 2), origin.Y),
            in options);
    }
}

/// <summary>
/// A tab shown as a marker glyph that keeps the tab. Two visual columns stand for the one document
/// character: the first is this element, reporting no width so the tab that follows it starts where
/// the tab did and still reaches its tab stop; the second is the tab itself. The original arranges
/// it the same way, as a zero-width glyph run followed by the tab character.
/// </summary>
internal sealed class TabMarkerElement(string glyph, TextRunStyle style) : VisualLineElement(2, 1)
{
    protected internal override string GetVisualText() => "￼\t";

    protected internal override int PaintedVisualLength => 1;

    protected internal override void PrepareForPaint(TextView textView)
    {
        ArgumentNullException.ThrowIfNull(textView);
        Foreground = textView.ResolvedNonPrintableCharacter;
    }

    public override InlineMetrics Measure(uint dpi)
    {
        var layout = MarkerLayout.For(glyph, style, dpi);
        return new InlineMetrics(0, layout.MeasuredSize.Height, layout.Lines[0].Baseline);
    }

    public override void Draw(ITextRenderContext context, Point origin, uint dpi)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = new TextDrawOptions(Foreground ?? Color.FromRgb(0x80, 0x80, 0x80));
        context.Draw(MarkerLayout.For(glyph, style, dpi), origin, in options);
    }
}

/// <summary>
/// A control character drawn as its name inside a rounded box, which is how the original makes an
/// otherwise invisible character visible without letting it look like ordinary text.
/// </summary>
internal sealed class ControlCharacterBoxElement(string name, TextRunStyle style) : VisualLineElement(1, 1)
{
    private const double HORIZONTAL_PADDING = 3.0;
    private const double CORNER_RADIUS = 2.5;
    private static readonly Color _boxColor = Color.FromArgb(200, 128, 128, 128);
    private static readonly Color _nameColor = Color.FromRgb(255, 255, 255);

    protected internal override string GetVisualText() => name;

    public override InlineMetrics Measure(uint dpi)
    {
        var layout = MarkerLayout.For(name, style, dpi);
        return new InlineMetrics(
            layout.MeasuredSize.Width + HORIZONTAL_PADDING,
            layout.MeasuredSize.Height,
            layout.Lines[0].Baseline);
    }

    public override void Draw(ITextRenderContext context, Point origin, uint dpi)
    {
        ArgumentNullException.ThrowIfNull(context);
        var layout = MarkerLayout.For(name, style, dpi);
        var box = new Rect(
            origin.X,
            origin.Y,
            layout.MeasuredSize.Width + HORIZONTAL_PADDING,
            layout.MeasuredSize.Height);
        context.Graphics.FillRoundedRectangle(box, CORNER_RADIUS, CORNER_RADIUS, _boxColor);
        var options = new TextDrawOptions(_nameColor);
        context.Draw(layout, new Point(origin.X + (HORIZONTAL_PADDING / 2), origin.Y), in options);
    }
}

internal static class MarkerLayout
{
    private static readonly TextParagraphStyle _paragraph = new()
    {
        Wrapping = TextWrapping.NoWrap,
        MaxWidth = double.PositiveInfinity
    };

    public static ITextLayout For(string text, TextRunStyle style, uint dpi)
    {
        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        return factory.TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = text.AsMemory(),
                Dpi = dpi,
                DefaultStyle = style,
                Paragraph = _paragraph
            },
            TextLayoutCachePolicy.Content);
    }
}
