using LiveChartsCore.Drawing;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A text label geometry (axis labels, data labels, titles), honoring <see cref="Padding"/>.</summary>
public class LabelGeometry : BaseLabelGeometry, IDrawnElement<MewDrawingContext>
{
    /// <summary>Measures the text plus padding, rotated by the label rotation.</summary>
    public override LvcSize Measure()
    {
        var text = MewChartsText.Measure(Text, TextSize);
        var width = (float)text.Width + Padding.Left + Padding.Right;
        var height = (float)text.Height + Padding.Top + Padding.Bottom;
        return new LvcSize(width, height).GetRotatedSize(RotateTransform);
    }

    /// <summary>Draws the text aligned around the label position and inset by its padding; empty text draws nothing.</summary>
    public virtual void Draw(MewDrawingContext context)
    {
        if (string.IsNullOrEmpty(Text)) return;

        var layout = MewChartsText.GetLayout(Text, TextSize);
        if (layout is null) return;

        var textSize = layout.MeasuredSize;
        var width = textSize.Width + Padding.Left + Padding.Right;
        var height = textSize.Height + Padding.Top + Padding.Bottom;

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

        context.G.Text.Draw(
            layout,
            new Point(left + Padding.Left, top + Padding.Top),
            new TextDrawOptions(context.ActiveColor));
    }
}
