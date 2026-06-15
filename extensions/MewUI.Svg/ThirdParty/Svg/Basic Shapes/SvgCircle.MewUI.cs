using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgCircle
{
    private PathGeometry _path;

    public override PathGeometry? Path(ISvgRenderer renderer)
    {
        if (_path is null || IsPathDirty)
        {
            var halfStrokeWidth = StrokeWidth / 2;
            if (renderer is not null)
            {
                halfStrokeWidth = 0;
                IsPathDirty = false;
            }

            var center = Center.ToDeviceValue(renderer, this);
            var radius = Radius.ToDeviceValue(renderer, UnitRenderingType.Other, this) + halfStrokeWidth;
            if (radius <= 0f)
            {
                return null;
            }

            _path = PathGeometry.FromCircle(center.X, center.Y, radius);
        }

        return _path;
    }

    protected override void Render(ISvgRenderer renderer)
    {
        if (Radius.Value > 0.0f)
        {
            base.Render(renderer);
        }
    }
}
