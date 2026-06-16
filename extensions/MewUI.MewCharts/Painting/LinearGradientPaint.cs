using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Painting;

/// <summary>
/// Paints geometries with a linear gradient. Start/end are relative (0..1) to the current draw
/// area and resolved to absolute coordinates at paint time.
/// </summary>
public class LinearGradientPaint : MewPaint
{
    private static readonly Point DefaultStart = new(0, 0);
    private static readonly Point DefaultEnd = new(0, 1);

    private readonly GradientStop[] _stops;
    private readonly Point _start;
    private readonly Point _end;
    private IBrush? _brush;
    private IPen? _pen;

    public LinearGradientPaint(GradientStop[] stops, Point start, Point end)
    {
        _stops = stops;
        _start = start;
        _end = end;
    }

    public LinearGradientPaint(Color[] colors) : this(BuildStops(colors), DefaultStart, DefaultEnd) { }

    public LinearGradientPaint(Color start, Color end)
        : this([new GradientStop(0, start), new GradientStop(1, end)], DefaultStart, DefaultEnd) { }

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
        new LinearGradientPaint(_stops, _start, _end)
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
        var start = new Point(area.X + _start.X * area.Width, area.Y + _start.Y * area.Height);
        var end = new Point(area.X + _end.X * area.Width, area.Y + _end.Y * area.Height);

        _brush?.Dispose();
        _brush = context.Factory.CreateLinearGradientBrush(start, end, _stops, SpreadMethod.Pad, GradientUnits.UserSpaceOnUse, null);

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
