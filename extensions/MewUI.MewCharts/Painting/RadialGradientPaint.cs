using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Painting;

/// <summary>
/// Paints geometries with a radial gradient centered in the current draw area. Center/radius are
/// relative (0..1) to the draw area and resolved to absolute coordinates at paint time.
/// </summary>
public class RadialGradientPaint : MewPaint
{
    private readonly GradientStop[] _stops;
    private readonly Point _center;
    private readonly double _radius;
    private IBrush? _brush;
    private IPen? _pen;

    public RadialGradientPaint(GradientStop[] stops, Point center, double radius)
    {
        _stops = stops;
        _center = center;
        _radius = radius;
    }

    public RadialGradientPaint(Color[] colors) : this(BuildStops(colors), new Point(0.5, 0.5), 0.5) { }

    public RadialGradientPaint(Color center, Color outer)
        : this([new GradientStop(0, center), new GradientStop(1, outer)], new Point(0.5, 0.5), 0.5) { }

    private static GradientStop[] BuildStops(Color[] colors)
    {
        var stops = new GradientStop[colors.Length];
        for (var index = 0; index < colors.Length; index++)
        {
            var offset = colors.Length == 1 ? 0 : (double)index / (colors.Length - 1);
            stops[index] = new GradientStop(offset, colors[index]);
        }
        return stops;
    }

    public override Paint CloneTask() =>
        new RadialGradientPaint(_stops, _center, _radius)
        {
            PaintStyle = PaintStyle,
            IsAntialias = IsAntialias,
            StrokeThickness = StrokeThickness,
            ZIndex = ZIndex,
        };

    internal override void OnPaintStarted(DrawingContext drawingContext, IDrawnElement? drawnElement)
    {
        var context = (MewDrawingContext)drawingContext;
        var area = context.DrawArea;
        var center = new Point(area.X + _center.X * area.Width, area.Y + _center.Y * area.Height);
        var radius = _radius * Math.Min(area.Width, area.Height);

        _brush?.Dispose();
        _brush = context.Factory.CreateRadialGradientBrush(center, center, radius, radius, _stops, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null);

        var thickness = drawnElement?.StrokeThickness ?? StrokeThickness;
        context.ActiveColor = _stops[_stops.Length / 2].Color;
        context.ActiveStyle = PaintStyle;
        context.ActiveStrokeThickness = thickness;

        if (PaintStyle.HasFlag(PaintStyle.Stroke))
        {
            _pen?.Dispose();
            _pen = context.Factory.CreatePen(_brush, thickness);
            context.ActivePen = _pen;
            context.ActiveBrush = null;
        }
        else
        {
            context.ActiveBrush = _brush;
            context.ActivePen = null;
        }
    }

    internal override Paint Transitionate(float progress, Paint target) => target;

    internal override void DisposeTask()
    {
        _pen?.Dispose();
        _pen = null;
        _brush?.Dispose();
        _brush = null;
        base.DisposeTask();
    }
}
