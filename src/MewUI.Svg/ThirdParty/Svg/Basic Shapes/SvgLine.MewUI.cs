using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgLine
{
    private PathGeometry _path;

    public override PathGeometry? Path(ISvgRenderer renderer)
    {
        if ((_path is null || IsPathDirty) && StrokeWidth > 0)
        {
            var start = new Aprillz.MewUI.Point(StartX.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this), StartY.ToDeviceValue(renderer, UnitRenderingType.Vertical, this));
            var end = new Aprillz.MewUI.Point(EndX.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this), EndY.ToDeviceValue(renderer, UnitRenderingType.Vertical, this));
            _path = new PathGeometry();
            _path.MoveTo(start);
            _path.LineTo(end);
            IsPathDirty = false;
        }

        return _path;
    }
}
