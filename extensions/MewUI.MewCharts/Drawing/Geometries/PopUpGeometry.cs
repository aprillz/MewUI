using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A rounded panel with a wedge on one side that points at its target, used as the tooltip background.</summary>
public class PopUpGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    private readonly PathGeometry _path = new();

    /// <summary>Gets or sets the wedge length in DIPs; the default is 15.</summary>
    public double Wedge { get; set; } = 15;

    /// <summary>Gets or sets the wedge base width as a multiple of <see cref="Wedge"/>; the default is 2.</summary>
    public double WedgeThickness { get; set; } = 2;

    /// <summary>Gets or sets the diameter of the rounded corners in DIPs; the default is 10.</summary>
    public double BorderRadius { get; set; } = 10;

    /// <summary>Gets or sets the side of the panel the wedge sits on; the default is <see cref="PopUpPlacement.Bottom"/>, a wedge on the top edge pointing up.</summary>
    public PopUpPlacement Placement { get; set; } = PopUpPlacement.Bottom;

    /// <summary>Gets the panel bounds without the wedge.</summary>
    public Rect GetBodyBounds()
    {
        var x = X + (Placement == PopUpPlacement.Right ? Wedge : 0);
        var y = Y + (Placement == PopUpPlacement.Bottom ? Wedge : 0);
        var width = Width - (Placement is PopUpPlacement.Right or PopUpPlacement.Left ? Wedge : 0);
        var height = Height - (Placement is PopUpPlacement.Bottom or PopUpPlacement.Top ? Wedge : 0);
        return new Rect(x, y, Math.Max(0, width), Math.Max(0, height));
    }

    /// <summary>Fills or strokes the panel and its wedge with the active paint.</summary>
    public virtual void Draw(MewDrawingContext context)
    {
        var body = GetBodyBounds();
        var x = body.X;
        var y = body.Y;
        var width = body.Width;
        var height = body.Height;
        var wedge = Wedge;
        var halfBase = wedge * WedgeThickness * 0.5;
        var radius = BorderRadius / 2;

        var path = _path;
        path.Reset();
        path.MoveTo(x + radius, y);

        if (Placement == PopUpPlacement.Bottom)
        {
            path.LineTo(x + width * 0.5 - halfBase, y);
            path.LineTo(x + width * 0.5, y - wedge);
            path.LineTo(x + width * 0.5 + halfBase, y);
        }

        path.LineTo(x + width - radius, y);
        AddCorner(path, x + width - radius, y + radius, radius, 270);

        if (Placement == PopUpPlacement.Left)
        {
            path.LineTo(x + width, y + height * 0.5 - halfBase);
            path.LineTo(x + width + wedge, y + height * 0.5);
            path.LineTo(x + width, y + height * 0.5 + halfBase);
        }

        path.LineTo(x + width, y + height - radius);
        AddCorner(path, x + width - radius, y + height - radius, radius, 0);

        if (Placement == PopUpPlacement.Top)
        {
            path.LineTo(x + width * 0.5 + halfBase, y + height);
            path.LineTo(x + width * 0.5, y + height + wedge);
            path.LineTo(x + width * 0.5 - halfBase, y + height);
        }

        path.LineTo(x + radius, y + height);
        AddCorner(path, x + radius, y + height - radius, radius, 90);

        if (Placement == PopUpPlacement.Right)
        {
            path.LineTo(x, y + height * 0.5 + halfBase);
            path.LineTo(x - wedge, y + height * 0.5);
            path.LineTo(x, y + height * 0.5 - halfBase);
        }

        path.LineTo(x, y + radius);
        AddCorner(path, x + radius, y + radius, radius, 180);

        path.Close();
        context.DrawPath(path);
    }

    // A quarter circle turning clockwise on screen from startDegrees, joined to the current point by a line.
    private static void AddCorner(PathGeometry path, double centerX, double centerY, double radius, double startDegrees)
    {
        if (radius <= 0) return;
        var start = startDegrees * Math.PI / 180;
        path.LineTo(centerX + radius * Math.Cos(start), centerY + radius * Math.Sin(start));
        path.Arc(centerX, centerY, radius, radius, start, start + Math.PI / 2);
    }
}
