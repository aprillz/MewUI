using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Painting;

/// <summary>
/// Paints geometries with a single solid <see cref="Color"/>, optionally with a dashed stroke.
/// </summary>
public class SolidColorPaint : MewPaint
{
    private IPen? _pen;

    public SolidColorPaint() { }

    public SolidColorPaint(Color color) => Color = color;

    public SolidColorPaint(Color color, float strokeThickness)
        : base(strokeThickness) => Color = color;

    /// <summary>The fill/stroke color.</summary>
    public Color Color { get; set; }

    /// <summary>Optional dash pattern (in stroke-width units, like SVG/Skia). Null = solid stroke.</summary>
    public double[]? DashArray { get; set; }

    /// <summary>Dash phase offset (in stroke-width units).</summary>
    public double DashOffset { get; set; }

    public override Paint CloneTask()
    {
        var clone = new SolidColorPaint(Color)
        {
            PaintStyle = PaintStyle,
            IsAntialias = IsAntialias,
            StrokeThickness = StrokeThickness,
            StrokeMiter = StrokeMiter,
            ZIndex = ZIndex,
            DashArray = DashArray,
            DashOffset = DashOffset,
        };
        return clone;
    }

    internal override void OnPaintStarted(DrawingContext drawingContext, IDrawnElement? drawnElement)
    {
        var context = (MewDrawingContext)drawingContext;
        var thickness = drawnElement?.StrokeThickness ?? StrokeThickness;

        context.ActiveColor = Color;
        context.ActiveStyle = PaintStyle;
        context.ActiveStrokeThickness = thickness;
        context.ActiveBrush = null;

        if (DashArray is { Length: > 0 } && PaintStyle.HasFlag(PaintStyle.Stroke))
        {
            _pen?.Dispose();
            _pen = context.Factory.CreatePen(Color, thickness, new StrokeStyle { DashArray = DashArray, DashOffset = DashOffset });
            context.ActivePen = _pen;
        }
        else
        {
            context.ActivePen = null;
        }
    }

    internal override void DisposeTask()
    {
        _pen?.Dispose();
        _pen = null;
        base.DisposeTask();
    }

    internal override Paint Transitionate(float progress, Paint target)
    {
        if (target is not SolidColorPaint toPaint) return target;

        Color = new Color(
            (byte)(Color.A + progress * (toPaint.Color.A - Color.A)),
            (byte)(Color.R + progress * (toPaint.Color.R - Color.R)),
            (byte)(Color.G + progress * (toPaint.Color.G - Color.G)),
            (byte)(Color.B + progress * (toPaint.Color.B - Color.B)));

        return this;
    }

    public override string ToString() => $"({Color.R}, {Color.G}, {Color.B}, {Color.A})";
}
