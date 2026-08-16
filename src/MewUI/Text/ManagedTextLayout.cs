using System.Globalization;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

internal interface IManagedTextLayoutData
{
    TextLayoutRequestSnapshot Snapshot { get; }
    IReadOnlyList<ManagedTextLine> ManagedLines { get; }
}

internal sealed class ManagedTextLayout : ITextLayout, IManagedTextLayoutData
{
    private const int FastSegmentMapCapacity = 4;
    private readonly ManagedTextEngine _engine;
    private readonly List<ManagedTextLine> _lines;
    private readonly IReadOnlyList<TextLayoutLineMetrics> _lineMetrics;
    private int[]? _fastCaretBoundaries;
    private readonly Dictionary<int, FastSegmentMapEntry> _fastSegmentMaps = [];
    private readonly LinkedList<int> _fastSegmentMapOrder = [];

    public ManagedTextLayout(
        ManagedTextEngine engine,
        TextLayoutRequestSnapshot snapshot,
        List<ManagedTextLine> lines,
        Size measuredSize,
        bool isFastPath)
    {
        _engine = engine;
        Snapshot = snapshot;
        _lines = lines;
        _lineMetrics = lines.Select(static line => line.Metrics).ToArray();
        MeasuredSize = measuredSize;
        ContentHeight = lines.Count == 0 ? 0 : lines[^1].Metrics.Bounds.Bottom;
        IsFastPath = isFastPath;
    }

    public TextLayoutRequestSnapshot Snapshot { get; }

    public IReadOnlyList<ManagedTextLine> ManagedLines => _lines;

    public Size MeasuredSize { get; }

    public double ContentHeight { get; }

    public IReadOnlyList<TextLayoutLineMetrics> Lines => _lineMetrics;

    internal bool IsFastPath { get; }

    internal IFont GetDefaultFont() => _engine.GetFont(Snapshot.DefaultStyle, Snapshot.Dpi);

    internal bool HasMaterializedClusters
        => _lines.Any(static line => line.Clusters is not null);

    public CharacterHit HitTestPoint(Point point)
    {
        if (_lines.Count == 0)
        {
            return default;
        }

        int lineIndex = FindLineByY(point.Y);
        var line = _lines[lineIndex];
        if (IsFastPath && line.Clusters is null)
        {
            return HitTestFastPath(line, point.X);
        }
        var clusters = EnsureClusters(line);
        if (clusters.Count == 0)
        {
            return new CharacterHit(line.Metrics.TextStart, 0);
        }

        if (point.X <= clusters[0].X)
        {
            return new CharacterHit(clusters[0].Start, 0);
        }

        foreach (var cluster in clusters)
        {
            if (point.X <= cluster.X + cluster.Width)
            {
                return point.X < cluster.X + cluster.Width * 0.5
                    ? new CharacterHit(cluster.Start, 0)
                    : new CharacterHit(cluster.Start, cluster.Length);
            }
        }

        var last = clusters[^1];
        return new CharacterHit(last.Start, last.Length);
    }

    public Rect GetCaretBounds(CharacterHit hit)
    {
        int insertion = Math.Clamp(hit.InsertionIndex, 0, Snapshot.Text.Length);
        var line = FindLineByInsertion(insertion);
        if (IsFastPath && line.Clusters is null)
        {
            return new Rect(
                GetFastPathX(line, insertion),
                line.Metrics.Bounds.Y,
                1,
                line.Metrics.Bounds.Height);
        }
        var clusters = EnsureClusters(line);
        double x = line.Metrics.Bounds.X;

        foreach (var cluster in clusters)
        {
            if (insertion <= cluster.Start)
            {
                x = cluster.X;
                break;
            }
            if (insertion <= cluster.End)
            {
                x = insertion == cluster.Start ? cluster.X : cluster.X + cluster.Width;
                break;
            }
            x = cluster.X + cluster.Width;
        }

        return new Rect(x, line.Metrics.Bounds.Y, 1, line.Metrics.Bounds.Height);
    }

    public CharacterHit GetNextLogicalCaret(CharacterHit from, LogicalDirection direction, CaretMode mode)
    {
        int insertion = Math.Clamp(from.InsertionIndex, 0, Snapshot.Text.Length);
        if (mode == CaretMode.CodeUnit)
        {
            int next = direction == LogicalDirection.Forward
                ? Math.Min(Snapshot.Text.Length, insertion + 1)
                : Math.Max(0, insertion - 1);
            return new CharacterHit(next, 0);
        }

        IReadOnlyList<int> boundaries = IsFastPath && !_lines.Any(static line => line.Clusters is not null)
            ? GetFastCaretBoundaries()
            : GetCaretBoundaries();
        if (direction == LogicalDirection.Forward)
        {
            foreach (int boundary in boundaries)
            {
                if (boundary > insertion)
                {
                    return new CharacterHit(boundary, 0);
                }
            }
            return new CharacterHit(Snapshot.Text.Length, 0);
        }

        for (int i = boundaries.Count - 1; i >= 0; i--)
        {
            if (boundaries[i] < insertion)
            {
                return new CharacterHit(boundaries[i], 0);
            }
        }
        return default;
    }

    public CharacterHit GetNextVisualCaret(CharacterHit from, VisualDirection direction, CaretMode mode)
    {
        if (direction == VisualDirection.Left)
        {
            return GetNextLogicalCaret(from, LogicalDirection.Backward, mode);
        }
        if (direction == VisualDirection.Right)
        {
            return GetNextLogicalCaret(from, LogicalDirection.Forward, mode);
        }

        var caret = GetCaretBounds(from);
        int currentLine = FindLineIndexByInsertion(Math.Clamp(from.InsertionIndex, 0, Snapshot.Text.Length));
        int targetLine = direction == VisualDirection.Up ? currentLine - 1 : currentLine + 1;
        if (targetLine < 0 || targetLine >= _lines.Count)
        {
            return from;
        }

        var target = _lines[targetLine].Metrics.Bounds;
        return HitTestPoint(new Point(caret.X, target.Y + target.Height * 0.5));
    }

    public void GetRangeBounds(int start, int length, IList<Rect> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (start < 0 || length < 0 || start > Snapshot.Text.Length - length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }
        if (length == 0)
        {
            return;
        }

        if (IsFastPath && !_lines.Any(static line => line.Clusters is not null))
        {
            var line = _lines[0];
            double left = GetFastPathX(line, start);
            double right = GetFastPathX(line, start + length);
            output.Add(new Rect(
                Math.Min(left, right),
                line.Metrics.Bounds.Y,
                Math.Abs(right - left),
                line.Metrics.Bounds.Height));
            return;
        }

        int end = start + length;
        foreach (var line in _lines)
        {
            double left = double.PositiveInfinity;
            double right = double.NegativeInfinity;
            foreach (var cluster in EnsureClusters(line))
            {
                if (cluster.End <= start || cluster.Start >= end)
                {
                    continue;
                }
                left = Math.Min(left, cluster.X);
                right = Math.Max(right, cluster.X + cluster.Width);
            }

            if (!double.IsPositiveInfinity(left))
            {
                output.Add(new Rect(left, line.Metrics.Bounds.Y, Math.Max(0, right - left), line.Metrics.Bounds.Height));
            }
        }
    }

    internal List<ManagedTextCluster> EnsureClusters(ManagedTextLine line)
    {
        if (line.Clusters is not null)
        {
            return line.Clusters;
        }

        lock (line)
        {
            if (line.Clusters is not null)
            {
                return line.Clusters;
            }

            var clusters = _engine.MeasureClusters(Snapshot, line.Metrics.TextStart, line.Metrics.TextLength);
            double naturalWidth = clusters.Sum(static cluster => cluster.Width);
            double scale = naturalWidth > 0 ? line.Metrics.Bounds.Width / naturalWidth : 1;
            double x = line.Metrics.Bounds.X;
            foreach (var cluster in clusters)
            {
                cluster.X = x;
                cluster.Width *= scale;
                x += cluster.Width;
            }
            line.Clusters = clusters;
            return clusters;
        }
    }

    private int FindLineByY(double y)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var bounds = _lines[i].Metrics.Bounds;
            if (y < bounds.Bottom)
            {
                return i;
            }
        }
        return _lines.Count - 1;
    }

    private ManagedTextLine FindLineByInsertion(int insertion)
        => _lines[FindLineIndexByInsertion(insertion)];

    private int FindLineIndexByInsertion(int insertion)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var metrics = _lines[i].Metrics;
            int lineEnd = metrics.TextEnd + metrics.NewLineLength;
            bool ownsBoundary = metrics.NewLineLength > 0 || i == _lines.Count - 1;
            if (insertion < lineEnd || (insertion == lineEnd && ownsBoundary) || i == _lines.Count - 1)
            {
                return i;
            }
        }
        return _lines.Count - 1;
    }

    private List<int> GetCaretBoundaries()
    {
        var boundaries = new List<int> { 0 };
        foreach (var line in _lines)
        {
            foreach (var cluster in EnsureClusters(line))
            {
                if (boundaries[^1] != cluster.Start)
                {
                    boundaries.Add(cluster.Start);
                }
                if (boundaries[^1] != cluster.End)
                {
                    boundaries.Add(cluster.End);
                }
            }
            int lineEnd = line.Metrics.TextEnd + line.Metrics.NewLineLength;
            if (boundaries[^1] != lineEnd)
            {
                boundaries.Add(lineEnd);
            }
        }
        if (boundaries[^1] != Snapshot.Text.Length)
        {
            boundaries.Add(Snapshot.Text.Length);
        }
        return boundaries;
    }

    private CharacterHit HitTestFastPath(ManagedTextLine line, double x)
    {
        var bounds = line.Metrics.Bounds;
        if (x <= bounds.X)
        {
            return default;
        }
        var segments = line.FastSegments;
        if (segments is null || segments.Count == 0)
        {
            return new CharacterHit(Snapshot.Text.Length, 0);
        }
        var map = GetFastSegmentMap(FindFastPathSegment(segments, x));
        int[] boundaries = map.Boundaries;
        if (x >= bounds.Right)
        {
            // The character the point is past, entered from its trailing edge, as the cluster path
            // reports it. FirstCharacterIndex names the character a point falls in, so answering with
            // the line end would make it round up where every other path rounds down.
            int lastStart = boundaries.Length >= 2 ? boundaries[^2] : Snapshot.Text.Length;
            return new CharacterHit(lastStart, Snapshot.Text.Length - lastStart);
        }

        int low = 0;
        int high = boundaries.Length - 1;
        while (high - low > 1)
        {
            int middle = low + (high - low) / 2;
            double middleX = map.TryGetX(boundaries[middle], out double cachedX)
                ? cachedX
                : GetFastPathX(line, boundaries[middle]);
            if (x < middleX)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        double leadingX = map.TryGetX(boundaries[low], out double cachedLeading)
            ? cachedLeading
            : GetFastPathX(line, boundaries[low]);
        double trailingX = map.TryGetX(boundaries[high], out double cachedTrailing)
            ? cachedTrailing
            : GetFastPathX(line, boundaries[high]);
        // The trailing half is said as a trailing length rather than the next boundary, so
        // FirstCharacterIndex names the character the point is in and InsertionIndex still rounds.
        return x < leadingX + (trailingX - leadingX) * 0.5
            ? new CharacterHit(boundaries[low], 0)
            : new CharacterHit(boundaries[low], boundaries[high] - boundaries[low]);
    }

    private double GetFastPathX(ManagedTextLine line, int insertion)
    {
        insertion = Math.Clamp(insertion, 0, Snapshot.Text.Length);
        if (insertion == 0)
        {
            return line.Metrics.Bounds.X;
        }
        if (insertion == Snapshot.Text.Length)
        {
            return line.Metrics.Bounds.Right;
        }
        var segment = FindFastPathSegmentByInsertion(line.FastSegments!, insertion);
        if (insertion == segment.End)
        {
            return segment.X + segment.Width;
        }
        var map = GetFastSegmentMap(segment);
        if (map.TryGetX(insertion, out double x))
        {
            return x;
        }
        return segment.X + _engine.MeasureFastPathRange(
            Snapshot,
            segment.Start,
            insertion - segment.Start);
    }

    private FastSegmentMap GetFastSegmentMap(ManagedTextSegment segment)
    {
        lock (_fastSegmentMaps)
        {
            if (_fastSegmentMaps.TryGetValue(segment.Start, out var cached))
            {
                _fastSegmentMapOrder.Remove(cached.Node);
                _fastSegmentMapOrder.AddLast(cached.Node);
                return cached.Map;
            }

            string text = Snapshot.Text.Substring(segment.Start, segment.Length);
            int[] starts = StringInfo.ParseCombiningCharacters(text);
            var boundaries = new int[starts.Length + (starts.Length == 0 || starts[^1] != text.Length ? 1 : 0)];
            for (int i = 0; i < starts.Length; i++)
            {
                boundaries[i] = segment.Start + starts[i];
            }
            if (boundaries.Length > starts.Length)
            {
                boundaries[^1] = segment.End;
            }

            double[]? advances = _engine.MeasureFastPathAdvances(Snapshot, segment.Start, segment.Length);
            var map = new FastSegmentMap(segment, boundaries, advances);
            var node = _fastSegmentMapOrder.AddLast(segment.Start);
            _fastSegmentMaps.Add(segment.Start, new FastSegmentMapEntry(map, node));
            while (_fastSegmentMaps.Count > FastSegmentMapCapacity && _fastSegmentMapOrder.First is { } oldest)
            {
                _fastSegmentMapOrder.RemoveFirst();
                _fastSegmentMaps.Remove(oldest.Value);
            }
            return map;
        }
    }

    private static ManagedTextSegment FindFastPathSegment(
        IReadOnlyList<ManagedTextSegment> segments,
        double x)
    {
        int low = 0;
        int high = segments.Count - 1;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (x <= segments[middle].X + segments[middle].Width)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }
        return segments[low];
    }

    private static ManagedTextSegment FindFastPathSegmentByInsertion(
        IReadOnlyList<ManagedTextSegment> segments,
        int insertion)
    {
        int low = 0;
        int high = segments.Count - 1;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (insertion <= segments[middle].End)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }
        return segments[low];
    }

    private sealed record FastSegmentMapEntry(FastSegmentMap Map, LinkedListNode<int> Node);

    private sealed class FastSegmentMap(
        ManagedTextSegment segment,
        int[] boundaries,
        double[]? advances)
    {
        public int[] Boundaries { get; } = boundaries;

        public bool TryGetX(int insertion, out double x)
        {
            int relative = insertion - segment.Start;
            if (relative <= 0)
            {
                x = segment.X;
                return true;
            }
            if (relative >= segment.Length)
            {
                x = segment.X + segment.Width;
                return true;
            }
            if (advances is null || advances.Length < relative || advances[^1] <= 0)
            {
                x = 0;
                return false;
            }
            double scale = segment.Width / advances[^1];
            x = segment.X + advances[relative - 1] * scale;
            return true;
        }
    }

    private int[] GetFastCaretBoundaries()
    {
        if (_fastCaretBoundaries is not null)
        {
            return _fastCaretBoundaries;
        }

        int[] starts = StringInfo.ParseCombiningCharacters(Snapshot.Text);
        if (starts.Length == 0)
        {
            return _fastCaretBoundaries = [0];
        }
        if (starts[^1] == Snapshot.Text.Length)
        {
            return _fastCaretBoundaries = starts;
        }

        var boundaries = new int[starts.Length + 1];
        starts.CopyTo(boundaries, 0);
        boundaries[^1] = Snapshot.Text.Length;
        return _fastCaretBoundaries = boundaries;
    }
}
