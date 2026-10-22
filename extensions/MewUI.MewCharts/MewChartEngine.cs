using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Painting;

using LiveChartsCore.Drawing;
using LiveChartsCore.Geo;
using LiveChartsCore.Kernel.Providers;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Motion;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts;

/// <summary>The LiveCharts <see cref="ChartEngine"/> that supplies the MewUI default axes, paints and visuals.</summary>
public class MewChartEngine : ChartEngine
{
    /// <summary>Always throws <see cref="NotSupportedException"/>; geo maps are not supported by the MewUI backend.</summary>
    public override IMapFactory GetDefaultMapFactory() =>
        throw new NotSupportedException("GeoMap is not yet supported by the MewUI charts backend.");

    /// <summary>Creates a new <see cref="Axis"/>.</summary>
    public override ICartesianAxis GetDefaultCartesianAxis() => new Axis();

    /// <summary>Creates a new <see cref="PolarAxis"/>.</summary>
    public override IPolarAxis GetDefaultPolarAxis() => new PolarAxis();

    /// <summary>Creates a <see cref="SolidColorPaint"/> of the given color.</summary>
    public override Paint GetSolidColorPaint(LvcColor color = new()) =>
        new SolidColorPaint(MewPaint.ToMewColor(color));

    /// <summary>Adds the translucent zooming-section rectangle to the canvas and returns it.</summary>
    public override BoundedDrawnGeometry InitializeZoommingSection(CoreMotionCanvas canvas)
    {
        var rectangle = new RectangleGeometry();

        var zoomingSectionPaint = new SolidColorPaint(new Color(50, 33, 150, 243))
        {
            PaintStyle = PaintStyle.Fill,
            ZIndex = int.MaxValue,
        };

        zoomingSectionPaint.AddGeometryToPaintTask(canvas, rectangle);
        canvas.AddDrawableTask(zoomingSectionPaint);

        return rectangle;
    }
}
