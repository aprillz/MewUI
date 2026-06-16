using LiveChartsCore.Drawing;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A box-and-whisker geometry (min/first/median/third/max).</summary>
public class BoxGeometry : BaseBoxGeometry, IDrawnElement<MewDrawingContext>
{
    public virtual void Draw(MewDrawingContext context)
    {
        var width = Width;
        var cx = X + width * 0.5f;
        var max = Y;
        var third = Third;
        var first = First;
        var min = Min;
        var median = Median;

        float yi, yj;
        if (third > first) { yi = first; yj = third; }
        else { yi = third; yj = first; }

        if (context.ActiveStyle.HasFlag(PaintStyle.Stroke))
        {
            context.DrawLine(new Point(cx, max), new Point(cx, yi));
            context.DrawLine(new Point(X, median), new Point(X + width, median));
            context.DrawLine(new Point(cx, yj), new Point(cx, min));
        }

        context.DrawRectangle(new Rect(X, yi, width, Math.Abs(third - first)));
    }
}
