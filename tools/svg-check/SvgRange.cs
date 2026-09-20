namespace Aprillz.MewUI.SvgCheck;

/// <summary>
/// The element and attribute names <c>SimpleSvgSource</c> reads, and the ones that put a document
/// outside its range. Kept as names rather than shared code so the tool needs nothing from the core
/// assembly; the parity test cross-checks the two against each other.
/// </summary>
internal static class SvgRange
{
    /// <summary>Elements that are read, either as a shape or as part of the document's structure.</summary>
    public static readonly HashSet<string> Accepted = new(StringComparer.Ordinal)
    {
        "svg", "g", "defs", "path", "rect", "circle", "ellipse", "line", "polygon", "polyline",
        "lineargradient", "radialgradient", "stop", "clippath",
        "title", "desc", "metadata",
    };

    /// <summary>Elements that take a document out of range, named so a report can say which.</summary>
    public static readonly HashSet<string> Rejected = new(StringComparer.Ordinal)
    {
        "style", "filter", "mask", "pattern", "marker", "text", "tspan", "textpath",
        "image", "use", "symbol", "switch",
        "animate", "animatetransform", "animatemotion", "set",
        "fecolormatrix", "feblend", "feoffset", "fegaussianblur", "feflood", "fecomposite",
        "femerge", "femergenode", "fedropshadow", "feimage", "fetile", "feturbulence",
        "fediffuselighting", "fespecularlighting", "fedisplacementmap", "feconvolvematrix",
        "fecomponenttransfer", "femorphology",
    };

    /// <summary>Attributes that take a document out of range.</summary>
    public static readonly HashSet<string> RejectedAttributes = new(StringComparer.Ordinal)
    {
        "class", "filter", "mask",
    };

    /// <summary>
    /// Attributes and forms that are read but that few documents exercise. A report flags them so a
    /// new icon set's unusual files can be picked up as test fixtures instead of being hunted by hand.
    /// </summary>
    public static readonly HashSet<string> Uncommon = new(StringComparer.Ordinal)
    {
        "paint-order", "stroke-dasharray", "stroke-dashoffset", "spreadmethod",
        "fx", "fy", "clip-path", "skewx", "skewy",
    };
}
