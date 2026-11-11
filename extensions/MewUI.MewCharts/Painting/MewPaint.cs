using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Painting.ImageFilters;
using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Painting;

/// <summary>Base <see cref="Paint"/> for the MewUI drawing backend, whose subclasses write their resolved state into the <see cref="MewDrawingContext"/> when painting starts.</summary>
public abstract class MewPaint : Paint
{
    /// <summary>Initializes the paint with the given stroke thickness and miter limit.</summary>
    protected MewPaint(float strokeThickness = 1f, float strokeMiter = 0f)
        : base(strokeThickness, strokeMiter)
    { }

    /// <summary>Gets or sets whether axis-aligned strokes are aligned to device pixels; the default is false.</summary>
    /// <remarks>Keep it disabled for data series and enable it for chart frames, axes and grid lines.</remarks>
    public bool PixelSnap { get; set; }

    /// <summary>Gets or sets the cap drawn at both ends of open strokes; the default is <see cref="StrokeLineCap.Flat"/>.</summary>
    public StrokeLineCap StrokeCap { get; set; }

    /// <summary>Gets or sets the join drawn where stroke segments meet; the default is <see cref="StrokeLineJoin.Miter"/>.</summary>
    public StrokeLineJoin StrokeJoin { get; set; }

    /// <summary>Gets or sets the font family of text drawn with this paint; null (the default) uses the chart's inherited font family.</summary>
    public string? FontFamily { get; set; }

    /// <summary>Gets or sets the font weight of text drawn with this paint; the default is <see cref="FontWeight.Normal"/>.</summary>
    public FontWeight FontWeight { get; set; } = FontWeight.Normal;

    /// <summary>Gets or sets whether text drawn with this paint is italic; the default is false.</summary>
    public bool Italic { get; set; }

    /// <summary>Gets or sets the filter applied to everything this paint draws, such as a <see cref="DropShadow"/>; null (the default) applies none.</summary>
    public ImageFilter? ImageFilter { get; set; }

    internal override void OnPaintFinished(DrawingContext drawingContext, IDrawnElement? drawnElement) { }

    internal override void ApplyOpacityMask(DrawingContext context, float opacity, IDrawnElement? drawnElement) =>
        ((MewDrawingContext)context).PushOpacity(opacity);

    internal override void RestoreOpacityMask(DrawingContext context, float opacity, IDrawnElement? drawnElement) =>
        ((MewDrawingContext)context).PopOpacity();

    internal override void DisposeTask() { }

    /// <summary>Converts a LiveCharts color to a MewUI color.</summary>
    public static Color ToMewColor(LiveChartsCore.Drawing.LvcColor color) =>
        new(color.A, color.R, color.G, color.B);

    /// <summary>Copies the stroke style, font and filter settings of this paint onto <paramref name="clone"/>.</summary>
    protected void CopyPaintSettingsTo(MewPaint clone)
    {
        clone.PixelSnap = PixelSnap;
        clone.FontFamily = FontFamily;
        clone.FontWeight = FontWeight;
        clone.Italic = Italic;
        clone.StrokeCap = StrokeCap;
        clone.StrokeJoin = StrokeJoin;
        clone.ImageFilter = ImageFilter?.Clone();
    }

    // Null when the stroke is solid with flat caps and miter joins, so the fast color/thickness path is used.
    internal StrokeStyle? GetStrokeStyle(double[]? dashArray = null, double dashOffset = 0)
    {
        bool dashed = dashArray is { Length: > 0 };
        if (!dashed && StrokeCap == StrokeLineCap.Flat && StrokeJoin == StrokeLineJoin.Miter && StrokeMiter <= 0)
        {
            return null;
        }

        return new StrokeStyle
        {
            LineCap = StrokeCap,
            LineJoin = StrokeJoin,
            MiterLimit = StrokeMiter > 0 ? StrokeMiter : StrokeStyle.Default.MiterLimit,
            DashArray = dashed ? dashArray : null,
            DashOffset = dashed ? dashOffset : 0,
        };
    }

    internal void ApplyFilterTo(MewDrawingContext context) =>
        context.ActivePaintShadow = ImageFilter as DropShadow;
}
