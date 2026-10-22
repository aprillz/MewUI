using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Painting;

/// <summary>Paints geometries with a radial gradient whose center and radius are relative (0..1) to the current draw area.</summary>
public class RadialGradientPaint : MewPaint
{
    private readonly GradientStop[] _stops;
    private readonly Point _center;
    private readonly double _radius;
    private Brush? _brush;
    private Pen? _pen;

    /// <summary>Initializes a gradient with explicit stops and a relative center and radius.</summary>
    public RadialGradientPaint(GradientStop[] stops, Point center, double radius)
    {
        _stops = stops;
        _center = center;
        _radius = radius;
    }

    /// <summary>Initializes a gradient centered in the draw area with evenly spaced stops for the given colors.</summary>
    public RadialGradientPaint(Color[] colors) : this(BuildStops(colors), new Point(0.5, 0.5), 0.5) { }

    /// <summary>Initializes a gradient centered in the draw area from <paramref name="center"/> to <paramref name="outer"/>.</summary>
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

    /// <summary>Returns a new paint with the same gradient and paint settings.</summary>
    public override Paint CloneTask() =>
        new RadialGradientPaint(_stops, _center, _radius)
        {
            PaintStyle = PaintStyle,
            IsAntialias = IsAntialias,
            StrokeThickness = StrokeThickness,
            ZIndex = ZIndex,
            PixelSnap = PixelSnap,
        };

    internal override void OnPaintStarted(DrawingContext drawingContext, IDrawnElement? drawnElement)
    {
        var context = (MewDrawingContext)drawingContext;
        var area = context.DrawArea;
        var center = new Point(area.X + _center.X * area.Width, area.Y + _center.Y * area.Height);
        var radius = _radius * Math.Min(area.Width, area.Height);

        // Descriptors are immutable, so reuse them across frames and rebuild only when the
        // resolved geometry changes (draw area is stable between resizes).
        if (_brush is not RadialGradientBrush cached || cached.Center != center || cached.RadiusX != radius)
        {
            _brush = new RadialGradientBrush(center, center, radius, radius, _stops, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null);
        }

        var thickness = drawnElement?.StrokeThickness ?? StrokeThickness;
        context.ActiveColor = _stops[_stops.Length / 2].Color;
        context.ActiveStyle = PaintStyle;
        context.ActiveStrokeThickness = thickness;
        context.ActivePixelSnap = PixelSnap;

        if (PaintStyle.HasFlag(PaintStyle.Stroke))
        {
            if (_pen is null || !ReferenceEquals(_pen.Brush, _brush) || _pen.Thickness != thickness)
            {
                _pen = new Pen(_brush, thickness);
            }
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
        base.DisposeTask();
    }
}
