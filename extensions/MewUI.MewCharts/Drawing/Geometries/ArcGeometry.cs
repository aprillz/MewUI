using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>
/// An open arc stroke (used by angular-gauge ticks). The sweep is approximated by short line
/// segments because the MewUI path API has no angle-sweep arc primitive.
/// </summary>
public class ArcGeometry : BaseArcGeometry, IDrawnElement<MewDrawingContext>
{
    private const double DegToRad = Math.PI / 180.0;

    public virtual void Draw(MewDrawingContext context)
    {
        var centerX = CenterX;
        var centerY = CenterY;
        var radius = Width * 0.5f;
        var startAngle = StartAngle;
        var sweepAngle = SweepAngle;

        var segments = Math.Max(2, (int)(Math.Abs(sweepAngle) / 3.0));
        var path = new PathGeometry();

        Point OnCircle(double angleDegrees) =>
            new(centerX + Math.Cos(angleDegrees * DegToRad) * radius,
                centerY + Math.Sin(angleDegrees * DegToRad) * radius);

        path.MoveTo(OnCircle(startAngle));
        for (var step = 1; step <= segments; step++)
            path.LineTo(OnCircle(startAngle + sweepAngle * step / segments));

        context.DrawPath(path);
    }
}
