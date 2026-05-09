using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public abstract partial class SvgGradientServer
{
    public override IBrush? GetBrush(SvgVisualElement styleOwner, ISvgRenderer renderer, float opacity, bool forStroke = false)
    {
        LoadStops(styleOwner);

        if (Stops.Count == 0)
        {
            return null;
        }

        if (Stops.Count == 1)
        {
            var stopColor = Stops[0].GetColor(styleOwner);
            byte alpha = (byte)Math.Clamp(Math.Round(opacity * (stopColor.A / 255f) * 255), 0, 255);
            return renderer.GraphicsFactory.CreateSolidColorBrush(new Color(alpha, stopColor.R, stopColor.G, stopColor.B));
        }

        return CreateBrush(styleOwner, renderer, opacity, forStroke);
    }

    protected abstract IBrush? CreateBrush(SvgVisualElement renderingElement, ISvgRenderer renderer, float opacity, bool forStroke);

    protected Matrix3x2 EffectiveGradientTransform
        => GradientTransform?.GetMatrix() ?? Matrix3x2.Identity;

    protected IReadOnlyList<GradientStop> GetColorBlend(ISvgRenderer renderer, float opacity, bool radial)
    {
        int colourBlends = Stops.Count;
        bool insertStart = false;
        bool insertEnd = false;

        // MewUI backends expect SVG-convention stops for both linear and radial
        // (D2D stop 0 = origin/focal; GDI backend reverses internally).
        // So both orientations use the same ordering here.
        if (Stops[0].Offset.Value > 0f)
        {
            colourBlends++;
            insertStart = true;
        }

        float lastValue = Stops[^1].Offset.Value;
        if (lastValue < 100f || lastValue < 1f)
        {
            colourBlends++;
            insertEnd = true;
        }

        var blend = new List<GradientStop>(colourBlends);
        int actualStops = 0;

        for (int i = 0; i < colourBlends; i++)
        {
            var currentStop = Stops[actualStops];
            double boundWidth = renderer.GetBoundable().Bounds.Width;

            float mergedOpacity = opacity * currentStop.StopOpacity;
            double position = currentStop.Offset.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this) / boundWidth;
            position = Math.Min(Math.Max(position, 0f), 1f);
            var stopColor = currentStop.GetColor(this);
            var colour = new Color(
                (byte)Math.Clamp(Math.Round(mergedOpacity * 255), 0, 255),
                stopColor.R,
                stopColor.G,
                stopColor.B);

            actualStops++;

            if (insertStart && i == 0)
            {
                blend.Add(new GradientStop(0.0, colour));
                i++;
            }

            blend.Add(new GradientStop(position, colour));

            if (insertEnd && i == colourBlends - 2)
            {
                i++;
                blend.Add(new GradientStop(1.0, colour));
            }
        }

        return blend;
    }

    protected IReadOnlyList<GradientStop> GetColorStops(ISvgRenderer renderer, float opacity, bool radial)
        => GetColorBlend(renderer, opacity, radial);

    protected SvgUnit NormalizeUnit(SvgUnit orig)
    {
        return orig.Type == SvgUnitType.Percentage && GradientUnits == SvgCoordinateUnits.ObjectBoundingBox
            ? new SvgUnit(SvgUnitType.User, orig.Value / 100f)
            : orig;
    }

    protected Matrix3x2 GetEffectiveGradientTransform(Rect bounds, bool usesObjectBounds)
    {
        var transform = EffectiveGradientTransform;
        transform = Matrix3x2.CreateTranslation((float)bounds.X, (float)bounds.Y) * transform;

        if (usesObjectBounds)
        {
            transform = Matrix3x2.CreateScale((float)bounds.Width, (float)bounds.Height) * transform;
        }

        return transform;
    }

    protected static Point TransformPoint(Point point, Matrix3x2 matrix)
    {
        var transformed = Vector2.Transform(new Vector2((float)point.X, (float)point.Y), matrix);
        return new Point(transformed.X, transformed.Y);
    }

    protected static Point TransformVector(Point vector, Matrix3x2 matrix)
    {
        return new Point(
            vector.X * matrix.M11 + vector.Y * matrix.M21,
            vector.X * matrix.M12 + vector.Y * matrix.M22);
    }
}
