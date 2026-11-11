using Aprillz.MewUI;
using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore.Drawing;

// Backing point for Lines/CustomPoints (upstream DataPoint): carries a per-point marker rotation.
sealed class CustomPointsDataPoint(int value, float rotation)
{
    public int Value { get; set; } = value;
    public float Rotation { get; set; } = rotation;
}

// Arrow marker for Lines/CustomPoints (upstream ArrowGeometry, path from svgrepo arrow-up).
sealed class ArrowGeometry : BaseSvgPathGeometry
{
    private const string ARROW_PATH =
        "M12.75 20C12.75 20.4142 12.4142 20.75 12 20.75C11.5858 20.75 11.25 20.4142 11.25 " +
        "20L11.25 10.75H6.00002C5.69668 10.75 5.4232 10.5673 5.30711 10.287C5.19103 10.0068 " +
        "5.25519 9.68417 5.46969 9.46967L11.4697 3.46967C11.6103 3.32902 11.8011 3.25 12 " +
        "3.25C12.1989 3.25 12.3897 3.32902 12.5304 3.46967L18.5304 9.46967C18.7449 9.68417 " +
        "18.809 10.0068 18.6929 10.287C18.5768 10.5673 18.3034 10.75 18 10.75H12.75L12.75 20Z";

    public ArrowGeometry() : base(ARROW_PATH) { }
}

// Marker for Lines/Custom and StepLines/Custom (upstream MyGeometry): a rectangle crossed by both diagonals.
sealed class LinesMyGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    public void Draw(MewDrawingContext context)
    {
        context.DrawRectangle(new Rect(X, Y, Width, Height));
        context.DrawLine(new Point(X, Y), new Point(X + Width, Y + Height));
        context.DrawLine(new Point(X + Width, Y), new Point(X, Y + Height));
    }
}

// Marker for Scatter/Custom (upstream MyGeometry): horizontal lines every 10 pixels.
sealed class ScatterMyGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    public void Draw(MewDrawingContext context)
    {
        var lineY = Y;
        while (lineY < Y + Height)
        {
            context.DrawLine(new Point(X, lineY), new Point(X + Width, lineY));
            lineY += 10;
        }
    }
}
