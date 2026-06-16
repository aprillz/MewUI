using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Painting;

using LiveChartsCore.Drawing;
using LiveChartsCore.Geo;
using LiveChartsCore.Kernel.Providers;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Motion;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts;

/// <summary>
/// The MewUI chart engine: supplies the MewUI backend's default axes, paints and visuals to
/// the LiveCharts core. The MewUI analog of <c>SkiaSharpProvider</c>.
/// </summary>
public class MewChartEngine : ChartEngine
{
    public override IMapFactory GetDefaultMapFactory() =>
        throw new NotSupportedException("GeoMap is not yet supported by the MewUI charts backend.");

    public override ICartesianAxis GetDefaultCartesianAxis() => new Axis();

    public override IPolarAxis GetDefaultPolarAxis() => new PolarAxis();

    public override Paint GetSolidColorPaint(LvcColor color = new()) =>
        new SolidColorPaint(MewPaint.ToMewColor(color));

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
