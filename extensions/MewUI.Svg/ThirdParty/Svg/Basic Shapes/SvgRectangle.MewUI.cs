using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgRectangle
{
    private PathGeometry _path;

    public override PathGeometry? Path(ISvgRenderer renderer)
    {
        if (_path is null || IsPathDirty)
        {
            var location = Location.ToDeviceValue(renderer, this);
            var width = Width.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this);
            var height = Height.ToDeviceValue(renderer, UnitRenderingType.Vertical, this);
            if (width <= 0f || height <= 0f)
            {
                return null;
            }

            var rx = Math.Min(CornerRadiusX.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this), width / 2f);
            var ry = Math.Min(CornerRadiusY.ToDeviceValue(renderer, UnitRenderingType.Vertical, this), height / 2f);
            var rect = new Rect(location.X, location.Y, width, height);
            _path = (rx > 0f || ry > 0f)
                ? PathGeometry.FromRoundedRect(rect, rx, ry)
                : PathGeometry.FromRect(rect);
            IsPathDirty = false;
        }

        return _path;
    }
}
