using System.Numerics;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Resources;

/// <summary>
/// Turns SVG markup into a <see cref="SimpleSvgDocument"/>. Reads the subset that reduces to plain
/// fills, strokes and clips; anything else is skipped.
/// </summary>
internal static class SimpleSvgReader
{
    // Where a paint value came from, which decides whether Tint may replace it.
    private enum PaintOrigin
    {
        /// <summary>Nobody declared it, so the initial value applies.</summary>
        Initial,

        /// <summary>The root element declared it.</summary>
        Root,

        /// <summary>A shape or an intermediate group declared it.</summary>
        Local,
    }

    private readonly struct Paint
    {
        public required PaintOrigin Origin { get; init; }

        /// <summary>Null when the paint is <c>none</c>.</summary>
        public Brush? Brush { get; init; }

        /// <summary>True when the brush is a plain color, which is what a tint can stand in for.</summary>
        public bool IsSolid { get; init; }
    }

    private struct State
    {
        public Paint Fill;
        public double FillOpacity;
        public FillRule FillRule;
        public Paint Stroke;
        public double StrokeOpacity;
        public double StrokeWidth;
        public StrokeLineCap LineCap;
        public StrokeLineJoin LineJoin;
        public double MiterLimit;
        public double[]? Dash;
        public double DashOffset;
        public bool StrokeUnderFill;
    }

    private sealed class Context
    {
        public required Dictionary<string, SimpleSvgElement> Definitions { get; init; }

        public List<SimpleSvgCommand> Commands { get; } = [];

        /// <summary>Guards against a clip path that references itself through another definition.</summary>
        public HashSet<string> ResolvingClips { get; } = [];
    }

    /// <summary>Reads markup. Throws <see cref="FormatException"/> when it is malformed.</summary>
    public static SimpleSvgDocument Read(string markup)
    {
        var root = SimpleSvgMarkupParser.Parse(markup);
        if (!string.Equals(root.Name, "svg", StringComparison.Ordinal))
        {
            throw new FormatException($"Root element is '{root.Name}', not 'svg'.");
        }

        ReadCoordinateSystem(root, out Rect viewBox, out Size intrinsicSize);

        var context = new Context { Definitions = CollectDefinitions(root) };
        var state = InitialState();
        ApplyAttributes(root, ref state, PaintOrigin.Root, context);

        foreach (var child in root.Children)
        {
            Walk(child, state, context);
        }

        return new SimpleSvgDocument(viewBox, intrinsicSize, [.. context.Commands]);
    }

    // The root's viewBox gives the coordinate system; width and height give the intrinsic size. Either
    // may be missing, and Material Symbols puts a negative origin in the viewBox.
    private static void ReadCoordinateSystem(SimpleSvgElement root, out Rect viewBox, out Size intrinsicSize)
    {
        bool hasWidth = SimpleSvgValueParser.TryLength(root.Attribute("width") ?? string.Empty, out double width);
        bool hasHeight = SimpleSvgValueParser.TryLength(root.Attribute("height") ?? string.Empty, out double height);

        var box = SimpleSvgValueParser.Numbers(root.Attribute("viewbox") ?? string.Empty);
        if (box.Length >= 4 && box[2] > 0 && box[3] > 0)
        {
            viewBox = new Rect(box[0], box[1], box[2], box[3]);
            intrinsicSize = hasWidth && hasHeight && width > 0 && height > 0
                ? new Size(width, height)
                : new Size(box[2], box[3]);
            return;
        }

        if (hasWidth && hasHeight && width > 0 && height > 0)
        {
            viewBox = new Rect(0, 0, width, height);
            intrinsicSize = new Size(width, height);
            return;
        }

        throw new FormatException("The root element declares neither a usable viewBox nor width and height.");
    }

    private static State InitialState() => new()
    {
        Fill = new Paint { Origin = PaintOrigin.Initial, Brush = new SolidColorBrush(Color.Black), IsSolid = true },
        FillOpacity = 1.0,
        FillRule = FillRule.NonZero,
        Stroke = new Paint { Origin = PaintOrigin.Initial, Brush = null, IsSolid = false },
        StrokeOpacity = 1.0,
        StrokeWidth = 1.0,
        LineCap = StrokeLineCap.Flat,
        LineJoin = StrokeLineJoin.Miter,
        MiterLimit = 4.0,
        Dash = null,
        DashOffset = 0.0,
        StrokeUnderFill = false,
    };

    private static Dictionary<string, SimpleSvgElement> CollectDefinitions(SimpleSvgElement root)
    {
        // Definitions may sit after the shapes that reference them, so they are indexed up front.
        var definitions = new Dictionary<string, SimpleSvgElement>(StringComparer.Ordinal);
        Collect(root, definitions);
        return definitions;
    }

    private static void Collect(SimpleSvgElement element, Dictionary<string, SimpleSvgElement> definitions)
    {
        string? id = element.Attribute("id");
        if (!string.IsNullOrEmpty(id))
        {
            definitions.TryAdd(id, element);
        }

        foreach (var child in element.Children)
        {
            Collect(child, definitions);
        }
    }

    private static void Walk(SimpleSvgElement element, State inherited, Context context)
    {
        if (IsSkipped(element.Name))
        {
            return;
        }

        var state = inherited;
        ApplyAttributes(element, ref state, PaintOrigin.Local, context);

        if (string.Equals(element.Attribute("display"), "none", StringComparison.OrdinalIgnoreCase) ||
            HasDeclaration(element, "display", "none"))
        {
            return;
        }

        var scope = BuildScope(element, context);
        if (scope != null)
        {
            context.Commands.Add(scope.Value);
        }

        if (string.Equals(element.Name, "g", StringComparison.Ordinal) ||
            string.Equals(element.Name, "svg", StringComparison.Ordinal))
        {
            foreach (var child in element.Children)
            {
                Walk(child, state, context);
            }
        }
        else
        {
            var geometry = BuildGeometry(element);
            if (geometry != null && !geometry.IsEmpty)
            {
                geometry.Freeze();
                context.Commands.Add(BuildDraw(geometry, state));
            }
        }

        if (scope != null)
        {
            context.Commands.Add(new SimpleSvgCommand
            {
                Kind = SimpleSvgCommandKind.EndGroup,
                Opacity = scope.Value.Opacity,
            });
        }
    }

    // `defs` holds definitions rather than drawings, and the rest are either metadata or outside the
    // accepted subset. A prefixed name is an editor's own element, never SVG.
    private static bool IsSkipped(string name) =>
        name.Contains(':', StringComparison.Ordinal) ||
        name is "defs" or "title" or "desc" or "metadata" or "style" or
                "clippath" or "mask" or "filter" or "pattern" or "marker" or
                "text" or "tspan" or "image" or "use" or "symbol" or
                "lineargradient" or "radialgradient" or "stop" or
                "animate" or "animatetransform" or "animatemotion" or "set" or "switch";

    // A scope is only emitted when there is something to scope, so an ordinary group costs nothing.
    private static SimpleSvgCommand? BuildScope(SimpleSvgElement element, Context context)
    {
        Matrix3x2? transform = null;
        string? transformText = element.Attribute("transform");
        if (!string.IsNullOrEmpty(transformText))
        {
            var matrix = SimpleSvgValueParser.Transform(transformText);
            if (!matrix.IsIdentity)
            {
                transform = matrix;
            }
        }

        double opacity = 1.0;
        string? opacityText = Declared(element, "opacity");
        if (!string.IsNullOrEmpty(opacityText))
        {
            opacity = SimpleSvgValueParser.Unit(opacityText, 1.0);
        }

        var clip = ResolveClip(Declared(element, "clip-path"), context);

        if (transform == null && clip == null && opacity >= 1.0)
        {
            return null;
        }

        return new SimpleSvgCommand
        {
            Kind = SimpleSvgCommandKind.BeginGroup,
            Transform = transform,
            Clip = clip,
            Opacity = opacity,
        };
    }

    private static PathGeometry? ResolveClip(string? value, Context context)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        string? id = SimpleSvgValueParser.Reference(value);
        if (id == null ||
            !context.Definitions.TryGetValue(id, out var definition) ||
            !string.Equals(definition.Name, "clippath", StringComparison.Ordinal) ||
            !context.ResolvingClips.Add(id))
        {
            return null;
        }

        try
        {
            var combined = new PathGeometry();
            foreach (var child in definition.Children)
            {
                var part = BuildGeometry(child);
                if (part == null || part.IsEmpty)
                {
                    continue;
                }

                string? transformText = child.Attribute("transform");
                if (!string.IsNullOrEmpty(transformText))
                {
                    part = part.Transform(SimpleSvgValueParser.Transform(transformText));
                }

                combined.AddPath(part);
            }

            if (combined.IsEmpty)
            {
                return null;
            }

            combined.Freeze();
            return combined;
        }
        finally
        {
            context.ResolvingClips.Remove(id);
        }
    }

    private static SimpleSvgCommand BuildDraw(PathGeometry geometry, in State state)
    {
        Brush? fill = Scale(state.Fill.Brush, state.FillOpacity);
        Brush? strokeBrush = Scale(state.Stroke.Brush, state.StrokeOpacity);

        Pen? pen = null;
        if (strokeBrush != null && state.StrokeWidth > 0)
        {
            var style = new StrokeStyle
            {
                LineCap = state.LineCap,
                LineJoin = state.LineJoin,
                MiterLimit = state.MiterLimit,
                DashArray = state.Dash,
                DashOffset = state.DashOffset,
            };
            pen = new Pen(strokeBrush, state.StrokeWidth, style);
        }

        return new SimpleSvgCommand
        {
            Kind = SimpleSvgCommandKind.Draw,
            Geometry = geometry,
            Fill = fill,
            FillRule = state.FillRule,
            Stroke = pen,
            StrokeUnderFill = state.StrokeUnderFill,
            FillFromRoot = fill != null && state.Fill.IsSolid && state.Fill.Origin != PaintOrigin.Local,
            StrokeFromRoot = pen != null && state.Stroke.IsSolid && state.Stroke.Origin != PaintOrigin.Local,
        };
    }

    // fill-opacity and stroke-opacity multiply into the paint's alpha rather than opening a layer.
    private static Brush? Scale(Brush? brush, double opacity)
    {
        if (brush == null || opacity >= 1.0)
        {
            return brush;
        }

        if (opacity <= 0.0)
        {
            return null;
        }

        if (brush is SolidColorBrush solid)
        {
            var color = solid.Color;
            return new SolidColorBrush(color.WithAlpha((byte)Math.Round(color.A * opacity)));
        }

        if (brush is LinearGradientBrush linear)
        {
            return new LinearGradientBrush(
                linear.StartPoint, linear.EndPoint, ScaleStops(linear.Stops, opacity),
                linear.SpreadMethod, linear.GradientUnits, linear.GradientTransform);
        }

        if (brush is RadialGradientBrush radial)
        {
            return new RadialGradientBrush(
                radial.Center, radial.GradientOrigin, radial.RadiusX, radial.RadiusY,
                ScaleStops(radial.Stops, opacity),
                radial.SpreadMethod, radial.GradientUnits, radial.GradientTransform);
        }

        return brush;
    }

    private static GradientStop[] ScaleStops(IReadOnlyList<GradientStop> stops, double opacity)
    {
        var scaled = new GradientStop[stops.Count];
        for (int i = 0; i < stops.Count; i++)
        {
            var color = stops[i].Color;
            scaled[i] = new GradientStop(stops[i].Offset, color.WithAlpha((byte)Math.Round(color.A * opacity)));
        }

        return scaled;
    }

    private static PathGeometry? BuildGeometry(SimpleSvgElement element)
    {
        switch (element.Name)
        {
            case "path":
            {
                string? data = element.Attribute("d");
                return string.IsNullOrWhiteSpace(data) ? null : PathGeometry.Parse(data);
            }

            case "rect":
            {
                double x = Length(element, "x");
                double y = Length(element, "y");
                double width = Length(element, "width");
                double height = Length(element, "height");
                if (width <= 0 || height <= 0)
                {
                    return null;
                }

                bool hasRx = SimpleSvgValueParser.TryLength(element.Attribute("rx") ?? string.Empty, out double rx);
                bool hasRy = SimpleSvgValueParser.TryLength(element.Attribute("ry") ?? string.Empty, out double ry);
                if (!hasRx && hasRy) rx = ry;
                if (!hasRy && hasRx) ry = rx;

                rx = Math.Clamp(rx, 0, width / 2);
                ry = Math.Clamp(ry, 0, height / 2);

                return rx > 0 && ry > 0
                    ? PathGeometry.FromRoundedRect(new Rect(x, y, width, height), rx, ry)
                    : PathGeometry.FromRect(x, y, width, height);
            }

            case "circle":
            {
                double r = Length(element, "r");
                return r > 0 ? PathGeometry.FromCircle(Length(element, "cx"), Length(element, "cy"), r) : null;
            }

            case "ellipse":
            {
                double rx = Length(element, "rx");
                double ry = Length(element, "ry");
                return rx > 0 && ry > 0
                    ? PathGeometry.FromEllipse(Length(element, "cx"), Length(element, "cy"), rx, ry)
                    : null;
            }

            case "line":
            {
                var line = new PathGeometry();
                line.MoveTo(Length(element, "x1"), Length(element, "y1"));
                line.LineTo(Length(element, "x2"), Length(element, "y2"));
                return line;
            }

            case "polygon":
            case "polyline":
                return BuildPolyline(
                    SimpleSvgValueParser.Numbers(element.Attribute("points") ?? string.Empty),
                    close: string.Equals(element.Name, "polygon", StringComparison.Ordinal));

            default:
                return null;
        }
    }

    private static PathGeometry? BuildPolyline(double[] points, bool close)
    {
        if (points.Length < 4)
        {
            return null;
        }

        var geometry = new PathGeometry();
        geometry.MoveTo(points[0], points[1]);
        for (int i = 2; i + 1 < points.Length; i += 2)
        {
            geometry.LineTo(points[i], points[i + 1]);
        }

        if (close)
        {
            geometry.Close();
        }

        return geometry;
    }

    private static double Length(SimpleSvgElement element, string name) =>
        SimpleSvgValueParser.TryLength(element.Attribute(name) ?? string.Empty, out double value) ? value : 0;

    // Presentation attributes first, then the inline style, which wins.
    private static void ApplyAttributes(SimpleSvgElement element, ref State state, PaintOrigin origin, Context context)
    {
        foreach (var pair in element.Attributes)
        {
            if (!pair.Key.Contains(':', StringComparison.Ordinal))
            {
                ApplyDeclaration(pair.Key, pair.Value, ref state, origin, context);
            }
        }

        string? style = element.Attribute("style");
        if (string.IsNullOrEmpty(style))
        {
            return;
        }

        foreach (var (name, value) in SimpleSvgValueParser.Declarations(style))
        {
            ApplyDeclaration(name, value, ref state, origin, context);
        }
    }

    private static void ApplyDeclaration(string name, string value, ref State state, PaintOrigin origin, Context context)
    {
        switch (name)
        {
            case "fill":
                state.Fill = ReadPaint(value, origin, context);
                break;

            case "fill-opacity":
                state.FillOpacity = SimpleSvgValueParser.Unit(value, state.FillOpacity);
                break;

            case "fill-rule":
                state.FillRule = string.Equals(value.Trim(), "evenodd", StringComparison.OrdinalIgnoreCase)
                    ? FillRule.EvenOdd
                    : FillRule.NonZero;
                break;

            case "stroke":
                state.Stroke = ReadPaint(value, origin, context);
                break;

            case "stroke-opacity":
                state.StrokeOpacity = SimpleSvgValueParser.Unit(value, state.StrokeOpacity);
                break;

            case "stroke-width":
                if (SimpleSvgValueParser.TryLength(value, out double width))
                {
                    state.StrokeWidth = Math.Max(0, width);
                }
                break;

            case "stroke-linecap":
                state.LineCap = value.Trim().ToLowerInvariant() switch
                {
                    "round" => StrokeLineCap.Round,
                    "square" => StrokeLineCap.Square,
                    _ => StrokeLineCap.Flat,
                };
                break;

            case "stroke-linejoin":
                state.LineJoin = value.Trim().ToLowerInvariant() switch
                {
                    "round" => StrokeLineJoin.Round,
                    "bevel" => StrokeLineJoin.Bevel,
                    _ => StrokeLineJoin.Miter,
                };
                break;

            case "stroke-miterlimit":
                if (SimpleSvgValueParser.TryNumber(value, out double limit) && limit >= 1)
                {
                    state.MiterLimit = limit;
                }
                break;

            case "stroke-dasharray":
            {
                var dashes = SimpleSvgValueParser.Numbers(value);
                state.Dash = dashes.Length > 0 && Array.Exists(dashes, d => d > 0) ? dashes : null;
                break;
            }

            case "stroke-dashoffset":
                if (SimpleSvgValueParser.TryLength(value, out double dashOffset))
                {
                    state.DashOffset = dashOffset;
                }
                break;

            case "paint-order":
                state.StrokeUnderFill = value.TrimStart().StartsWith("stroke", StringComparison.OrdinalIgnoreCase);
                break;
        }
    }

    private static Paint ReadPaint(string value, PaintOrigin origin, Context context)
    {
        var text = value.AsSpan().Trim();

        if (text.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return new Paint { Origin = origin, Brush = null, IsSolid = false };
        }

        string? id = SimpleSvgValueParser.Reference(text);
        if (id != null)
        {
            var gradient = ReadGradient(id, context);
            return new Paint { Origin = origin, Brush = gradient, IsSolid = false };
        }

        if (SimpleSvgValueParser.TryColor(text, out var color))
        {
            return new Paint { Origin = origin, Brush = new SolidColorBrush(color), IsSolid = true };
        }

        // An unreadable value leaves the inherited paint in place, which is what SVG does too.
        return new Paint { Origin = origin, Brush = new SolidColorBrush(Color.Black), IsSolid = true };
    }

    private static Brush? ReadGradient(string id, Context context)
    {
        if (!context.Definitions.TryGetValue(id, out var element))
        {
            return null;
        }

        bool isLinear = string.Equals(element.Name, "lineargradient", StringComparison.Ordinal);
        bool isRadial = string.Equals(element.Name, "radialgradient", StringComparison.Ordinal);
        if (!isLinear && !isRadial)
        {
            return null;
        }

        var stops = ReadStops(element);
        if (stops.Length == 0)
        {
            return null;
        }

        var units = string.Equals(element.Attribute("gradientunits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase)
            ? GradientUnits.UserSpaceOnUse
            : GradientUnits.ObjectBoundingBox;

        var spread = (element.Attribute("spreadmethod") ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "reflect" => SpreadMethod.Reflect,
            "repeat" => SpreadMethod.Repeat,
            _ => SpreadMethod.Pad,
        };

        Matrix3x2? gradientTransform = null;
        string? transformText = element.Attribute("gradienttransform");
        if (!string.IsNullOrEmpty(transformText))
        {
            var matrix = SimpleSvgValueParser.Transform(transformText);
            if (!matrix.IsIdentity)
            {
                gradientTransform = matrix;
            }
        }

        if (isLinear)
        {
            double defaultEnd = units == GradientUnits.ObjectBoundingBox ? 1 : 0;
            return new LinearGradientBrush(
                new Point(Coordinate(element, "x1", 0), Coordinate(element, "y1", 0)),
                new Point(Coordinate(element, "x2", defaultEnd), Coordinate(element, "y2", 0)),
                stops, spread, units, gradientTransform);
        }

        double half = units == GradientUnits.ObjectBoundingBox ? 0.5 : 0;
        double centerX = Coordinate(element, "cx", half);
        double centerY = Coordinate(element, "cy", half);
        double radius = Coordinate(element, "r", half);
        double focalX = Coordinate(element, "fx", centerX);
        double focalY = Coordinate(element, "fy", centerY);

        return new RadialGradientBrush(
            new Point(centerX, centerY), new Point(focalX, focalY), radius, radius,
            stops, spread, units, gradientTransform);
    }

    private static double Coordinate(SimpleSvgElement element, string name, double fallback)
    {
        string? text = element.Attribute(name);
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }

        var span = text.AsSpan().Trim();
        if (span.EndsWith("%", StringComparison.Ordinal))
        {
            return SimpleSvgValueParser.TryNumber(span[..^1], out double percent) ? percent / 100.0 : fallback;
        }

        return SimpleSvgValueParser.TryNumber(span, out double value) ? value : fallback;
    }

    private static GradientStop[] ReadStops(SimpleSvgElement gradient)
    {
        var stops = new List<GradientStop>();
        foreach (var child in gradient.Children)
        {
            if (!string.Equals(child.Name, "stop", StringComparison.Ordinal))
            {
                continue;
            }

            double offset = SimpleSvgValueParser.Unit(child.Attribute("offset") ?? "0", 0);
            var color = Color.Black;
            double alpha = 1.0;

            string? colorText = child.Attribute("stop-color");
            if (!string.IsNullOrEmpty(colorText) && SimpleSvgValueParser.TryColor(colorText, out var parsed))
            {
                color = parsed;
            }

            string? opacityText = child.Attribute("stop-opacity");
            if (!string.IsNullOrEmpty(opacityText))
            {
                alpha = SimpleSvgValueParser.Unit(opacityText, 1.0);
            }

            string? style = child.Attribute("style");
            if (!string.IsNullOrEmpty(style))
            {
                foreach (var (name, value) in SimpleSvgValueParser.Declarations(style))
                {
                    if (name == "stop-color" && SimpleSvgValueParser.TryColor(value, out var styled))
                    {
                        color = styled;
                    }
                    else if (name == "stop-opacity")
                    {
                        alpha = SimpleSvgValueParser.Unit(value, alpha);
                    }
                }
            }

            stops.Add(new GradientStop(offset, color.WithAlpha((byte)Math.Round(color.A * alpha))));
        }

        return [.. stops];
    }

    private static string? Declared(SimpleSvgElement element, string name)
    {
        string? fromStyle = null;
        string? style = element.Attribute("style");
        if (!string.IsNullOrEmpty(style))
        {
            foreach (var (declaration, value) in SimpleSvgValueParser.Declarations(style))
            {
                if (string.Equals(declaration, name, StringComparison.Ordinal))
                {
                    fromStyle = value;
                }
            }
        }

        return fromStyle ?? element.Attribute(name);
    }

    private static bool HasDeclaration(SimpleSvgElement element, string name, string value) =>
        string.Equals(Declared(element, name), value, StringComparison.OrdinalIgnoreCase);
}
