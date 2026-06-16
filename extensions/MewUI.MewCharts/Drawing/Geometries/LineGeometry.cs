using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A straight line geometry (also used for axis separators and error bars).</summary>
public class LineGeometry : BaseLineGeometry, IDrawnElement<MewDrawingContext>
{
    public virtual void Draw(MewDrawingContext context) =>
        context.DrawLine(new Point(X, Y), new Point(X1, Y1));
}
