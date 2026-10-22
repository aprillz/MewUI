using Aprillz.MewUI.MewCharts.Drawing;

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

    internal override void OnPaintFinished(DrawingContext drawingContext, IDrawnElement? drawnElement) { }

    internal override void ApplyOpacityMask(DrawingContext context, float opacity, IDrawnElement? drawnElement) =>
        ((MewDrawingContext)context).PushOpacity(opacity);

    internal override void RestoreOpacityMask(DrawingContext context, float opacity, IDrawnElement? drawnElement) =>
        ((MewDrawingContext)context).PopOpacity();

    internal override void DisposeTask() { }

    /// <summary>Converts a LiveCharts color to a MewUI color.</summary>
    public static Color ToMewColor(LiveChartsCore.Drawing.LvcColor color) =>
        new(color.A, color.R, color.G, color.B);
}
