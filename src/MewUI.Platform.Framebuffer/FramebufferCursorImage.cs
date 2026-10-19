namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>A pointer image in premultiplied ARGB8888, row-major, with the pixel that marks the pointer position.</summary>
internal sealed record FramebufferCursorBitmap(uint[] Pixels, int Width, int Height, int HotX, int HotY);

/// <summary>
/// Pointer images for each <see cref="CursorType"/>: a white shape with a black outline and antialiased edges.
/// Straight edges along the axes or at 45 degrees are laid on whole device pixels at the requested scale, so they
/// stay sharp at fractional scales; curved shapes are scaled as they are.
/// </summary>
internal static class FramebufferCursorImage
{
    /// <summary>
    /// The image for <paramref name="type"/> at <paramref name="dpiScale"/>, drawn at a smaller scale when needed to
    /// stay within <paramref name="maxSize"/> pixels on each side.
    /// </summary>
    public static FramebufferCursorBitmap Create(CursorType type, double dpiScale, int maxSize)
    {
        double scale = Math.Max(dpiScale, 1.0);
        while (true)
        {
            var bitmap = Render(BuildShape(type, new PixelGrid(scale)), new PixelGrid(scale));
            if ((bitmap.Width <= maxSize && bitmap.Height <= maxSize) || scale <= 1.0)
            {
                return bitmap;
            }

            scale = Math.Max(1.0, scale - 0.25);
        }
    }

    private static FramebufferCursorBitmap Render(CursorShape shape, PixelGrid grid)
    {
        int outline = grid.Length(1);

        // A sharp corner's outline reaches twice the outline width past it.
        int pad = 2 * outline + 1;
        int originX = (int)Math.Floor(shape.MinX) - pad;
        int originY = (int)Math.Floor(shape.MinY) - pad;
        int width = (int)Math.Ceiling(shape.MaxX) + pad - originX;
        int height = (int)Math.Ceiling(shape.MaxY) + pad - originY;
        var outlined = shape.StraightOutlines(outline);

        // Coverage is counted over a grid of points inside each pixel, so where two parts meet the join is solid
        // instead of each part's own antialiased edge showing through as a grey seam.
        const int SAMPLES = 8;
        var pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int filled = 0;
                int covered = 0;
                for (int row = 0; row < SAMPLES; row++)
                {
                    for (int column = 0; column < SAMPLES; column++)
                    {
                        double sampleX = originX + x + (column + 0.5) / SAMPLES;
                        double sampleY = originY + y + (row + 0.5) / SAMPLES;
                        double curved = shape.CurvedDistance(sampleX, sampleY);
                        if (curved <= 0 || shape.StraightDistance(sampleX, sampleY) <= 0)
                        {
                            filled++;
                            covered++;
                        }
                        else if (curved <= outline || CursorShape.Nearest(outlined, sampleX, sampleY) <= 0)
                        {
                            covered++;
                        }
                    }
                }

                uint alpha = (uint)Math.Round(covered * 255.0 / (SAMPLES * SAMPLES));
                uint white = (uint)Math.Round(filled * 255.0 / (SAMPLES * SAMPLES));
                pixels[y * width + x] = alpha << 24 | white << 16 | white << 8 | white;
            }
        }

        return new FramebufferCursorBitmap(pixels, width, height,
            Math.Clamp(shape.HotX - originX, 0, width - 1), Math.Clamp(shape.HotY - originY, 0, height - 1));
    }

    private static CursorShape BuildShape(CursorType type, PixelGrid grid) => type switch
    {
        CursorType.IBeam => IBeam(grid),
        CursorType.Wait => Hourglass(grid),
        CursorType.Cross => Cross(grid),
        CursorType.UpArrow => UpArrow(grid),
        CursorType.SizeWE => DoubleArrow(grid, new CursorShape()),
        CursorType.SizeNS => DoubleArrow(grid, new CursorShape(transposed: true)),
        CursorType.SizeNWSE => DiagonalArrow(grid, mirrored: false),
        CursorType.SizeNESW => DiagonalArrow(grid, mirrored: true),
        CursorType.SizeAll => FourWayArrow(grid),
        CursorType.No => NotAllowed(grid),
        CursorType.Hand => Hand(grid),
        CursorType.Help => Help(grid),
        _ => Arrow(grid, new CursorShape()),
    };

    private static CursorShape Arrow(PixelGrid grid, CursorShape shape)
    {
        // The classic pointer: a vertical left side and a 45 degree right side down to the wing, a neck, then a
        // tail two pixels wide that drops two pixels for each one across and ends square to its own length,
        // beside a 45 degree cut back to the left side. Later points follow from earlier lengths so those angles
        // survive rounding at any scale.
        int wing = grid.Length(9);
        int neck = grid.Length(6);
        int neckDrop = grid.Length(2);
        int tailDrop = grid.Length(5);
        int tail = grid.Length(2);
        int side = grid.Length(13);
        double tailTop = wing + neckDrop;
        double tailBottom = tailTop + tailDrop;
        double tailRight = neck + tailDrop / 2.0;
        double tailLeft = tailRight - tail;

        // Where the tail's left side, x = tailLeft - (tailBottom - y) / 2, meets the cut x + y = side.
        double notchY = (side - tailLeft + tailBottom / 2) / 1.5;
        double notchX = side - notchY;
        shape.AddPolygon(
            (0, 0),
            (wing, wing),
            (neck, wing),
            (neck, tailTop),
            (tailRight, tailBottom),
            (tailRight - 0.8 * tail, tailBottom + 0.4 * tail),
            (notchX, notchY),
            (0, side));
        shape.SetHotSpot(0, 0);
        return shape;
    }

    private static CursorShape IBeam(PixelGrid grid)
    {
        int stroke = grid.OddLength(1);
        int arm = grid.Length(3);
        int height = grid.OddLength(15);
        var shape = new CursorShape();
        shape.AddRectangle(arm, 0, arm + stroke, height);
        shape.AddRectangle(0, 0, 2 * arm + stroke, stroke);
        shape.AddRectangle(0, height - stroke, 2 * arm + stroke, height);
        shape.SetHotSpot(arm + (stroke - 1) / 2, height / 2);
        return shape;
    }

    private static CursorShape Hourglass(PixelGrid grid)
    {
        int stroke = grid.Length(1);
        int inset = grid.Length(1);
        int width = grid.OddLength(9);
        int height = grid.OddLength(11);
        double middleX = width / 2.0;
        double middleY = height / 2.0;
        var shape = new CursorShape();
        shape.AddRectangle(0, 0, width, stroke);
        shape.AddRectangle(0, height - stroke, width, height);
        shape.AddPolygon((inset, stroke), (width - inset, stroke), (middleX, middleY));
        shape.AddPolygon((inset, height - stroke), (middleX, middleY), (width - inset, height - stroke));
        shape.SetHotSpot(width / 2, height / 2);
        return shape;
    }

    private static CursorShape Cross(PixelGrid grid)
    {
        int stroke = grid.OddLength(1);
        int arm = grid.Length(7);
        var shape = new CursorShape();
        shape.AddRectangle(arm, 0, arm + stroke, 2 * arm + stroke);
        shape.AddRectangle(0, arm, 2 * arm + stroke, arm + stroke);
        shape.SetHotSpot(arm + (stroke - 1) / 2, arm + (stroke - 1) / 2);
        return shape;
    }

    private static CursorShape DoubleArrow(PixelGrid grid, CursorShape shape)
    {
        int halfLength = grid.Length(10);
        int headLength = grid.Length(4);
        AddStraightArrow(grid, shape, halfLength, headLength, shaftTop: headLength, headAtStart: true);
        int stroke = grid.OddLength(1);
        shape.SetHotSpot(halfLength + (stroke - 1) / 2, headLength + (stroke - 1) / 2);
        return shape;
    }

    private static CursorShape FourWayArrow(PixelGrid grid)
    {
        int halfLength = grid.Length(11);
        int headLength = grid.Length(3);
        var shape = new CursorShape();
        AddStraightArrow(grid, shape, halfLength, headLength, shaftTop: halfLength, headAtStart: true);
        AddStraightArrow(grid, shape.Transposed(), halfLength, headLength, shaftTop: halfLength, headAtStart: true);
        int center = halfLength + (grid.OddLength(1) - 1) / 2;
        shape.SetHotSpot(center, center);
        return shape;
    }

    private static CursorShape UpArrow(PixelGrid grid)
    {
        int headLength = grid.Length(4);
        var shape = new CursorShape(transposed: true);
        AddStraightArrow(grid, shape, grid.Length(9.5), headLength, shaftTop: headLength, headAtStart: true, headAtEnd: false);
        shape.SetHotSpot(0, headLength + (grid.OddLength(1) - 1) / 2);
        return shape;
    }

    // An arrow along x from 0 to 2 * halfLength + stroke with a stroke-wide shaft whose top edge is at shaftTop.
    // Each head is as long as it is wide on either side of the shaft, so its sides run at 45 degrees from a tip as
    // wide as the shaft.
    private static void AddStraightArrow(PixelGrid grid, CursorShape shape, int halfLength, int headLength, int shaftTop,
        bool headAtStart, bool headAtEnd = true)
    {
        int stroke = grid.OddLength(1);
        int length = 2 * halfLength + stroke;
        int shaftBottom = shaftTop + stroke;

        // The shaft reaches a pixel into each head so the two never leave a seam.
        int shaftStart = headAtStart ? headLength - 1 : 0;
        int shaftEnd = headAtEnd ? length - (headLength - 1) : length;
        shape.AddRectangle(shaftStart, shaftTop, shaftEnd, shaftBottom);
        if (headAtEnd)
        {
            shape.AddPolygon((length, shaftTop), (length, shaftBottom),
                (length - headLength, shaftBottom + headLength), (length - headLength, shaftTop - headLength));
        }

        if (headAtStart)
        {
            shape.AddPolygon((0, shaftTop), (headLength, shaftTop - headLength),
                (headLength, shaftBottom + headLength), (0, shaftBottom));
        }
    }

    private static CursorShape DiagonalArrow(PixelGrid grid, bool mirrored)
    {
        int span = grid.OddLength(16);
        int headLength = grid.Length(6);
        int band = grid.Length(1);
        // The shaft's square ends sit a pixel inside the heads so the parts overlap rather than meet on a line.
        int shaftStart = Math.Max(0, (headLength - 1 - band) / 2);
        int shaftEnd = span - shaftStart;
        var shape = new CursorShape(mirrorWidth: mirrored ? span : null);
        shape.AddPolygon((0, 0), (headLength, 0), (0, headLength));
        shape.AddPolygon((span, span), (span - headLength, span), (span, span - headLength));

        // The shaft is the band band pixels either side of the diagonal, cut square at both ends.
        shape.AddPolygon((shaftStart + band, shaftStart), (shaftEnd, shaftEnd - band), (shaftEnd - band, shaftEnd),
            (shaftStart, shaftStart + band));
        shape.SetHotSpot(span / 2, span / 2);
        return shape;
    }

    private static CursorShape NotAllowed(PixelGrid grid)
    {
        // The ring keeps its unrounded size; only its centre moves onto a pixel centre, the hot spot.
        double center = Math.Floor(grid.Scaled(8.5)) + 0.5;
        double radius = grid.Scaled(7.5);
        double halfThickness = grid.Scaled(1);
        double reach = radius * 0.7;
        var shape = new CursorShape();
        shape.AddRing(center, center, radius, halfThickness);
        shape.AddSegment(center - reach, center - reach, center + reach, center + reach, halfThickness);
        shape.SetHotSpot((int)center, (int)center);
        return shape;
    }

    private static CursorShape Hand(PixelGrid grid)
    {
        // Neighbouring fingers overlap a little, so only a shallow notch at the top tells them apart and no thin
        // gap runs down between them.
        double radius = grid.Scaled(1.5);
        var shape = new CursorShape { Curved = true };
        shape.AddSegment(grid.Scaled(3.5), radius, grid.Scaled(3.5), grid.Scaled(10), radius);
        shape.AddSegment(grid.Scaled(6.3), grid.Scaled(5.5), grid.Scaled(6.3), grid.Scaled(11), radius);
        shape.AddSegment(grid.Scaled(9.1), grid.Scaled(6), grid.Scaled(9.1), grid.Scaled(11), radius);
        shape.AddSegment(grid.Scaled(11.9), grid.Scaled(7), grid.Scaled(11.9), grid.Scaled(11), radius);
        shape.AddPolygon(
            (grid.Scaled(2), grid.Scaled(8)), (grid.Scaled(13.4), grid.Scaled(8)), (grid.Scaled(13.4), grid.Scaled(14)),
            (grid.Scaled(12.4), grid.Scaled(16)), (grid.Scaled(3), grid.Scaled(16)), (grid.Scaled(2), grid.Scaled(14)));
        shape.AddSegment(grid.Scaled(0.6), grid.Scaled(9), grid.Scaled(2.5), grid.Scaled(12.5), grid.Scaled(1.2));
        shape.SetHotSpot((int)grid.Scaled(3.5), 0);
        return shape;
    }

    private static CursorShape Help(PixelGrid grid)
    {
        var shape = Arrow(grid, new CursorShape());
        double halfStroke = grid.Scaled(0.65);
        (double X, double Y)[] points = [(12, 2), (13, 1), (15, 1), (16, 2), (16, 3.5), (14, 5), (14, 6.5)];
        for (int index = 1; index < points.Length; index++)
        {
            shape.AddSegment(grid.Scaled(points[index - 1].X), grid.Scaled(points[index - 1].Y),
                grid.Scaled(points[index].X), grid.Scaled(points[index].Y), halfStroke);
        }

        shape.AddDisc(grid.Scaled(14), grid.Scaled(8.7), grid.Scaled(0.8));
        return shape;
    }

    /// <summary>Rounds layout units (one device pixel at scale 1) to whole device pixels.</summary>
    private readonly struct PixelGrid
    {
        private readonly double _scale;

        public PixelGrid(double scale) => _scale = scale;

        /// <summary>A coordinate, rounded to the nearest pixel boundary.</summary>
        public int Position(double units) => (int)Math.Round(units * _scale, MidpointRounding.AwayFromZero);

        /// <summary>A size, rounded to whole pixels and at least one.</summary>
        public int Length(double units) => Math.Max(1, Position(units));

        /// <summary>
        /// A size rounded to an odd number of whole pixels, for parts centred on the hot spot: an odd width has a
        /// middle pixel, so the part does not lean half a pixel to one side of the pointer.
        /// </summary>
        public int OddLength(double units) => (int)Math.Floor(units * _scale / 2) * 2 + 1;

        /// <summary>A coordinate or size scaled without rounding, for curved parts.</summary>
        public double Scaled(double units) => units * _scale;

        public (double X, double Y)[] Points(params (double X, double Y)[] units)
        {
            var points = new (double X, double Y)[units.Length];
            for (int index = 0; index < units.Length; index++)
            {
                points[index] = (Position(units[index].X), Position(units[index].Y));
            }

            return points;
        }
    }

    /// <summary>A union of filled primitives in device pixels, measured by signed distance.</summary>
    private sealed class CursorShape
    {
        private readonly List<(double X, double Y)[]> _straight;
        private readonly List<Func<double, double, double>> _curvedParts;
        private readonly bool _transposed;
        private readonly int? _mirrorWidth;
        private readonly double[] _bounds;
        private readonly int[] _hotSpot;

        public CursorShape(bool transposed = false, int? mirrorWidth = null)
            : this([], [], [double.MaxValue, double.MaxValue, double.MinValue, double.MinValue], [0, 0], transposed, mirrorWidth)
        {
        }

        private CursorShape(List<(double X, double Y)[]> straight, List<Func<double, double, double>> curvedParts, double[] bounds,
            int[] hotSpot, bool transposed, int? mirrorWidth)
        {
            _straight = straight;
            _curvedParts = curvedParts;
            _bounds = bounds;
            _hotSpot = hotSpot;
            _transposed = transposed;
            _mirrorWidth = mirrorWidth;
        }

        public double MinX => _bounds[0];

        public double MinY => _bounds[1];

        public double MaxX => _bounds[2];

        public double MaxY => _bounds[3];

        public int HotX => _hotSpot[0];

        public int HotY => _hotSpot[1];

        /// <summary>A view of the same shape that swaps x and y on everything added through it.</summary>
        public CursorShape Transposed() => new(_straight, _curvedParts, _bounds, _hotSpot, !_transposed, _mirrorWidth);

        /// <summary>When set, rectangles and polygons added next are antialiased like the round parts.</summary>
        public bool Curved { get; set; }

        /// <summary>Signed distance to the straight parts: rectangles and polygons.</summary>
        public double StraightDistance(double x, double y)
        {
            double distance = double.MaxValue;
            foreach (var polygon in _straight)
            {
                distance = Math.Min(distance, PolygonDistance(polygon, x, y));
            }

            return distance;
        }

        /// <summary>
        /// The straight parts grown by their outline: each side moves out until it is <paramref name="width"/>
        /// pixels away across or down, whichever is shorter, so a 45 degree side keeps its outer edge on whole
        /// pixels just as a vertical one does; corners stay sharp.
        /// </summary>
        public List<(double X, double Y)[]> StraightOutlines(double width)
        {
            var outlines = new List<(double X, double Y)[]>(_straight.Count);
            foreach (var polygon in _straight)
            {
                outlines.Add(Grow(polygon, width));
            }

            return outlines;
        }

        public static double Nearest(List<(double X, double Y)[]> polygons, double x, double y)
        {
            double distance = double.MaxValue;
            foreach (var polygon in polygons)
            {
                distance = Math.Min(distance, PolygonDistance(polygon, x, y));
            }

            return distance;
        }

        /// <summary>Signed distance to the antialiased parts: discs, rings, round strokes and anything added while <see cref="Curved"/>.</summary>
        public double CurvedDistance(double x, double y) => Nearest(_curvedParts, x, y);

        private static double Nearest(List<Func<double, double, double>> parts, double x, double y)
        {
            double distance = double.MaxValue;
            foreach (var part in parts)
            {
                distance = Math.Min(distance, part(x, y));
            }

            return distance;
        }

        public void SetHotSpot(int x, int y)
        {
            // Mapped by its centre, so a mirrored pixel lands on the pixel opposite rather than one beside it.
            var mapped = Map(x + 0.5, y + 0.5);
            _hotSpot[0] = (int)Math.Floor(mapped.X);
            _hotSpot[1] = (int)Math.Floor(mapped.Y);
        }

        public void AddRectangle(double left, double top, double right, double bottom)
        {
            var first = Map(left, top);
            var second = Map(right, bottom);
            double minX = Math.Min(first.X, second.X);
            double minY = Math.Min(first.Y, second.Y);
            double maxX = Math.Max(first.X, second.X);
            double maxY = Math.Max(first.Y, second.Y);
            Include(minX, minY, maxX, maxY);
            double centerX = (minX + maxX) / 2;
            double centerY = (minY + maxY) / 2;
            double halfWidth = (maxX - minX) / 2;
            double halfHeight = (maxY - minY) / 2;
            if (Curved)
            {
                _curvedParts.Add((x, y) => Math.Max(Math.Abs(x - centerX) - halfWidth, Math.Abs(y - centerY) - halfHeight));
            }
            else
            {
                _straight.Add([(minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY)]);
            }
        }

        public void AddDisc(double centerX, double centerY, double radius)
        {
            var center = Map(centerX, centerY);
            Include(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);
            _curvedParts.Add((x, y) => Math.Sqrt((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y)) - radius);
        }

        public void AddRing(double centerX, double centerY, double radius, double halfThickness)
        {
            var center = Map(centerX, centerY);
            double outer = radius + halfThickness;
            Include(center.X - outer, center.Y - outer, center.X + outer, center.Y + outer);
            _curvedParts.Add((x, y) =>
                Math.Abs(Math.Sqrt((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y)) - radius) - halfThickness);
        }

        /// <summary>A stroke from one point to another with round ends.</summary>
        public void AddSegment(double startX, double startY, double endX, double endY, double halfWidth)
        {
            var start = Map(startX, startY);
            var end = Map(endX, endY);
            Include(Math.Min(start.X, end.X) - halfWidth, Math.Min(start.Y, end.Y) - halfWidth,
                Math.Max(start.X, end.X) + halfWidth, Math.Max(start.Y, end.Y) + halfWidth);
            double edgeX = end.X - start.X;
            double edgeY = end.Y - start.Y;
            double lengthSquared = Math.Max(edgeX * edgeX + edgeY * edgeY, double.Epsilon);
            _curvedParts.Add((x, y) =>
            {
                double along = Math.Clamp(((x - start.X) * edgeX + (y - start.Y) * edgeY) / lengthSquared, 0, 1);
                double offsetX = x - start.X - edgeX * along;
                double offsetY = y - start.Y - edgeY * along;
                return Math.Sqrt(offsetX * offsetX + offsetY * offsetY) - halfWidth;
            });
        }

        public void AddPolygon(params (double X, double Y)[] points)
        {
            var mapped = new (double X, double Y)[points.Length];
            for (int index = 0; index < points.Length; index++)
            {
                mapped[index] = Map(points[index].X, points[index].Y);
                Include(mapped[index].X, mapped[index].Y, mapped[index].X, mapped[index].Y);
            }

            if (Curved)
            {
                _curvedParts.Add((x, y) => PolygonDistance(mapped, x, y));
            }
            else
            {
                _straight.Add(mapped);
            }
        }

        private (double X, double Y) Map(double x, double y)
        {
            if (_mirrorWidth != null)
            {
                x = _mirrorWidth.Value - x;
            }

            return _transposed ? (y, x) : (x, y);
        }

        private void Include(double minX, double minY, double maxX, double maxY)
        {
            _bounds[0] = Math.Min(_bounds[0], minX);
            _bounds[1] = Math.Min(_bounds[1], minY);
            _bounds[2] = Math.Max(_bounds[2], maxX);
            _bounds[3] = Math.Max(_bounds[3], maxY);
        }

        private static (double X, double Y)[] Grow((double X, double Y)[] points, double width)
        {
            int count = points.Length;
            double area = 0;
            for (int index = 0; index < count; index++)
            {
                var start = points[index];
                var end = points[(index + 1) % count];
                area += start.X * end.Y - end.X * start.Y;
            }

            // Each side's line, moved outward.
            var origins = new (double X, double Y)[count];
            var directions = new (double X, double Y)[count];
            for (int index = 0; index < count; index++)
            {
                var start = points[index];
                var end = points[(index + 1) % count];
                double edgeX = end.X - start.X;
                double edgeY = end.Y - start.Y;
                double length = Math.Sqrt(edgeX * edgeX + edgeY * edgeY);
                double normalX = Math.Sign(area) * edgeY / length;
                double normalY = -Math.Sign(area) * edgeX / length;
                double reach = width * Math.Max(Math.Abs(normalX), Math.Abs(normalY));
                origins[index] = (start.X + normalX * reach, start.Y + normalY * reach);
                directions[index] = (edgeX, edgeY);
            }

            // Each corner where the two moved lines meet.
            var grown = new (double X, double Y)[count];
            for (int index = 0; index < count; index++)
            {
                int previous = (index + count - 1) % count;
                var first = origins[previous];
                var firstDirection = directions[previous];
                var second = origins[index];
                var secondDirection = directions[index];
                double cross = firstDirection.X * secondDirection.Y - firstDirection.Y * secondDirection.X;
                if (Math.Abs(cross) < 1e-9)
                {
                    grown[index] = second;
                    continue;
                }

                double along = ((second.X - first.X) * secondDirection.Y - (second.Y - first.Y) * secondDirection.X) / cross;
                grown[index] = (first.X + firstDirection.X * along, first.Y + firstDirection.Y * along);
            }

            return grown;
        }

        private static double PolygonDistance((double X, double Y)[] points, double x, double y)
        {
            double nearest = double.MaxValue;
            bool inside = false;
            for (int current = 0, previous = points.Length - 1; current < points.Length; previous = current, current++)
            {
                var start = points[current];
                var end = points[previous];
                double edgeX = end.X - start.X;
                double edgeY = end.Y - start.Y;
                double offsetX = x - start.X;
                double offsetY = y - start.Y;
                double along = Math.Clamp((offsetX * edgeX + offsetY * edgeY) / (edgeX * edgeX + edgeY * edgeY), 0, 1);
                double gapX = offsetX - edgeX * along;
                double gapY = offsetY - edgeY * along;
                nearest = Math.Min(nearest, Math.Sqrt(gapX * gapX + gapY * gapY));

                // Even-odd crossing test along a horizontal ray.
                bool crossesBelow = y >= start.Y;
                bool crossesAbove = y < end.Y;
                bool leftOfEdge = edgeX * offsetY > edgeY * offsetX;
                if ((crossesBelow && crossesAbove && leftOfEdge) || (!crossesBelow && !crossesAbove && !leftOfEdge))
                {
                    inside = !inside;
                }
            }

            return inside ? -nearest : nearest;
        }
    }
}
