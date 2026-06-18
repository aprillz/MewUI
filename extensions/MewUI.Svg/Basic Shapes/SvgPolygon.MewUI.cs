using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgPolygon
{
    private PathGeometry _path;

    public override PathGeometry? Path(ISvgRenderer renderer)
    {
        if (_path is null || IsPathDirty)
        {
            if (Points is null || Points.Count < 2)
            {
                return null;
            }

            _path = new PathGeometry();
            var first = SvgUnit.GetDevicePoint(Points[0], Points[1], renderer, this);
            _path.MoveTo(first);
            for (int i = 2; (i + 1) < Points.Count; i += 2)
            {
                var point = SvgUnit.GetDevicePoint(Points[i], Points[i + 1], renderer, this);
                _path.LineTo(point);
            }

            _path.Close();
            IsPathDirty = false;
        }

        return _path;
    }
}
