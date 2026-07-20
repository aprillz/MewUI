using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Svg.DataTypes;

namespace Svg;

public partial class SvgMarker
{
    public override PathGeometry? Path(ISvgRenderer renderer)
    {
        return MarkerElement?.Path(renderer);
    }

    public void RenderMarker(ISvgRenderer renderer, SvgVisualElement owner, Point refPoint, Point markerPoint1, Point markerPoint2, bool isStartMarker)
    {
        var angle = 0f;
        if (Orient.IsAuto)
        {
            angle = (float)(Math.Atan2(markerPoint2.Y - markerPoint1.Y, markerPoint2.X - markerPoint1.X) * 180.0 / Math.PI);
            if (isStartMarker && Orient.IsAutoStartReverse)
            {
                angle += 180f;
            }
        }

        RenderPart2(angle, renderer, owner, refPoint);
    }

    public void RenderMarker(ISvgRenderer renderer, SvgVisualElement owner, Point refPoint, Point markerPoint1, Point markerPoint2, Point markerPoint3)
    {
        var angle1 = (float)(Math.Atan2(markerPoint2.Y - markerPoint1.Y, markerPoint2.X - markerPoint1.X) * 180.0 / Math.PI);
        var angle2 = (float)(Math.Atan2(markerPoint3.Y - markerPoint2.Y, markerPoint3.X - markerPoint2.X) * 180.0 / Math.PI);
        RenderPart2((angle1 + angle2) / 2f, renderer, owner, refPoint);
    }

    private void RenderPart2(float angle, ISvgRenderer renderer, SvgVisualElement owner, Point markerPoint)
    {
        var markerPath = GetClone(owner, renderer);
        if (markerPath is null || markerPath.IsEmpty)
        {
            return;
        }

        // System.Numerics transforms row vectors from left to right. Build the marker's
        // local transforms first and place it at the path vertex last. The previous order
        // translated to markerPoint first, so a child <use> rotate/scale also transformed
        // the absolute endpoint and moved markers off-canvas (issue-215-01).
        var matrix = MarkerElement?.Transforms is { Count: > 0 }
            ? MarkerElement.Transforms.GetMatrix()
            : Matrix3x2.Identity;

        switch (MarkerUnits)
        {
            case SvgMarkerUnits.StrokeWidth:
                if (ViewBox.Width > 0 && ViewBox.Height > 0)
                {
                    var strokeWidth = owner.StrokeWidth.ToDeviceValue(renderer, UnitRenderingType.Other, this);
                    matrix *= Matrix3x2.CreateTranslation(
                        AdjustForViewBoxWidth(-RefX.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this) * strokeWidth),
                        AdjustForViewBoxHeight(-RefY.ToDeviceValue(renderer, UnitRenderingType.Vertical, this) * strokeWidth));
                    matrix *= Matrix3x2.CreateScale(MarkerWidth, MarkerHeight);
                }
                else
                {
                    matrix *= Matrix3x2.CreateTranslation(
                        -RefX.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this),
                        -RefY.ToDeviceValue(renderer, UnitRenderingType.Vertical, this));
                }
                break;
            case SvgMarkerUnits.UserSpaceOnUse:
                matrix *= Matrix3x2.CreateTranslation(
                    -RefX.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this),
                    -RefY.ToDeviceValue(renderer, UnitRenderingType.Vertical, this));
                break;
        }

        matrix *= Matrix3x2.CreateRotation(
            (Orient.IsAuto ? angle : Orient.Angle) * (float)(Math.PI / 180.0));
        matrix *= Matrix3x2.CreateTranslation((float)markerPoint.X, (float)markerPoint.Y);

        var transformed = MewSvgPathUtilities.TransformPath(markerPath, matrix);
        var pen = CreatePen(owner, renderer);
        if (pen is not null)
        {
            renderer.DrawPath(pen, transformed);
        }

        var fill = Children.Count > 0 ? Children[0].Fill : Fill;
        if (fill is not null && fill != SvgPaintServer.None)
        {
            var brush = fill.GetBrush(this, renderer, FixOpacityValue(FillOpacity));
            if (brush is not null)
            {
                renderer.FillPath(brush, transformed);
            }
        }
    }

    private Pen? CreatePen(SvgVisualElement path, ISvgRenderer renderer)
    {
        if (Stroke is null || Stroke == SvgPaintServer.None)
        {
            return null;
        }

        var width = MarkerUnits == SvgMarkerUnits.StrokeWidth
            ? path.StrokeWidth.ToDeviceValue(renderer, UnitRenderingType.Other, this)
            : StrokeWidth.ToDeviceValue(renderer, UnitRenderingType.Other, this);
        if (width <= 0f)
        {
            return null;
        }

        var brush = Stroke.GetBrush(this, renderer, FixOpacityValue(Opacity), true);
        if (brush is null)
        {
            return null;
        }

        var strokeStyle = MewSvgPathUtilities.CreateStrokeStyle(this, renderer, Math.Max(width, 1f));
        return new Pen(brush, width, strokeStyle);
    }

    private PathGeometry? GetClone(SvgVisualElement path, ISvgRenderer renderer)
    {
        var source = Path(renderer);
        if (source is null || source.IsEmpty)
        {
            return null;
        }

        return MarkerUnits switch
        {
            SvgMarkerUnits.StrokeWidth => MewSvgPathUtilities.TransformPath(
                source,
                Matrix3x2.CreateScale(AdjustForViewBoxWidth(path.StrokeWidth), AdjustForViewBoxHeight(path.StrokeWidth))),
            _ => source
        };
    }

    private float AdjustForViewBoxWidth(float width)
    {
        return ViewBox.Width <= 0 ? 1 : width / ViewBox.Width;
    }

    private float AdjustForViewBoxHeight(float height)
    {
        return ViewBox.Height <= 0 ? 1 : height / ViewBox.Height;
    }
}
