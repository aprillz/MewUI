using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

internal static class MewSvgPathUtilities
{
    public static PathGeometry TransformPath(PathGeometry source, Matrix3x2 matrix)
    {
        if (source.IsEmpty || matrix == Matrix3x2.Identity)
            return source;

        var path = new PathGeometry { FillRule = source.FillRule };
        foreach (var cmd in source.Commands)
        {
            switch (cmd.Type)
            {
                case PathCommandType.MoveTo:
                {
                    var p = Vector2.Transform(new Vector2((float)cmd.X0, (float)cmd.Y0), matrix);
                    path.MoveTo(p.X, p.Y);
                    break;
                }
                case PathCommandType.LineTo:
                {
                    var p = Vector2.Transform(new Vector2((float)cmd.X0, (float)cmd.Y0), matrix);
                    path.LineTo(p.X, p.Y);
                    break;
                }
                case PathCommandType.BezierTo:
                {
                    var p1 = Vector2.Transform(new Vector2((float)cmd.X0, (float)cmd.Y0), matrix);
                    var p2 = Vector2.Transform(new Vector2((float)cmd.X1, (float)cmd.Y1), matrix);
                    var p3 = Vector2.Transform(new Vector2((float)cmd.X2, (float)cmd.Y2), matrix);
                    path.BezierTo(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y);
                    break;
                }
                case PathCommandType.Close:
                    path.Close();
                    break;
            }
        }

        return path;
    }

    public static StrokeStyle CreateStrokeStyle(SvgVisualElement element, ISvgRenderer renderer, double strokeWidth)
    {
        IReadOnlyList<double>? dashArray = null;
        if (element.StrokeDashArray != null && element.StrokeDashArray.Count > 0)
        {
            var values = element.StrokeDashArray
                .Select(x => (double)(Math.Max(1f, x.ToDeviceValue(renderer, UnitRenderingType.Other, element)) / strokeWidth))
                .ToArray();
            if (values.Length % 2 != 0)
                values = values.Concat(values).ToArray();
            dashArray = values;
        }

        return new StrokeStyle
        {
            LineCap = element.StrokeLineCap switch
            {
                SvgStrokeLineCap.Round => StrokeLineCap.Round,
                SvgStrokeLineCap.Square => StrokeLineCap.Square,
                _ => StrokeLineCap.Flat
            },
            LineJoin = element.StrokeLineJoin switch
            {
                SvgStrokeLineJoin.Bevel => StrokeLineJoin.Bevel,
                SvgStrokeLineJoin.Round => StrokeLineJoin.Round,
                _ => StrokeLineJoin.Miter
            },
            MiterLimit = element.StrokeMiterLimit,
            DashArray = dashArray,
            DashOffset = dashArray == null ? 0 : element.StrokeDashOffset.ToDeviceValue(renderer, UnitRenderingType.Other, element) / strokeWidth
        };
    }

    public static SpreadMethod ToSpreadMethod(SvgGradientSpreadMethod method)
    {
        return method switch
        {
            SvgGradientSpreadMethod.Reflect => SpreadMethod.Reflect,
            SvgGradientSpreadMethod.Repeat => SpreadMethod.Repeat,
            _ => SpreadMethod.Pad
        };
    }

    public static List<Point> GetMarkerPoints(PathGeometry path)
    {
        var points = new List<Point>();
        Point current = default;
        Point figureStart = default;
        var hasFigure = false;

        foreach (var cmd in path.Commands)
        {
            switch (cmd.Type)
            {
                case PathCommandType.MoveTo:
                    current = new Point(cmd.X0, cmd.Y0);
                    figureStart = current;
                    points.Add(current);
                    hasFigure = true;
                    break;

                case PathCommandType.LineTo:
                    current = new Point(cmd.X0, cmd.Y0);
                    points.Add(current);
                    break;

                case PathCommandType.BezierTo:
                    current = new Point(cmd.X2, cmd.Y2);
                    points.Add(current);
                    break;

                case PathCommandType.Close:
                    if (hasFigure)
                    {
                        current = figureStart;
                        points.Add(current);
                    }
                    break;
            }
        }

        return points;
    }
}
