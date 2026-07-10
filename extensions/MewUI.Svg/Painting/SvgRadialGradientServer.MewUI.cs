using System.Numerics;
using System.Linq;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgRadialGradientServer
{
    protected override Brush? CreateBrush(SvgVisualElement renderingElement, ISvgRenderer renderer, float opacity, bool forStroke)
    {
        try
        {
            if (GradientUnits == SvgCoordinateUnits.ObjectBoundingBox)
                renderer.SetBoundable(renderingElement);

            var center = new Point(
                NormalizeUnit(CenterX).ToDeviceValue(renderer, UnitRenderingType.Horizontal, this),
                NormalizeUnit(CenterY).ToDeviceValue(renderer, UnitRenderingType.Vertical, this));
            var focalPoint = new Point(
                NormalizeUnit(FocalX).ToDeviceValue(renderer, UnitRenderingType.Horizontal, this),
                NormalizeUnit(FocalY).ToDeviceValue(renderer, UnitRenderingType.Vertical, this));
            var specifiedRadius = NormalizeUnit(Radius).ToDeviceValue(renderer, UnitRenderingType.Other, this);

            var bounds = renderer.GetBoundable().Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0 || specifiedRadius <= 0f)
            {
                if (GetCallback != null)
                    return GetCallback().GetBrush(renderingElement, renderer, opacity, forStroke);

                return null;
            }

            var effectiveTransform = GetEffectiveGradientTransform(bounds, GradientUnits == SvgCoordinateUnits.ObjectBoundingBox);

            var scaleBounds = Inflate(renderingElement.Bounds, renderingElement.StrokeWidth);
            var scale = CalcScale(scaleBounds, center, specifiedRadius, effectiveTransform);
            var blend = CalculateColorBlend(renderer, opacity, scale, out scale);

            return new RadialGradientBrush(
                center,
                focalPoint,
                specifiedRadius * scale,
                specifiedRadius * scale,
                blend,
                MewSvgPathUtilities.ToSpreadMethod(SpreadMethod),
                Aprillz.MewUI.Rendering.GradientUnits.UserSpaceOnUse,
                effectiveTransform);
        }
        finally
        {
            if (GradientUnits == SvgCoordinateUnits.ObjectBoundingBox)
                renderer.PopBoundable();
        }
    }

    private float CalcScale(Rect bounds, Point center, float radius, Matrix3x2 transform)
    {
        var points = new[]
        {
            new Point(bounds.Left, bounds.Top),
            new Point(bounds.Right, bounds.Top),
            new Point(bounds.Right, bounds.Bottom),
            new Point(bounds.Left, bounds.Bottom)
        };

        var transformedCenter = TransformPoint(center, transform);
        var shrink = Matrix3x2.CreateTranslation((float)-transformedCenter.X, (float)-transformedCenter.Y)
            * Matrix3x2.CreateScale(.95f)
            * Matrix3x2.CreateTranslation((float)transformedCenter.X, (float)transformedCenter.Y);

        while (!(Contains(points[0], center, radius, transform)
            && Contains(points[1], center, radius, transform)
            && Contains(points[2], center, radius, transform)
            && Contains(points[3], center, radius, transform)))
        {
            var previous = new[]
            {
                points[0],
                points[1],
                points[2],
                points[3]
            };

            for (var i = 0; i < points.Length; i++)
            {
                points[i] = TransformPoint(points[i], shrink);
            }

            if (SequenceEqual(previous, points))
                break;
        }

        var height = points[2].Y - points[1].Y;
        if (Math.Abs(height) < 1e-6)
            return 1f;

        return (float)(bounds.Height / height);
    }

    private IReadOnlyList<GradientStop> CalculateColorBlend(ISvgRenderer renderer, float opacity, float scale, out float outScale)
    {
        var colorStops = GetColorStops(renderer, opacity, true)
            .Select(x => new GradientStop(x.Offset, x.Color))
            .ToList();

        outScale = scale;
        if (scale > 1)
        {
            switch (SpreadMethod)
            {
                case SvgGradientSpreadMethod.Reflect:
                {
                    var newScale = (float)Math.Ceiling(scale);
                    var positions = colorStops.Select(p => 1 + (p.Offset - 1) / newScale).ToList();
                    var colors = colorStops.Select(p => p.Color).ToList();

                    for (var i = 1; i < newScale; i++)
                    {
                        if (i % 2 == 1)
                        {
                            for (var j = 1; j < colorStops.Count; j++)
                            {
                                positions.Insert(0, (newScale - i - 1) / newScale + 1 - colorStops[j].Offset);
                                colors.Insert(0, colorStops[j].Color);
                            }
                        }
                        else
                        {
                            for (var j = 0; j < colorStops.Count - 1; j++)
                            {
                                positions.Insert(j, (newScale - i - 1) / newScale + colorStops[j].Offset);
                                colors.Insert(j, colorStops[j].Color);
                            }
                        }
                    }

                    outScale = newScale;
                    return ToGradientStops(positions, colors);
                }

                case SvgGradientSpreadMethod.Repeat:
                {
                    var newScale = (float)Math.Ceiling(scale);
                    var positions = colorStops.Select(p => p.Offset / newScale).ToList();
                    var colors = colorStops.Select(p => p.Color).ToList();

                    for (var i = 1; i < newScale; i++)
                    {
                        foreach (var stop in colorStops)
                        {
                            positions.Add((i + (stop.Offset <= 0 ? 0.001 : stop.Offset)) / newScale);
                            colors.Add(stop.Color);
                        }
                    }

                    outScale = newScale;
                    return ToGradientStops(positions, colors);
                }

                default:
                    outScale = 1f;
                    break;
            }
        }

        return colorStops;
    }

    private static IReadOnlyList<GradientStop> ToGradientStops(IReadOnlyList<double> positions, IReadOnlyList<Color> colors)
    {
        var result = new List<GradientStop>(positions.Count);
        for (var i = 0; i < positions.Count; i++)
            result.Add(new GradientStop(Math.Clamp(positions[i], 0.0, 1.0), colors[i]));

        return result;
    }

    private static Rect Inflate(Rect rect, float thickness)
        => new(rect.X - thickness, rect.Y - thickness, rect.Width + thickness * 2.0, rect.Height + thickness * 2.0);

    private static bool Contains(Point point, Point center, float radius, Matrix3x2 transform)
    {
        if (!Matrix3x2.Invert(transform, out var inverse))
            return false;

        var local = TransformPoint(point, inverse);
        var dx = local.X - center.X;
        var dy = local.Y - center.Y;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static bool SequenceEqual(IReadOnlyList<Point> left, IReadOnlyList<Point> right)
    {
        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
                return false;
        }

        return true;
    }
}
