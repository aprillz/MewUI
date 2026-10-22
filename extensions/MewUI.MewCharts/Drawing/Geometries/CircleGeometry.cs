using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A circle geometry (point marker).</summary>
public class CircleGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    /// <summary>Draws a circle whose diameter is the geometry width; a non-positive width draws nothing.</summary>
    public virtual void Draw(MewDrawingContext context)
    {
        if (Width <= 0) return;
        context.DrawEllipse(new Rect(X, Y, Width, Width));
    }
}
