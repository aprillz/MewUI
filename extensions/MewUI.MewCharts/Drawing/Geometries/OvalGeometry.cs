using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>An ellipse inscribed in the geometry bounds; unlike <see cref="CircleGeometry"/> the width and height may differ.</summary>
public class OvalGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    /// <summary>Fills or strokes the ellipse with the active paint; an empty size draws nothing.</summary>
    public virtual void Draw(MewDrawingContext context)
    {
        if (Width <= 0 || Height <= 0) return;
        context.DrawEllipse(new Rect(X, Y, Width, Height));
    }
}
