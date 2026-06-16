using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A rectangle geometry.</summary>
public class RectangleGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    public virtual void Draw(MewDrawingContext context) =>
        context.DrawRectangle(new Rect(X, Y, Width, Height));
}
