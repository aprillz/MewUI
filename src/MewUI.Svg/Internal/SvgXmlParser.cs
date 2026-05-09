using System.Globalization;
using System.Xml.Linq;

namespace Aprillz.MewUI.Svg.Internal;

/// <summary>
/// Parses an SVG XML document into a <see cref="SvgDocumentNode"/> tree.
/// </summary>
internal static class SvgXmlParser
{
    private static readonly XNamespace SvgNs   = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";

    public static SvgDocumentNode Parse(XDocument doc)
    {
        var svgRoot = doc.Root ?? throw new InvalidOperationException("Empty XML document.");
        var node = new SvgDocumentNode();
        ParseViewport(svgRoot, node);
        ReadStyle(svgRoot, node.Style);
        ParseChildren(svgRoot, node.Children, node.Defs);
        CollectDefs(node.Children, node.Defs);
        ResolveGradientHrefs(node.Defs);
        return node;
    }

    private static void ResolveGradientHrefs(Dictionary<string, SvgNode> defs)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, node) in defs)
        {
            if (node is SvgGradientNode gradient && gradient.Stops.Count == 0 && gradient.Href != null)
            {
                visiting.Clear();
                visiting.Add(id);
                InheritStops(gradient, defs, visiting);
            }
        }
    }

    private static void InheritStops(SvgGradientNode gradient, Dictionary<string, SvgNode> defs, HashSet<string> visiting)
    {
        if (gradient.Href == null) return;
        if (!visiting.Add(gradient.Href)) return;
        if (!defs.TryGetValue(gradient.Href, out var target)) return;
        if (target is not SvgGradientNode parent) return;

        if (parent.Stops.Count == 0 && parent.Href != null)
            InheritStops(parent, defs, visiting);

        foreach (var stop in parent.Stops)
            gradient.Stops.Add(stop);
    }

    // ──────────────────────────────────────────────
    // Viewport / viewBox
    // ──────────────────────────────────────────────

    private static void ParseViewport(XElement el, SvgDocumentNode node)
    {
        node.Width  = ParseOptionalLength(el, "width");
        node.Height = ParseOptionalLength(el, "height");

        var vb = el.Attribute("viewBox")?.Value;
        if (vb != null)
        {
            var nums = ParseNumberList(vb);
            if (nums.Length >= 4)
            {
                node.VbX = nums[0]; node.VbY = nums[1];
                node.VbW = nums[2]; node.VbH = nums[3];
            }
        }
    }

    // ──────────────────────────────────────────────
    // Child element parsing
    // ──────────────────────────────────────────────

    private static void ParseChildren(XElement parent, List<SvgNode> children, Dictionary<string, SvgNode> defs)
    {
        foreach (var el in parent.Elements())
        {
            var node = ParseElement(el, defs);
            if (node != null) children.Add(node);
        }
    }

    private static SvgNode? ParseElement(XElement el, Dictionary<string, SvgNode> defs)
    {
        // Strip namespace
        string localName = el.Name.LocalName;

        SvgNode? node = localName switch
        {
            "g"              => ParseGroup(el, defs),
            "svg"            => ParseInnerSvg(el, defs),
            "defs"           => ParseDefs(el, defs),
            "symbol"         => ParseSymbol(el, defs),
            "use"            => ParseUse(el),
            "path"           => ParsePath(el),
            "rect"           => ParseRect(el),
            "circle"         => ParseCircle(el),
            "ellipse"        => ParseEllipse(el),
            "line"           => ParseLine(el),
            "polyline"       => ParsePolyline(el, closed: false),
            "polygon"        => ParsePolyline(el, closed: true),
            "linearGradient" => ParseLinearGradient(el, defs),
            "radialGradient" => ParseRadialGradient(el, defs),
            _                => null,
        };

        if (node == null) return null;

        ReadCommon(el, node);
        return node;
    }

    private static SvgGroupNode ParseGroup(XElement el, Dictionary<string, SvgNode> defs)
    {
        var g = new SvgGroupNode();
        ParseChildren(el, g.Children, defs);
        return g;
    }

    private static SvgGroupNode ParseInnerSvg(XElement el, Dictionary<string, SvgNode> defs)
    {
        // Treat nested <svg> as a group (simplified — full viewport handling omitted)
        var g = new SvgGroupNode();
        ParseChildren(el, g.Children, defs);
        return g;
    }

    private static SvgDefsNode ParseDefs(XElement el, Dictionary<string, SvgNode> defs)
    {
        var defsNode = new SvgDefsNode();
        ParseChildren(el, defsNode.Children, defs);
        return defsNode;
    }

    private static SvgSymbolNode ParseSymbol(XElement el, Dictionary<string, SvgNode> defs)
    {
        var sym = new SvgSymbolNode();
        var vb = el.Attribute("viewBox")?.Value;
        if (vb != null)
        {
            var nums = ParseNumberList(vb);
            if (nums.Length >= 4) { sym.VbX = nums[0]; sym.VbY = nums[1]; sym.VbW = nums[2]; sym.VbH = nums[3]; }
        }
        ParseChildren(el, sym.Children, defs);
        return sym;
    }

    private static SvgUseNode ParseUse(XElement el)
    {
        var use = new SvgUseNode
        {
            Href   = (el.Attribute("href") ?? el.Attribute(XLinkNs + "href"))?.Value?.TrimStart('#'),
            X      = ParseDouble(el, "x"),
            Y      = ParseDouble(el, "y"),
            Width  = ParseDouble(el, "width"),
            Height = ParseDouble(el, "height"),
        };
        return use;
    }

    private static SvgPathNode ParsePath(XElement el)
        => new() { D = el.Attribute("d")?.Value };

    private static SvgRectNode ParseRect(XElement el)
    {
        double rx = ParseDouble(el, "rx", -1);
        double ry = ParseDouble(el, "ry", -1);
        if (rx < 0 && ry >= 0) rx = ry;
        if (ry < 0 && rx >= 0) ry = rx;
        if (rx < 0) rx = 0;
        if (ry < 0) ry = 0;
        return new SvgRectNode
        {
            X = ParseDouble(el, "x"), Y = ParseDouble(el, "y"),
            Width = ParseDouble(el, "width"), Height = ParseDouble(el, "height"),
            Rx = rx, Ry = ry,
        };
    }

    private static SvgCircleNode ParseCircle(XElement el)
        => new() { Cx = ParseDouble(el, "cx"), Cy = ParseDouble(el, "cy"), R = ParseDouble(el, "r") };

    private static SvgEllipseNode ParseEllipse(XElement el)
        => new() { Cx = ParseDouble(el, "cx"), Cy = ParseDouble(el, "cy"), Rx = ParseDouble(el, "rx"), Ry = ParseDouble(el, "ry") };

    private static SvgLineNode ParseLine(XElement el)
        => new() { X1 = ParseDouble(el, "x1"), Y1 = ParseDouble(el, "y1"), X2 = ParseDouble(el, "x2"), Y2 = ParseDouble(el, "y2") };

    private static SvgPolylineNode ParsePolyline(XElement el, bool closed)
    {
        var node = new SvgPolylineNode { Closed = closed };
        var pts = el.Attribute("points")?.Value;
        if (pts != null)
        {
            var nums = ParseNumberList(pts);
            for (int i = 0; i + 1 < nums.Length; i += 2)
                node.Points.Add((nums[i], nums[i + 1]));
        }
        return node;
    }

    private static SvgLinearGradientNode ParseLinearGradient(XElement el, Dictionary<string, SvgNode> defs)
    {
        var g = new SvgLinearGradientNode
        {
            X1 = ParseDouble(el, "x1", 0), Y1 = ParseDouble(el, "y1", 0),
            X2 = ParseDouble(el, "x2", 1), Y2 = ParseDouble(el, "y2", 0),
            Href = (el.Attribute("href") ?? el.Attribute(XLinkNs + "href"))?.Value?.TrimStart('#'),
        };
        ReadCommonGradientAttributes(el, g);
        ParseGradientStops(el, g, defs);
        return g;
    }

    private static SvgRadialGradientNode ParseRadialGradient(XElement el, Dictionary<string, SvgNode> defs)
    {
        var g = new SvgRadialGradientNode
        {
            Cx = ParseDouble(el, "cx", 0.5), Cy = ParseDouble(el, "cy", 0.5), R = ParseDouble(el, "r", 0.5),
            Fx = ParseDouble(el, "fx", double.NaN), Fy = ParseDouble(el, "fy", double.NaN),
            Href = (el.Attribute("href") ?? el.Attribute(XLinkNs + "href"))?.Value?.TrimStart('#'),
        };
        if (double.IsNaN(g.Fx)) g.Fx = g.Cx;
        if (double.IsNaN(g.Fy)) g.Fy = g.Cy;
        ReadCommonGradientAttributes(el, g);
        ParseGradientStops(el, g, defs);
        return g;
    }

    private static void ReadCommonGradientAttributes(XElement el, SvgGradientNode gradient)
    {
        var units = el.Attribute("gradientUnits")?.Value;
        if (string.Equals(units, "userSpaceOnUse", StringComparison.Ordinal))
            gradient.Units = Rendering.GradientUnits.UserSpaceOnUse;
        else
            gradient.Units = Rendering.GradientUnits.ObjectBoundingBox;

        gradient.Spread = el.Attribute("spreadMethod")?.Value switch
        {
            "reflect" => Rendering.SpreadMethod.Reflect,
            "repeat" => Rendering.SpreadMethod.Repeat,
            _ => Rendering.SpreadMethod.Pad,
        };

        var xform = el.Attribute("gradientTransform")?.Value;
        if (!string.IsNullOrWhiteSpace(xform))
            gradient.Transform = SvgTransformParser.Parse(xform.AsSpan());
    }

    private static void ParseGradientStops(XElement el, SvgGradientNode gradient, Dictionary<string, SvgNode> defs)
    {
        foreach (var stop in el.Elements().Where(e => e.Name.LocalName == "stop"))
        {
            double offset = 0;
            var offsetAttr = stop.Attribute("offset")?.Value?.Trim();
            if (offsetAttr != null)
            {
                if (offsetAttr.EndsWith('%'))
                    double.TryParse(offsetAttr[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
                else
                    double.TryParse(offsetAttr, NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
                if (offsetAttr.EndsWith('%')) offset /= 100.0;
            }

            // Parse stop-color and stop-opacity from style or attributes
            var style = ParseInlineStyle(stop.Attribute("style")?.Value ?? "");
            var colorStr = style.GetValueOrDefault("stop-color") ?? stop.Attribute("stop-color")?.Value ?? "black";
            var opacityStr = style.GetValueOrDefault("stop-opacity") ?? stop.Attribute("stop-opacity")?.Value;
            double opacity = 1;
            if (opacityStr != null) double.TryParse(opacityStr, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity);

            if (!SvgColorParser.TryParseColor(colorStr.AsSpan(), out var color))
                color = new Color(0, 0, 0);

            gradient.Stops.Add(new SvgGradientStop { Offset = offset, Color = color, Opacity = opacity });
        }
    }

    // ──────────────────────────────────────────────
    // Common attribute reading
    // ──────────────────────────────────────────────

    private static void ReadCommon(XElement el, SvgNode node)
    {
        node.Id = el.Attribute("id")?.Value;

        var xform = el.Attribute("transform")?.Value;
        if (xform != null) node.LocalTransform = SvgTransformParser.Parse(xform.AsSpan());

        ReadStyle(el, node.Style);
    }

    private static void ReadStyle(XElement el, SvgStyle style)
    {
        // Presentation attributes first, then inline style (which wins)
        ApplyPresentationAttr(el, style);

        var inlineStyle = el.Attribute("style")?.Value;
        if (inlineStyle != null)
            ApplyInlineStyle(inlineStyle, style);
    }

    private static void ApplyPresentationAttr(XElement el, SvgStyle style)
    {
        ApplyPaint(el, "fill",          v => style.Fill        = v);
        ApplyPaint(el, "stroke",        v => style.Stroke      = v);
        ApplyDouble(el, "stroke-width", v => style.StrokeWidth = v);
        ApplyDouble(el, "fill-opacity", v => style.FillOpacity = v);
        ApplyDouble(el, "stroke-opacity", v => style.StrokeOpacity = v);
        ApplyDouble(el, "opacity",      v => style.Opacity     = v);
        ApplyFillRule(el, "fill-rule",  style);
        ApplyVisibility(el, style);
    }

    private static void ApplyInlineStyle(string css, SvgStyle style)
    {
        foreach (var (key, value) in ParseInlineStyle(css))
        {
            switch (key)
            {
                case "fill":           ApplyPaintValue(value, v => style.Fill           = v); break;
                case "stroke":         ApplyPaintValue(value, v => style.Stroke         = v); break;
                case "stroke-width":   ApplyDoubleValue(value, v => style.StrokeWidth   = v); break;
                case "fill-opacity":   ApplyDoubleValue(value, v => style.FillOpacity   = v); break;
                case "stroke-opacity": ApplyDoubleValue(value, v => style.StrokeOpacity = v); break;
                case "opacity":        ApplyDoubleValue(value, v => style.Opacity        = v); break;
                case "fill-rule":
                    if (!value.Equals("inherit", StringComparison.OrdinalIgnoreCase))
                        style.FillRule = value;
                    break;
                case "display":
                    style.Display = !value.Equals("none", StringComparison.OrdinalIgnoreCase);
                    break;
                case "visibility":
                    if (!value.Equals("inherit", StringComparison.OrdinalIgnoreCase))
                        style.Visibility = value.Equals("visible", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }
    }

    private static void ApplyPaint(XElement el, string attr, Action<SvgPaint> set)
    {
        var v = el.Attribute(attr)?.Value;
        if (v == null) return;
        ApplyPaintValue(v, set);
    }

    private static void ApplyPaintValue(string v, Action<SvgPaint> set)
    {
        var paint = SvgColorParser.Parse(v.AsSpan());
        if (paint.HasValue) set(paint.Value);
    }

    private static void ApplyDouble(XElement el, string attr, Action<double> set)
    {
        var v = el.Attribute(attr)?.Value?.Trim();
        if (v == null || v.Equals("inherit", StringComparison.OrdinalIgnoreCase)) return;
        ApplyDoubleValue(v, set);
    }

    private static void ApplyDoubleValue(string v, Action<double> set)
    {
        // Strip trailing px/pt/%
        v = v.Trim();
        if (v.Equals("inherit", StringComparison.OrdinalIgnoreCase)) return;
        ReadOnlySpan<char> span = v.AsSpan().TrimEnd("xptm%e");
        if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            set(d);
    }

    private static void ApplyFillRule(XElement el, string attr, SvgStyle style)
    {
        var v = el.Attribute(attr)?.Value?.Trim();
        if (v != null && !v.Equals("inherit", StringComparison.OrdinalIgnoreCase))
            style.FillRule = v;
    }

    private static void ApplyVisibility(XElement el, SvgStyle style)
    {
        var display = el.Attribute("display")?.Value;
        if (display != null && !display.Equals("inherit", StringComparison.OrdinalIgnoreCase))
            style.Display = !display.Equals("none", StringComparison.OrdinalIgnoreCase);

        var vis = el.Attribute("visibility")?.Value;
        if (vis != null && !vis.Equals("inherit", StringComparison.OrdinalIgnoreCase))
            style.Visibility = vis.Equals("visible", StringComparison.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────
    // Defs collection (walk entire tree)
    // ──────────────────────────────────────────────

    private static void CollectDefs(List<SvgNode> children, Dictionary<string, SvgNode> defs)
    {
        foreach (var node in children)
        {
            if (node.Id != null)
                defs.TryAdd(node.Id, node);

            var childList = node switch
            {
                SvgGroupNode g   => g.Children,
                SvgDefsNode  d   => d.Children,
                SvgSymbolNode s  => s.Children,
                _                => null,
            };
            if (childList != null) CollectDefs(childList, defs);
        }
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static double ParseDouble(XElement el, string attr, double fallback = 0)
    {
        var v = el.Attribute(attr)?.Value?.Trim();
        if (v == null) return fallback;
        // Strip trailing unit suffixes (px, pt, %, etc.)
        var span = v.AsSpan().TrimEnd("xptm%e");
        return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d : fallback;
    }

    private static double? ParseOptionalLength(XElement el, string attr)
    {
        var v = el.Attribute(attr)?.Value?.Trim();
        if (v == null) return null;
        var span = v.AsSpan().TrimEnd("xptm%e");
        return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
    }

    private static double[] ParseNumberList(string s)
    {
        var result = new List<double>();
        var span = s.AsSpan();
        int pos = 0;
        while (pos < span.Length)
        {
            // Skip separators
            while (pos < span.Length && (span[pos] == ' ' || span[pos] == '\t' || span[pos] == ',' || span[pos] == '\r' || span[pos] == '\n'))
                pos++;
            if (pos >= span.Length) break;
            int start = pos;
            bool hasDot = false, hasE = false;
            if (pos < span.Length && (span[pos] == '-' || span[pos] == '+')) pos++;
            while (pos < span.Length)
            {
                char c = span[pos];
                if (char.IsDigit(c)) pos++;
                else if (c == '.' && !hasDot && !hasE) { hasDot = true; pos++; }
                else if ((c == 'e' || c == 'E') && !hasE) { hasE = true; pos++;
                    if (pos < span.Length && (span[pos] == '+' || span[pos] == '-')) pos++; }
                else break;
            }
            if (pos == start) { pos++; continue; }
            if (double.TryParse(span[start..pos], NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                result.Add(d);
        }
        return [.. result];
    }

    private static Dictionary<string, string> ParseInlineStyle(string css)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in css.Split(';'))
        {
            int colon = part.IndexOf(':');
            if (colon > 0)
                result[part[..colon].Trim()] = part[(colon + 1)..].Trim();
        }
        return result;
    }
}
