using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>An angular-gauge needle: a triangle pointing from the pivot outward, plus a pivot disc.</summary>
public class NeedleGeometry : BaseNeedleGeometry, IDrawnElement<MewDrawingContext>
{
    public NeedleGeometry() => TransformOrigin = new LvcPoint(0f, 0f);

    public virtual void Draw(MewDrawingContext context)
    {
        var halfWidth = Width / 2f;

        var path = new PathGeometry();
        path.MoveTo(new Point(X, Y + Radius));
        path.LineTo(new Point(X - halfWidth, Y));
        path.LineTo(new Point(X + halfWidth, Y));
        path.Close();

        context.DrawPath(path);
        context.DrawEllipse(new Rect(X - halfWidth, Y - halfWidth, halfWidth * 2, halfWidth * 2));
    }
}
