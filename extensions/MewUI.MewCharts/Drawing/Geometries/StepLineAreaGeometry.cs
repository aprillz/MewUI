using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Segments;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>The connecting path/area of a step-line series.</summary>
public class StepLineAreaGeometry : VectorGeometry
{
    private bool _isFirst = true;

    protected override void OnDrawSegment(MewDrawingContext context, PathGeometry path, Segment segment)
    {
        if (_isFirst)
        {
            _isFirst = false;
            return;
        }

        path.LineTo(segment.Xj, segment.Yi);
        path.LineTo(segment.Xj, segment.Yj);
    }

    protected override void OnOpen(MewDrawingContext context, PathGeometry path, Segment segment)
    {
        if (ClosingMethod == VectorClosingMethod.NotClosed)
        {
            path.MoveTo(segment.Xj, segment.Yj);
            return;
        }

        if (ClosingMethod == VectorClosingMethod.CloseToPivot)
        {
            path.MoveTo(segment.Xj, Pivot);
            path.LineTo(segment.Xj, segment.Yj);
        }
    }

    protected override void OnClose(MewDrawingContext context, PathGeometry path, Segment segment)
    {
        _isFirst = true;

        if (ClosingMethod == VectorClosingMethod.NotClosed) return;

        if (ClosingMethod == VectorClosingMethod.CloseToPivot)
        {
            path.LineTo(segment.Xj, Pivot);
            path.Close();
        }
    }
}
