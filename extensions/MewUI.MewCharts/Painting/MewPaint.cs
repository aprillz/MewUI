using Aprillz.MewUI.MewCharts.Drawing;

using LiveChartsCore.Drawing;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Painting;

/// <summary>
/// Base <see cref="Paint"/> for the MewUI drawing backend. Subclasses resolve their visual
/// state into <see cref="MewDrawingContext"/> in <c>OnPaintStarted</c>.
/// </summary>
public abstract class MewPaint : Paint
{
    protected MewPaint(float strokeThickness = 1f, float strokeMiter = 0f)
        : base(strokeThickness, strokeMiter)
    { }

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
