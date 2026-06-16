using Aprillz.MewUI.MewCharts.Painting;

using LiveChartsCore.Drawing;
using LiveChartsCore.Generators;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A rectangle that paints with its own <see cref="Color"/> (used by heat maps).</summary>
public partial class ColoredRectangleGeometry : BoundedDrawnGeometry, IColoredGeometry, IDrawnElement<MewDrawingContext>
{
    public ColoredRectangleGeometry() => _ColorMotionProperty = new(LvcColor.Empty);

    /// <inheritdoc cref="IColoredGeometry.Color" />
    [MotionProperty]
    public partial LvcColor Color { get; set; }

    public virtual void Draw(MewDrawingContext context)
    {
        context.ActiveColor = MewPaint.ToMewColor(Color);
        context.ActiveBrush = null;
        context.DrawRectangle(new Rect(X, Y, Width, Height));
    }
}
