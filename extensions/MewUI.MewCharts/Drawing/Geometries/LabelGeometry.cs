using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A text label geometry (axis labels, data labels, titles), honoring <see cref="Padding"/>.</summary>
public class LabelGeometry : BaseLabelGeometry, IDrawnElement<MewDrawingContext>
{
    public override LvcSize Measure()
    {
        var text = MewChartsText.Measure(Text, TextSize);
        var width = (float)text.Width + Padding.Left + Padding.Right;
        var height = (float)text.Height + Padding.Top + Padding.Bottom;
        return new LvcSize(width, height).GetRotatedSize(RotateTransform);
    }

    public virtual void Draw(MewDrawingContext context)
    {
        if (string.IsNullOrEmpty(Text)) return;

        var font = MewChartsText.GetFont(TextSize);
        if (font is null) return;

        var text = MewChartsText.Measure(Text, TextSize);
        var width = text.Width + Padding.Left + Padding.Right;
        var height = text.Height + Padding.Top + Padding.Bottom;

        // Align the padded box around (X, Y), then inset the text by the padding.
        var left = HorizontalAlign switch
        {
            Align.Start => (double)X,
            Align.End => X - width,
            _ => X - width / 2,
        };

        var top = VerticalAlign switch
        {
            Align.Start => (double)Y,
            Align.End => Y - height,
            _ => Y - height / 2,
        };

        var textBounds = new Rect(left + Padding.Left, top + Padding.Top, text.Width, text.Height);
        context.G.DrawText(Text, textBounds, font, context.ActiveColor);
    }
}
