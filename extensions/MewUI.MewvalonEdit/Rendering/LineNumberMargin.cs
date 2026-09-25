using System.Globalization;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Draws the document line numbers beside the text, in the inherited <c>Foreground</c> that
/// <c>TextEditor.LineNumbersForeground</c> assigns.
/// </summary>
public sealed class LineNumberMargin : AbstractMargin
{
    private const double LEFT_INSET = 4;
    private const double RIGHT_INSET = 6;
    private const int MIN_DIGITS = 2;

    private int _measuredDigits = -1;

    /// <summary>Re-measures when the document grows or shrinks past a digit boundary.</summary>
    public void SyncWidthToLineCount()
    {
        if (GetDigitCount() != _measuredDigits)
        {
            InvalidateMeasure();
        }
    }

    protected override void OnDocumentChanged(Document.TextDocument? oldValue, Document.TextDocument? newValue)
        => SyncWidthToLineCount();

    protected override Size MeasureContent(Size availableSize)
    {
        _measuredDigits = GetDigitCount();
        double width = MeasureNumberWidth(new string('9', _measuredDigits));
        return new Size(width + LEFT_INSET + RIGHT_INSET, Math.Max(1, availableSize.Height));
    }

    protected override void OnRenderTextViewport(IGraphicsContext context, Rect textViewport)
    {
        if (TextView is not { } view)
        {
            return;
        }

        double scrollY = view.Host.ScrollOffset.Y;
        foreach (var line in view.Host.VisibleTextLines)
        {
            DrawLineNumber(context, line.LogicalLine.LineNumber + 1, textViewport.Y + line.DocumentY - scrollY);
        }
    }

    /// <summary>
    /// Draws a one-based line number the way this margin draws it beside its line, with its top at
    /// <paramref name="top"/>, for places that show a line away from its own row.
    /// </summary>
    public void DrawLineNumber(IGraphicsContext context, int lineNumber, double top)
    {
        ArgumentNullException.ThrowIfNull(context);
        var layout = GetNumberLayout(lineNumber.ToString(CultureInfo.InvariantCulture));
        double x = Math.Max(Bounds.X + LEFT_INSET, Bounds.Right - layout.MeasuredSize.Width - RIGHT_INSET);
        var options = new TextDrawOptions(Foreground);
        context.Text.Draw(layout, new Point(x, top), in options);
    }

    private int GetDigitCount()
    {
        int lineCount = TextView?.Host.Document.LineCount ?? 1;
        return Math.Max(MIN_DIGITS, lineCount.ToString(CultureInfo.InvariantCulture).Length);
    }

    // Measures the widest digits rather than estimating from font size, so proportional fonts
    // and high DPI do not clip the number.
    private double MeasureNumberWidth(string sample) => GetNumberLayout(sample).MeasuredSize.Width;

    private ITextLayout GetNumberLayout(string number)
        => GetGraphicsFactory().TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = number.AsMemory(),
                Dpi = GetDpi(),
                DefaultStyle = new TextRunStyle(FontFamily, FontSize, FontWeight),
                Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.NoWrap }
            },
            TextLayoutCachePolicy.Content);
}
