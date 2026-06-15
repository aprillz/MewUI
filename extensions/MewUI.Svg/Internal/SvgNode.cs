using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Svg.Internal;

// ──────────────────────────────────────────────
// Paint
// ──────────────────────────────────────────────

internal enum SvgPaintKind { Inherit, None, Color, Url, CurrentColor }

internal readonly struct SvgPaint
{
    public static readonly SvgPaint Inherit = new(SvgPaintKind.Inherit, default, null);
    public static readonly SvgPaint None = new(SvgPaintKind.None, default, null);
    public static readonly SvgPaint CurrentColor = new(SvgPaintKind.CurrentColor, default, null);

    public SvgPaintKind Kind { get; }
    public Color Color { get; }
    public string? Url { get; }

    public SvgPaint(SvgPaintKind kind, Color color, string? url)
    {
        Kind = kind; Color = color; Url = url;
    }

    public static SvgPaint FromColor(Color c) => new(SvgPaintKind.Color, c, null);
    public static SvgPaint FromUrl(string id) => new(SvgPaintKind.Url, default, id);
}

// ──────────────────────────────────────────────
// Computed (inherited) presentation state
// ──────────────────────────────────────────────

internal sealed class SvgStyle
{
    // Raw (un-resolved) values — null means "inherit"
    public SvgPaint? Fill;
    public SvgPaint? Stroke;
    public double? StrokeWidth;
    public double? FillOpacity;
    public double? StrokeOpacity;
    public double? Opacity;
    public bool? Display;   // false = don't render subtree
    public bool? Visibility; // false = invisible but still takes space (we skip drawing)
    public string? FillRule; // "nonzero" | "evenodd"

    public static readonly SvgStyle Default = new()
    {
        Fill = SvgPaint.FromColor(new Color(0, 0, 0)), // black
        Stroke = SvgPaint.None,
        StrokeWidth = 1,
        FillOpacity = 1,
        StrokeOpacity = 1,
        Opacity = 1,
        Display = true,
        Visibility = true,
        FillRule = "nonzero",
    };
}

// ──────────────────────────────────────────────
// Resolved style (all fields non-null)
// ──────────────────────────────────────────────

internal sealed class SvgResolvedStyle
{
    public SvgPaint Fill;
    public SvgPaint Stroke;
    public double StrokeWidth;
    public double FillOpacity;
    public double StrokeOpacity;
    public double Opacity;   // element-level multiplier
    public bool Display;
    public bool Visibility;
    public string FillRule = "nonzero";

    public SvgResolvedStyle Clone() => (SvgResolvedStyle)MemberwiseClone();

    /// <summary>Cascades parent style with child overrides.</summary>
    public static SvgResolvedStyle Cascade(SvgResolvedStyle parent, SvgStyle child)
    {
        var r = parent.Clone();
        if (child.Fill.HasValue) r.Fill = child.Fill.Value;
        if (child.Stroke.HasValue) r.Stroke = child.Stroke.Value;
        if (child.StrokeWidth.HasValue) r.StrokeWidth = child.StrokeWidth.Value;
        if (child.FillOpacity.HasValue) r.FillOpacity = child.FillOpacity.Value;
        if (child.StrokeOpacity.HasValue) r.StrokeOpacity = child.StrokeOpacity.Value;
        if (child.Opacity.HasValue) r.Opacity = child.Opacity.Value;
        if (child.Display.HasValue) r.Display = child.Display.Value;
        if (child.Visibility.HasValue) r.Visibility = child.Visibility.Value;
        if (child.FillRule != null) r.FillRule = child.FillRule;
        return r;
    }

    public static SvgResolvedStyle FromDefault() => new()
    {
        Fill = SvgPaint.FromColor(new Color(0, 0, 0)),
        Stroke = SvgPaint.None,
        StrokeWidth = 1,
        FillOpacity = 1,
        StrokeOpacity = 1,
        Opacity = 1,
        Display = true,
        Visibility = true,
        FillRule = "nonzero",
    };
}

// ──────────────────────────────────────────────
// Base node
// ──────────────────────────────────────────────

internal abstract class SvgNode
{
    public string? Id { get; set; }
    public SvgMatrix? LocalTransform { get; set; }
    public SvgStyle Style { get; } = new();
}

// ──────────────────────────────────────────────
// Container nodes
// ──────────────────────────────────────────────

internal sealed class SvgGroupNode : SvgNode
{
    public List<SvgNode> Children { get; } = new();
}

internal sealed class SvgDefsNode : SvgNode
{
    public List<SvgNode> Children { get; } = new();
}

internal sealed class SvgSymbolNode : SvgNode
{
    public List<SvgNode> Children { get; } = new();
    public double? VbX, VbY, VbW, VbH;
}

// ──────────────────────────────────────────────
// Shape nodes
// ──────────────────────────────────────────────

internal sealed class SvgPathNode : SvgNode
{
    public string? D { get; set; }
}

internal sealed class SvgRectNode : SvgNode
{
    public double X, Y, Width, Height, Rx, Ry;
}

internal sealed class SvgCircleNode : SvgNode
{
    public double Cx, Cy, R;
}

internal sealed class SvgEllipseNode : SvgNode
{
    public double Cx, Cy, Rx, Ry;
}

internal sealed class SvgLineNode : SvgNode
{
    public double X1, Y1, X2, Y2;
}

internal sealed class SvgPolylineNode : SvgNode
{
    public bool Closed { get; set; } // true = polygon
    public List<(double x, double y)> Points { get; } = new();
}

// ──────────────────────────────────────────────
// Use node
// ──────────────────────────────────────────────

internal sealed class SvgUseNode : SvgNode
{
    public string? Href { get; set; }
    public double X, Y, Width, Height;
}

// ──────────────────────────────────────────────
// Gradient nodes (for future fill resolution)
// ──────────────────────────────────────────────

internal sealed class SvgGradientStop
{
    public double Offset { get; set; }
    public Color Color { get; set; }
    public double Opacity { get; set; } = 1;
}

internal abstract class SvgGradientNode : SvgNode
{
    public List<SvgGradientStop> Stops { get; } = new();
    public string? Href { get; set; } // xlink:href to inherit stops
    public GradientUnits Units { get; set; } = GradientUnits.ObjectBoundingBox;
    public SpreadMethod Spread { get; set; } = SpreadMethod.Pad;
    public SvgMatrix? Transform { get; set; }

    /// <summary>Returns a representative solid color (midpoint of gradient).</summary>
    public Color GetRepresentativeColor()
    {
        if (Stops.Count == 0) return new Color(0, 0, 0);
        if (Stops.Count == 1) return Stops[0].Color;
        // Blend first and last
        var a = Stops[0];
        var b = Stops[^1];
        return a.Color.Lerp(b.Color, 0.5);
    }
}

internal sealed class SvgLinearGradientNode : SvgGradientNode
{
    public double X1 = 0, Y1 = 0, X2 = 1, Y2 = 0;
}

internal sealed class SvgRadialGradientNode : SvgGradientNode
{
    public double Cx = 0.5, Cy = 0.5, R = 0.5, Fx, Fy;
}

// ──────────────────────────────────────────────
// Root document
// ──────────────────────────────────────────────

internal sealed class SvgDocumentNode : SvgNode
{
    public List<SvgNode> Children { get; } = new();

    // Viewport
    public double? VbX, VbY, VbW, VbH;
    public double? Width, Height;

    /// <summary>All defs by id (gradients, symbols, etc.).</summary>
    public Dictionary<string, SvgNode> Defs { get; } = new(StringComparer.Ordinal);
}
