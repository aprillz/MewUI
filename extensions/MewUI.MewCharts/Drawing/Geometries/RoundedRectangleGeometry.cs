using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A rounded rectangle geometry (bars/columns).</summary>
public class RoundedRectangleGeometry : BaseRoundedRectangleGeometry, IDrawnElement<MewDrawingContext>
{
    /// <summary>Fills or strokes the rectangle bounds with the border radius and the active paint.</summary>
    public void Draw(MewDrawingContext context)
    {
        var borderRadius = BorderRadius;
        context.DrawRoundedRectangle(new Rect(X, Y, Width, Height), borderRadius.X, borderRadius.Y);
    }
}
