using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Controls.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A single-line text input control.
/// </summary>
public class TextBox : TextBase
{
    private readonly TextBoxView _view = new();

    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

    public TextBox()
    {
        BorderThickness = 1;
        Padding = new Thickness(4);
    }

    protected override Rect GetInteractionContentBounds()
        // Keep interaction bounds consistent with rendering bounds (border inset + padding).
        => GetViewportContentBounds();

    protected override string NormalizePastedText(string text)
    {
        text ??= string.Empty;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        // Single-line: preserve separation by converting newlines/tabs to spaces.
        if (text.IndexOf('\r') >= 0 || text.IndexOf('\n') >= 0)
        {
            text = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }

        if (!AcceptTab && text.IndexOf('\t') >= 0)
        {
            text = text.Replace('\t', ' ');
        }

        return text;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // Keep default sizing stable, but use actual font metrics and account for border inset.
        var borderInset = GetBorderVisualInset();

        using var measure = BeginTextMeasurement();
        var metrics = measure.Context.MeasureText("Mg", measure.Font);
        var lineHeight = Math.Max(FontSize, metrics.Height);

        var sample = Text.AsSpan();
        if (sample.IsEmpty)
        {
            sample = Placeholder.AsSpan();
        }

        if (sample.Length > 64)
        {
            sample = sample[..64];
        }

        double sampleWidth = sample.IsEmpty
            ? measure.Context.MeasureText("MMMMMMMMMM", measure.Font).Width
            : measure.Context.MeasureText(sample, measure.Font).Width;

        double chromeW = Padding.HorizontalThickness + borderInset * 2;
        double chromeH = Padding.VerticalThickness + borderInset * 2;

        double desiredW = Math.Max(16, sampleWidth + chromeW + 4);
        double desiredH = Math.Max(lineHeight + chromeH + 4, MinHeight > 0 ? MinHeight : 0);

        return new Size(desiredW, desiredH);
    }

    protected override void RenderTextContent(IGraphicsContext context, Rect contentBounds, IFont font, Theme theme, in VisualState state)
    {
        var (selStart, selEnd) = GetSelectionRange();
        _view.Render(
            context,
            contentBounds,
            font,
            theme,
            state.IsEnabled,
            state.IsFocused,
            IsReadOnly,
            Foreground,
            HorizontalOffset,
            FontFamily,
            FontSize,
            FontWeight,
            GetDpi(),
            DocumentVersion,
            CaretPosition,
            HasSelection,
            selStart,
            selEnd,
            Document.Length,
            (buffer, start, length) => Document.CopyTo(buffer, start, length));
    }

    protected override void SetCaretFromPoint(Point point, Rect contentBounds)
    {
        var clickX = point.X - contentBounds.X + HorizontalOffset;
        CaretPosition = GetCharacterIndexFromX(clickX);
    }

    protected override void AutoScrollForSelectionDrag(Point point, Rect contentBounds)
    {
        // If the pointer goes beyond the text box, scroll in that direction.
        // This matches common native text box behavior and enables selecting off-screen text.
        const double edgeDip = 8;
        if (point.X < contentBounds.X + edgeDip)
        {
            SetHorizontalOffset(HorizontalOffset + point.X - (contentBounds.X + edgeDip), false);
        }
        else if (point.X > contentBounds.Right - edgeDip)
        {
            SetHorizontalOffset(HorizontalOffset + point.X - (contentBounds.Right - edgeDip), false);
        }

        ClampScrollOffset();
    }

    protected override void EnsureCaretVisibleCore(Rect contentBounds) => EnsureCaretVisible(contentBounds);

    private int GetCharacterIndexFromX(double x)
    {
        using var measure = BeginTextMeasurement();
        return _view.GetCaretIndexFromX(
            x,
            measure.Context,
            measure.Font,
            FontFamily,
            FontSize,
            FontWeight,
            GetDpi(),
            DocumentVersion,
            Document.Length,
            (buffer, start, length) => Document.CopyTo(buffer, start, length));
    }

    private void EnsureCaretVisible(Rect contentBounds)
    {
        using var measure = BeginTextMeasurement();
        double newOffset = _view.EnsureCaretVisible(
            measure.Context,
            measure.Font,
            FontFamily,
            FontSize,
            FontWeight,
            GetDpi(),
            DocumentVersion,
            Document.Length,
            CaretPosition,
            HorizontalOffset,
            contentBounds.Width,
            Padding.Right,
            (buffer, start, length) => Document.CopyTo(buffer, start, length));
        SetHorizontalOffset(newOffset, false);
    }

    private void ClampScrollOffset(IGraphicsContext context, IFont font, double viewportWidthDip)
    {
        double newOffset = _view.ClampScrollOffset(
            context,
            font,
            FontFamily,
            FontSize,
            FontWeight,
            GetDpi(),
            DocumentVersion,
            Document.Length,
            HorizontalOffset,
            viewportWidthDip,
            Padding.Right,
            (buffer, start, length) => Document.CopyTo(buffer, start, length));
        SetHorizontalOffset(newOffset, false);
    }

    private void ClampScrollOffset()
    {
        var contentBounds = GetInteractionContentBounds();
        using var measure = BeginTextMeasurement();
        ClampScrollOffset(measure.Context, measure.Font, contentBounds.Width);
    }
}
