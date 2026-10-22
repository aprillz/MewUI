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
    private Pen? _pen;

    /// <summary>Initializes a paint with the default color.</summary>
    public SolidColorPaint() { }

    /// <summary>Initializes a paint of the given color.</summary>
    public SolidColorPaint(Color color) => Color = color;

    /// <summary>Initializes a paint of the given color and stroke thickness.</summary>
    public SolidColorPaint(Color color, float strokeThickness)
        : base(strokeThickness) => Color = color;

    /// <summary>Gets or sets the fill and stroke color.</summary>
    public Color Color { get; set; }

    /// <summary>Gets or sets the dash pattern in stroke-width units; null (the default) strokes solid.</summary>
    public double[]? DashArray { get; set; }

    /// <summary>Gets or sets the dash phase offset in stroke-width units.</summary>
    public double DashOffset { get; set; }

    /// <summary>Returns a new paint with the same color, dash and paint settings.</summary>
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
            PixelSnap = PixelSnap,
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
        context.ActivePixelSnap = PixelSnap;
        context.ActiveBrush = null;

        if (DashArray is { Length: > 0 } && PaintStyle.HasFlag(PaintStyle.Stroke))
        {
            var strokeStyle = new StrokeStyle { DashArray = DashArray, DashOffset = DashOffset };
            // Descriptors are immutable, so reuse the pen across frames and rebuild only when inputs change.
            if (_pen is null || _pen.Thickness != thickness || _pen.StrokeStyle != strokeStyle ||
                _pen.Brush is not SolidColorBrush solid || solid.Color != Color)
            {
                _pen = new Pen(Color, thickness, strokeStyle);
            }
            context.ActivePen = _pen;
        }
        else
        {
            context.ActivePen = null;
        }
    }

    internal override void DisposeTask()
    {
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

    /// <summary>Returns the color components as text.</summary>
    public override string ToString() => $"({Color.R}, {Color.G}, {Color.B}, {Color.A})";
}
