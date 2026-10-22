using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A rectangle geometry.</summary>
public class RectangleGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    /// <summary>Fills or strokes the rectangle bounds with the active paint.</summary>
    public virtual void Draw(MewDrawingContext context) =>
        context.DrawRectangle(new Rect(X, Y, Width, Height));
}
