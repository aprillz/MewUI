using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgEllipse
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

            var center = SvgUnit.GetDevicePoint(CenterX, CenterY, renderer, this);
            var radiusX = RadiusX.ToDeviceValue(renderer, UnitRenderingType.Other, this) + halfStrokeWidth;
            var radiusY = RadiusY.ToDeviceValue(renderer, UnitRenderingType.Other, this) + halfStrokeWidth;
            if (radiusX <= 0f || radiusY <= 0f)
            {
                return null;
            }

            _path = PathGeometry.FromEllipse(center.X, center.Y, radiusX, radiusY);
        }

        return _path;
    }

    protected override void Render(ISvgRenderer renderer)
    {
        if (RadiusX.Value > 0.0f && RadiusY.Value > 0.0f)
        {
            base.Render(renderer);
        }
    }
}
