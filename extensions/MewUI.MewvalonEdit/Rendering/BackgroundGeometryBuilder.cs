using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Builds a geometry for the background of a document segment. Rectangles that touch are joined
/// into one outline, so a selection spanning several lines is drawn as a single rounded shape
/// rather than a stack of boxes.
/// </summary>
public sealed class BackgroundGeometryBuilder
{
    private readonly PathGeometry _figures = new();
    private readonly List<Segment> _figure = [];
    private bool _hasFigure;
    private Point _figureStart;
    private int _insertionIndex;
    private double _lastTop, _lastBottom, _lastLeft, _lastRight;

    /// <summary>Radius of the rounded corners.</summary>
    public double CornerRadius { get; set; }

    /// <summary>
    /// Whether to align to whole pixels. With <see cref="BorderThickness"/> at 0 the geometry is
    /// aligned; with a non-zero thickness the outer edge of the border is.
    /// </summary>
    public bool AlignToWholePixels { get; set; }

    /// <summary>
    /// Border thickness the geometry will be stroked with. Only has an effect while
    /// <see cref="AlignToWholePixels"/> is set.
    /// </summary>
    public double BorderThickness { get; set; }

    /// <summary>Whether to extend the rectangles to full width at a line end.</summary>
    public bool ExtendToFullWidthAtLineEnd { get; set; }

    /// <summary>Adds the specified segment to the geometry.</summary>
    public void AddSegment(TextView textView, ISegment segment)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(segment);
        foreach (var rect in GetRectsForSegment(textView, segment, ExtendToFullWidthAtLineEnd))
        {
            AddRectangle(textView, rect);
        }
    }

    /// <summary>
    /// Adds a rectangle, aligning it as <see cref="AlignToWholePixels"/> asks. Use the
    /// four-coordinate overload when the coordinates are already aligned.
    /// </summary>
    public void AddRectangle(TextView textView, Rect rectangle)
    {
        ArgumentNullException.ThrowIfNull(textView);
        if (!AlignToWholePixels)
        {
            AddRectangle(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
            return;
        }

        // Rounded on the outer edge and offset back by half the border, so a stroke of that width
        // sits centred on a device pixel instead of straddling two. The edge is resolved in device
        // pixels rather than by rounding a DIP: an outer edge that falls on a whole pixel reaches
        // the rounding a couple of ulps below it once it has been through 1/scale, and lands on the
        // pixel before the intended one.
        double dpiScale = textView.DpiScale;
        double halfBorder = 0.5 * BorderThickness;
        AddRectangle(
            SnapOuterEdge(rectangle.Left, -halfBorder, dpiScale),
            SnapOuterEdge(rectangle.Top, -halfBorder, dpiScale),
            SnapOuterEdge(rectangle.Right, halfBorder, dpiScale),
            SnapOuterEdge(rectangle.Bottom, halfBorder, dpiScale));
    }

    // Pushes an edge out by the offset, snaps that outer edge to a whole device pixel, and returns
    // the stroke centre in DIP. Each term is converted to pixels before they are added: adding them
    // in DIP first leaves a sum that is a couple of ulps off, and at 150% or 175% that is enough to
    // round an edge onto the pixel before the one it sits on.
    private static double SnapOuterEdge(double edgeDip, double offsetDip, double dpiScale)
    {
        double offsetPx = offsetDip * dpiScale;
        double outerPx = Math.Round((edgeDip * dpiScale) + offsetPx, MidpointRounding.AwayFromZero);
        return (outerPx - offsetPx) / dpiScale;
    }

    /// <summary>
    /// Adds a rectangle whose coordinates are already aligned. A rectangle whose top meets the
    /// previous bottom continues that outline instead of starting a new one.
    /// </summary>
    public void AddRectangle(double left, double top, double right, double bottom)
    {
        // Two rows share a boundary, but each edge is snapped after being pushed out by half the
        // border, and those two roundings only land on the same value where half a border is half a
        // pixel. Off 100% they differ by up to a border, which is the distance that still counts as
        // the same boundary. Sharing no column ends the outline even so: a selection that starts
        // late on one line and continues onto a short one leaves two rows with nothing above each
        // other, and one outline through both would fold over itself.
        // The slack is compared with a tolerance of its own: the gap comes out at exactly one border
        // where the two roundings disagree, and neither side of that comparison is exact in binary.
        bool continues = Math.Abs(top - _lastBottom) <= Math.Max(0.01, BorderThickness) + 1e-6 &&
                         left < _lastRight && right > _lastLeft;
        if (!continues)
        {
            CloseFigure();
        }
        if (!_hasFigure)
        {
            _hasFigure = true;
            _figure.Clear();
            _figureStart = new Point(left, top + CornerRadius);
            if (Math.Abs(left - right) > CornerRadius)
            {
                _figure.Add(Segment.Arc(left + CornerRadius, top, clockwise: true));
                _figure.Add(Segment.Line(right - CornerRadius, top));
                _figure.Add(Segment.Arc(right, top + CornerRadius, clockwise: true));
            }
            _figure.Add(Segment.Line(right, bottom - CornerRadius));
            _insertionIndex = _figure.Count;
        }
        else
        {
            // The right edge grows downwards and the left edge upwards, so the segments of each go
            // in at the seam between them rather than at either end of the list.
            if (!IsClose(_lastRight, right))
            {
                double radius = right < _lastRight ? -CornerRadius : CornerRadius;
                bool inward = right < _lastRight;
                _figure.Insert(_insertionIndex++, Segment.Arc(_lastRight + radius, _lastBottom, inward));
                _figure.Insert(_insertionIndex++, Segment.Line(right - radius, top));
                _figure.Insert(_insertionIndex++, Segment.Arc(right, top + CornerRadius, !inward));
            }
            _figure.Insert(_insertionIndex++, Segment.Line(right, bottom - CornerRadius));
            _figure.Insert(_insertionIndex, Segment.Line(_lastLeft, _lastTop + CornerRadius));
            if (!IsClose(_lastLeft, left))
            {
                double radius = left < _lastLeft ? CornerRadius : -CornerRadius;
                bool outward = left < _lastLeft;
                _figure.Insert(_insertionIndex, Segment.Arc(_lastLeft, _lastBottom - CornerRadius, !outward));
                _figure.Insert(_insertionIndex, Segment.Line(_lastLeft - radius, _lastBottom));
                _figure.Insert(_insertionIndex, Segment.Arc(left + radius, _lastBottom, outward));
            }
        }
        _lastTop = top;
        _lastBottom = bottom;
        _lastLeft = left;
        _lastRight = right;
    }

    /// <summary>Closes the outline built so far, so the next rectangle starts a new one.</summary>
    public void CloseFigure()
    {
        if (!_hasFigure)
        {
            return;
        }

        _figure.Insert(_insertionIndex, Segment.Line(_lastLeft, _lastTop + CornerRadius));
        if (Math.Abs(_lastLeft - _lastRight) > CornerRadius)
        {
            _figure.Insert(_insertionIndex, Segment.Arc(_lastLeft, _lastBottom - CornerRadius, clockwise: true));
            _figure.Insert(_insertionIndex, Segment.Line(_lastLeft + CornerRadius, _lastBottom));
            _figure.Insert(_insertionIndex, Segment.Arc(_lastRight - CornerRadius, _lastBottom, clockwise: true));
        }

        _figures.MoveTo(_figureStart);
        foreach (var segment in _figure)
        {
            if (segment.IsArc)
            {
                _figures.SvgArcTo(CornerRadius, CornerRadius, 0, false, segment.Clockwise, segment.X, segment.Y);
            }
            else
            {
                _figures.LineTo(segment.X, segment.Y);
            }
        }
        _figures.Close();
        _figure.Clear();
        _hasFigure = false;
    }

    /// <summary>The geometry of everything added so far, or null when nothing was added.</summary>
    public PathGeometry? CreateGeometry()
    {
        CloseFigure();
        return _figures.IsEmpty ? null : _figures;
    }

    /// <summary>
    /// The rectangles the segment is shown in. Usually one per line inside the segment, but more
    /// where a line wraps or bidirectional text splits it.
    /// </summary>
    public static IEnumerable<Rect> GetRectsForSegment(
        TextView textView,
        ISegment segment,
        bool extendToFullWidthAtLineEnd = false)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(segment);
        return GetRectsForSegmentImpl(textView, segment, extendToFullWidthAtLineEnd);
    }

    private static IEnumerable<Rect> GetRectsForSegmentImpl(
        TextView textView, ISegment segment, bool extendToFullWidthAtLineEnd)
    {
        int segmentStart = Math.Clamp(segment.Offset, 0, textView.Document.TextLength);
        int segmentEnd = Math.Clamp(segment.Offset + segment.Length, 0, textView.Document.TextLength);
        var start = new TextViewPosition(textView.Document.GetLocation(segmentStart));
        var end = new TextViewPosition(textView.Document.GetLocation(segmentEnd));

        foreach (var visualLine in textView.VisualLines)
        {
            int lineStart = visualLine.StartOffset;
            if (lineStart > segmentEnd)
            {
                break;
            }
            int lineEnd = lineStart + visualLine.DocumentLength;
            if (lineEnd < segmentStart)
            {
                continue;
            }

            int segmentStartVC = segmentStart < lineStart
                ? 0
                : visualLine.ValidateVisualColumn(start, extendToFullWidthAtLineEnd);
            int segmentEndVC;
            if (segmentEnd > lineEnd)
            {
                segmentEndVC = extendToFullWidthAtLineEnd
                    ? int.MaxValue
                    : visualLine.VisualLengthWithEndOfLineMarker;
            }
            else
            {
                segmentEndVC = visualLine.ValidateVisualColumn(end, extendToFullWidthAtLineEnd);
            }

            foreach (var rect in ProcessTextLines(textView, visualLine, segmentStartVC, segmentEndVC))
            {
                yield return rect;
            }
        }
    }

    /// <summary>The rectangles of a visual column range, one per row the range covers.</summary>
    public static IEnumerable<Rect> GetRectsFromVisualSegment(
        TextView textView, VisualLine line, int startVC, int endVC)
    {
        ArgumentNullException.ThrowIfNull(textView);
        ArgumentNullException.ThrowIfNull(line);
        return ProcessTextLines(textView, line, startVC, endVC);
    }

    private static IEnumerable<Rect> ProcessTextLines(
        TextView textView, VisualLine visualLine, int segmentStartVC, int segmentEndVC)
    {
        var rows = visualLine.TextLines;
        var lastRow = rows[^1];
        var surface = textView.Surface;
        var viewport = surface.TextViewportBounds;
        double scrollX = surface.HorizontalOffset - viewport.X;
        double scrollY = surface.VerticalOffset - viewport.Y;
        var bounds = new List<Rect>();
        double lineLeft = visualLine.GetVisualXPosition(0);

        for (int index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var metrics = visualLine.GetTextLineMetrics(row);
            double y = visualLine.GetTextLineVisualYPosition(row, VisualYPosition.LineTop);
            int visualStartCol = visualLine.GetTextLineVisualStartColumn(row);
            // AvalonEdit takes one off the last row for the column its rows carry for the end of the
            // paragraph. Ours carry text only, so the row ends where its text does.
            int visualEndCol = visualStartCol + row.LogicalLength;
            if (!ReferenceEquals(row, lastRow))
            {
                visualEndCol -= metrics.TrailingWhitespaceLength;
            }

            if (segmentEndVC < visualStartCol)
            {
                break;
            }
            if (!ReferenceEquals(row, lastRow) && segmentStartVC > visualEndCol)
            {
                continue;
            }
            int segmentStartVCInLine = Math.Max(segmentStartVC, visualStartCol);
            int segmentEndVCInLine = Math.Min(segmentEndVC, visualEndCol);
            y -= scrollY;
            var lastRect = Rect.Empty;
            if (segmentStartVCInLine == segmentEndVCInLine)
            {
                // A zero-width range still has to produce a rectangle, or an empty line inside the
                // selection would show nothing. The two skips drop the duplicate a wrap boundary
                // would emit, since one offset maps to the end of a row and the start of the next.
                if (segmentEndVCInLine == visualEndCol && index < rows.Count - 1 &&
                    segmentEndVC > segmentEndVCInLine && metrics.TrailingWhitespaceLength == 0)
                {
                    continue;
                }
                if (segmentStartVCInLine == visualStartCol && index > 0 &&
                    segmentStartVC < segmentStartVCInLine &&
                    visualLine.GetTextLineMetrics(rows[index - 1]).TrailingWhitespaceLength == 0)
                {
                    continue;
                }
                double pos = visualLine.GetTextLineVisualXPosition(row, segmentStartVCInLine) - scrollX;
                lastRect = new Rect(pos, y, textView.EmptyLineSelectionWidth, row.Bounds.Height);
            }
            else if (segmentStartVCInLine <= visualEndCol)
            {
                bounds.Clear();
                visualLine.GetTextBounds(
                    row, segmentStartVCInLine, segmentEndVCInLine - segmentStartVCInLine, bounds);
                foreach (var bound in bounds)
                {
                    double left = lineLeft + bound.X - scrollX;
                    double right = left + bound.Width;
                    if (!lastRect.IsEmpty)
                    {
                        yield return lastRect;
                    }
                    // left > right is possible in right-to-left runs.
                    lastRect = new Rect(Math.Min(left, right), y, Math.Abs(right - left), row.Bounds.Height);
                }
            }

            // A range reaching past the row end continues into virtual space or into the next row,
            // and the rectangle has to reach that far with it.
            if (segmentEndVC > visualEndCol)
            {
                double left;
                if (segmentStartVC > visualLine.VisualLengthWithEndOfLineMarker)
                {
                    left = visualLine.GetTextLineVisualXPosition(lastRow, segmentStartVC);
                }
                else
                {
                    // Everything up to visualEndCol is already out, so only the remainder is left.
                    // A wrapped row's visualEndCol leaves out the whitespace the wrap hid, which has
                    // to be covered here; the last row's already includes it.
                    left = row.Bounds.X +
                        (ReferenceEquals(row, lastRow) ? metrics.Bounds.Width : metrics.VisibleWidth);
                }
                double right = !ReferenceEquals(row, lastRow) || segmentEndVC == int.MaxValue
                    ? Math.Max(surface.ExtentWidth, viewport.Width)
                    : visualLine.GetTextLineVisualXPosition(lastRow, segmentEndVC);

                left -= scrollX;
                right -= scrollX;
                var extendSelection = new Rect(
                    Math.Min(left, right), y, Math.Abs(right - left), row.Bounds.Height);
                if (lastRect.IsEmpty)
                {
                    yield return extendSelection;
                }
                else if (Touches(extendSelection, lastRect))
                {
                    yield return lastRect.Union(extendSelection);
                }
                else
                {
                    // An end of line inside a right-to-left run leaves the two apart.
                    yield return lastRect;
                    yield return extendSelection;
                }
            }
            else
            {
                yield return lastRect;
            }
        }
    }

    private static bool IsClose(double left, double right) => Math.Abs(left - right) < 0.01;

    /// <summary>
    /// Whether the two overlap or merely meet. <see cref="Rect.IntersectsWith"/> is strict, and the
    /// end-of-line extension starts exactly where the text rectangle ends, so a strict test would
    /// leave the two apart and break the run of rectangles the outline is built from.
    /// </summary>
    private static bool Touches(Rect left, Rect right)
        => left.X <= right.Right && left.Right >= right.X &&
           left.Y <= right.Bottom && left.Bottom >= right.Y;

    private readonly record struct Segment(bool IsArc, double X, double Y, bool Clockwise)
    {
        public static Segment Line(double x, double y) => new(false, x, y, false);

        public static Segment Arc(double x, double y, bool clockwise) => new(true, x, y, clockwise);
    }
}
