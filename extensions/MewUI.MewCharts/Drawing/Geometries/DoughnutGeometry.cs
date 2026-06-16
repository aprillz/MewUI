using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>
/// A pie/doughnut slice. The angular arcs are approximated by short line segments because the
/// MewUI path API has no angle-sweep arc primitive; segment count scales with the sweep.
/// </summary>
public class DoughnutGeometry : BaseDoughnutGeometry, IDrawnElement<MewDrawingContext>
{
    private const double DegToRad = Math.PI / 180.0;

    public virtual void Draw(MewDrawingContext context)
    {
        var centerX = CenterX;
        var centerY = CenterY;
        var innerRadius = InnerRadius;
        var outerRadius = Width * 0.5f;
        var startAngle = StartAngle;
        var sweepAngle = SweepAngle;

        var segments = Math.Max(2, (int)(Math.Abs(sweepAngle) / 3.0));
        var path = new PathGeometry();

        Point OnCircle(double angleDegrees, double radius) =>
            new(centerX + Math.Cos(angleDegrees * DegToRad) * radius,
                centerY + Math.Sin(angleDegrees * DegToRad) * radius);

        path.MoveTo(OnCircle(startAngle, innerRadius));
        path.LineTo(OnCircle(startAngle, outerRadius));

        for (var step = 1; step <= segments; step++)
            path.LineTo(OnCircle(startAngle + sweepAngle * step / segments, outerRadius));

        path.LineTo(OnCircle(startAngle + sweepAngle, innerRadius));

        for (var step = 1; step <= segments; step++)
            path.LineTo(OnCircle(startAngle + sweepAngle * (1.0 - (double)step / segments), innerRadius));

        path.Close();

        if (PushOut > 0)
        {
            var midAngle = (startAngle + 0.5f * sweepAngle) * DegToRad;
            context.G.Save();
            context.G.Translate(PushOut * Math.Cos(midAngle), PushOut * Math.Sin(midAngle));
            context.DrawPath(path);
            context.G.Restore();
        }
        else
        {
            context.DrawPath(path);
        }
    }
}
