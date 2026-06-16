using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Segments;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>The connecting line/area of a line series, drawn with cubic beziers.</summary>
public class CubicBezierAreaGeometry : VectorGeometry
{
    protected override void OnDrawSegment(MewDrawingContext context, PathGeometry path, Segment segment)
    {
        var cubic = (CubicBezierSegment)segment;
        path.BezierTo(segment.Xi, segment.Yi, cubic.Xm, cubic.Ym, segment.Xj, segment.Yj);
    }

    protected override void OnOpen(MewDrawingContext context, PathGeometry path, Segment segment)
    {
        if (ClosingMethod == VectorClosingMethod.NotClosed)
        {
            path.MoveTo(segment.Xi, segment.Yi);
            return;
        }

        if (ClosingMethod == VectorClosingMethod.CloseToPivot)
        {
            path.MoveTo(segment.Xi, Pivot);
            path.LineTo(segment.Xi, segment.Yi);
        }
    }

    protected override void OnClose(MewDrawingContext context, PathGeometry path, Segment segment)
    {
        if (ClosingMethod == VectorClosingMethod.NotClosed) return;

        if (ClosingMethod == VectorClosingMethod.CloseToPivot)
        {
            path.LineTo(segment.Xj, Pivot);
            path.Close();
        }
    }
}
