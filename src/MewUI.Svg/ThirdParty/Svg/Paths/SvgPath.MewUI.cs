using Aprillz.MewUI.Rendering;
using Svg.Pathing;

namespace Svg;

public partial class SvgPath
{
    private PathGeometry _path;

    public override PathGeometry? Path(ISvgRenderer renderer)
    {
        if (_path is null || IsPathDirty)
        {
            _path = new PathGeometry();
            _path.FillRule = FillRule == SvgFillRule.NonZero
                ? Aprillz.MewUI.Rendering.FillRule.NonZero
                : Aprillz.MewUI.Rendering.FillRule.EvenOdd;

            var pathData = PathData;
            if (pathData is not null && pathData.Count > 0 && pathData.First is SvgMoveToSegment)
            {
                var start = System.Drawing.PointF.Empty;
                foreach (var segment in pathData)
                {
                    start = segment.AddToPath(_path, start, pathData);
                }
            }

            IsPathDirty = false;
        }

        return _path;
    }
}
