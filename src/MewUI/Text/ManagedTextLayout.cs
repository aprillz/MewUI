using System.Globalization;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

internal sealed class ManagedTextLayout : ITextLayout
{
    private const int FastSegmentMapCapacity = 4;
    private readonly ManagedTextEngine _engine;
    private readonly List<ManagedTextLine> _lines;
    private readonly IReadOnlyList<TextLayoutLineMetrics> _lineMetrics;
    private int[]? _fastCaretBoundaries;
    private ManagedTextRun[]? _runs;
    private int _runCount;
    private float[]? _advances;
    private int _advanceCount;
    private readonly Dictionary<int, int[]> _runBoundaries = [];
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

        return HitTestLine(_lines[FindLineByY(point.Y)], point.X);
    }

    public Rect GetCaretBounds(CharacterHit hit)
    {
        int insertion = Math.Clamp(hit.InsertionIndex, 0, Snapshot.Text.Length);
        var line = FindLineByInsertion(insertion);
        var bounds = line.Metrics.Bounds;
        return new Rect(GetXForInsertion(line, insertion), bounds.Y, 1, bounds.Height);
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

        IReadOnlyList<int> boundaries = UsesFastPath
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

        int end = start + length;
        foreach (var line in _lines)
        {
            if (!TryGetLineRangeExtent(line, start, end, out double left, out double right))
            {
                continue;
            }

            var bounds = line.Metrics.Bounds;
            output.Add(new Rect(
                Math.Min(left, right),
                bounds.Y,
                Math.Abs(right - left),
                bounds.Height));
        }
    }

    /// <summary>True while every line still answers from measured segments rather than clusters.</summary>
    private bool UsesFastPath => IsFastPath && !HasMaterializedClusters;

    // Run model. Built from the clusters a line already carries, so the queries below can be asked
    // of either representation and compared while the two exist side by side.

    /// <summary>Builds this line's runs if they are not built yet and returns them.</summary>
    internal ReadOnlySpan<ManagedTextRun> GetRuns(ManagedTextLine line)
    {
        if (line.RunCount < 0)
        {
            BuildRuns(line);
        }

        return _runs.AsSpan(line.RunStart, line.RunCount);
    }

    internal ReadOnlySpan<ManagedTextRun> GetRuns(int lineIndex) => GetRuns(_lines[lineIndex]);

    private void BuildRuns(ManagedTextLine line)
    {
        var clusters = EnsureClusters(line);
        _runs ??= new ManagedTextRun[Math.Max(4, _lines.Count * 2)];
        _advances ??= new float[Math.Max(16, Snapshot.Text.Length)];
        line.RunStart = _runCount;

        int index = 0;
        while (index < clusters.Count)
        {
            var first = clusters[index];
            int end = index + 1;
            if (first.Kind == ManagedTextClusterKind.Text)
            {
                while (end < clusters.Count &&
                       clusters[end].Kind == ManagedTextClusterKind.Text &&
                       clusters[end].Style == first.Style &&
                       clusters[end].Start == clusters[end - 1].End)
                {
                    end++;
                }
            }

            AddRun(clusters, index, end);
            index = end;
        }

        line.RunCount = _runCount - line.RunStart;
    }

    private void AddRun(List<ManagedTextCluster> clusters, int start, int end)
    {
        var first = clusters[start];
        var last = clusters[end - 1];
        int textStart = first.Start;
        int textLength = last.End - textStart;

        var run = new ManagedTextRun
        {
            TextStart = textStart,
            TextLength = textLength,
            StyleIndex = Snapshot.GetStyleIndex(textStart),
            Font = first.Font,
            X = first.X,
            Width = last.X + last.Width - first.X,
            AdvanceStart = -1,
            AdvanceBase = 0,
            MeasuredHeight = 0,
            Baseline = 0,
            Kind = first.Kind switch
            {
                ManagedTextClusterKind.Tab => ManagedTextRunKind.Tab,
                ManagedTextClusterKind.NewLine => ManagedTextRunKind.NewLine,
                ManagedTextClusterKind.Inline => ManagedTextRunKind.Inline,
                _ => ManagedTextRunKind.Text
            },
            InlineIndex = -1
        };

        for (int index = start; index < end; index++)
        {
            run.MeasuredHeight = Math.Max(run.MeasuredHeight, clusters[index].Height);
            run.Baseline = Math.Max(run.Baseline, clusters[index].Baseline);
        }

        if (run.Kind == ManagedTextRunKind.Text)
        {
            run.AdvanceStart = _advanceCount;
            EnsureAdvanceCapacity(_advanceCount + textLength);
            // A code unit inside a cluster carries the cluster's far edge, which is the column an
            // insertion there reads as, and matches what the backends that report per-cluster
            // advances already produce.
            double cursor = 0;
            int written = run.AdvanceStart;
            for (int index = start; index < end; index++)
            {
                cursor += clusters[index].Width;
                for (int unit = 0; unit < clusters[index].Length; unit++)
                {
                    _advances![written++] = (float)cursor;
                }
            }
            _advanceCount = written;
        }

        if (_runCount == _runs!.Length)
        {
            Array.Resize(ref _runs, _runs.Length * 2);
        }
        _runs[_runCount++] = run;
    }

    private void EnsureAdvanceCapacity(int required)
    {
        if (_advances!.Length >= required)
        {
            return;
        }

        int capacity = _advances.Length;
        while (capacity < required)
        {
            capacity *= 2;
        }
        Array.Resize(ref _advances, capacity);
    }

    /// <summary>Column of an insertion inside a text run, measured from the layout's advances.</summary>
    private double GetRunX(in ManagedTextRun run, int insertion)
    {
        if (insertion <= run.TextStart)
        {
            return run.X;
        }
        // A tab or an inline object has no columns inside it, so any insertion within one reads as
        // its far side, which is what an insertion inside a cluster reads as too.
        if (insertion >= run.TextEnd || run.Kind != ManagedTextRunKind.Text)
        {
            return run.X + run.Width;
        }

        return run.X + (_advances![run.AdvanceStart + (insertion - run.TextStart) - 1] - run.AdvanceBase);
    }

    /// <summary>Text element starts inside a run, kept for as long as the run's line is alive.</summary>
    private int[] GetRunBoundaries(int runIndex)
    {
        if (_runBoundaries.TryGetValue(runIndex, out var cached))
        {
            return cached;
        }

        ref var run = ref _runs![runIndex];
        int[] starts = StringInfo.ParseCombiningCharacters(Snapshot.Text.Substring(run.TextStart, run.TextLength));
        for (int index = 0; index < starts.Length; index++)
        {
            starts[index] += run.TextStart;
        }

        _runBoundaries[runIndex] = starts;
        return starts;
    }

    // The cluster walk stays reachable by index while both representations exist, so the two can be
    // compared for the same line.
    internal double GetXForInsertionForTest(int lineIndex, int insertion)
        => GetXForInsertion(_lines[lineIndex], insertion);

    internal CharacterHit HitTestLineForTest(int lineIndex, double x)
        => HitTestLine(_lines[lineIndex], x);

    internal bool TryGetLineRangeExtentForTest(int lineIndex, int start, int end, out double left, out double right)
        => TryGetLineRangeExtent(_lines[lineIndex], start, end, out left, out right);

    internal ReadOnlySpan<ManagedTextRun> GetRunsForTest(int lineIndex) => GetRuns(_lines[lineIndex]);

    internal double GetXForInsertionViaRuns(int lineIndex, int insertion)
    {
        var line = _lines[lineIndex];
        var runs = GetRuns(line);
        if (runs.Length == 0)
        {
            return line.Metrics.Bounds.X;
        }

        for (int index = 0; index < runs.Length; index++)
        {
            if (insertion <= runs[index].TextStart)
            {
                return runs[index].X;
            }
            if (insertion <= runs[index].TextEnd)
            {
                return GetRunX(in runs[index], insertion);
            }
        }

        return runs[^1].X + runs[^1].Width;
    }

    internal CharacterHit HitTestLineViaRuns(int lineIndex, double x)
    {
        var line = _lines[lineIndex];
        var runs = GetRuns(line);
        if (runs.Length == 0)
        {
            return new CharacterHit(line.Metrics.TextStart, 0);
        }

        if (x <= runs[0].X)
        {
            return new CharacterHit(runs[0].TextStart, 0);
        }

        for (int index = 0; index < runs.Length; index++)
        {
            ref readonly var run = ref runs[index];
            if (x > run.X + run.Width && index != runs.Length - 1)
            {
                continue;
            }

            return HitTestRun(line.RunStart + index, in run, x);
        }

        ref readonly var lastRun = ref runs[^1];
        return new CharacterHit(lastRun.TextStart, lastRun.TextLength);
    }

    private CharacterHit HitTestRun(int runIndex, in ManagedTextRun run, double x)
    {
        if (run.Kind != ManagedTextRunKind.Text)
        {
            return x < run.X + run.Width * 0.5
                ? new CharacterHit(run.TextStart, 0)
                : new CharacterHit(run.TextStart, run.TextLength);
        }

        int[] boundaries = GetRunBoundaries(runIndex);
        for (int index = 0; index < boundaries.Length; index++)
        {
            int start = boundaries[index];
            int end = index + 1 < boundaries.Length ? boundaries[index + 1] : run.TextEnd;
            double right = GetRunX(in run, end);
            if (x <= right || index == boundaries.Length - 1)
            {
                double left = GetRunX(in run, start);
                return x < left + (right - left) * 0.5
                    ? new CharacterHit(start, 0)
                    : new CharacterHit(start, end - start);
            }
        }

        return new CharacterHit(run.TextStart, run.TextLength);
    }

    internal bool TryGetLineRangeExtentViaRuns(int lineIndex, int start, int end, out double left, out double right)
    {
        var runs = GetRuns(_lines[lineIndex]);
        left = double.PositiveInfinity;
        right = double.NegativeInfinity;
        foreach (ref readonly var run in runs)
        {
            if (run.TextEnd <= start || run.TextStart >= end)
            {
                continue;
            }

            left = Math.Min(left, GetRunX(in run, Math.Max(start, run.TextStart)));
            right = Math.Max(right, GetRunX(in run, Math.Min(end, run.TextEnd)));
        }

        return !double.IsPositiveInfinity(left);
    }

    // The three queries below are the whole cluster-facing surface: everything above asks a line
    // where an insertion sits, what a point hits, and how far a range reaches, and nothing else
    // touches how a line stores its columns.

    private CharacterHit HitTestLine(ManagedTextLine line, double x)
    {
        if (IsFastPath && line.Clusters is null)
        {
            return HitTestFastPath(line, x);
        }

        var clusters = EnsureClusters(line);
        if (clusters.Count == 0)
        {
            return new CharacterHit(line.Metrics.TextStart, 0);
        }

        if (x <= clusters[0].X)
        {
            return new CharacterHit(clusters[0].Start, 0);
        }

        foreach (var cluster in clusters)
        {
            if (x <= cluster.X + cluster.Width)
            {
                // The half that the point falls in decides whether the caret goes before the cluster
                // or after it, and the trailing half says so as a length so FirstCharacterIndex still
                // names the cluster the point is in.
                return x < cluster.X + cluster.Width * 0.5
                    ? new CharacterHit(cluster.Start, 0)
                    : new CharacterHit(cluster.Start, cluster.Length);
            }
        }

        var last = clusters[^1];
        return new CharacterHit(last.Start, last.Length);
    }

    private double GetXForInsertion(ManagedTextLine line, int insertion)
    {
        if (IsFastPath && line.Clusters is null)
        {
            return GetFastPathX(line, insertion);
        }

        double x = line.Metrics.Bounds.X;
        foreach (var cluster in EnsureClusters(line))
        {
            if (insertion <= cluster.Start)
            {
                return cluster.X;
            }
            if (insertion <= cluster.End)
            {
                // An insertion inside a cluster has no column of its own, so it reads as the far side.
                return cluster.X + cluster.Width;
            }
            x = cluster.X + cluster.Width;
        }

        return x;
    }

    private bool TryGetLineRangeExtent(ManagedTextLine line, int start, int end, out double left, out double right)
    {
        if (IsFastPath && line.Clusters is null)
        {
            left = GetFastPathX(line, start);
            right = GetFastPathX(line, end);
            return true;
        }

        left = double.PositiveInfinity;
        right = double.NegativeInfinity;
        foreach (var cluster in EnsureClusters(line))
        {
            if (cluster.End <= start || cluster.Start >= end)
            {
                continue;
            }
            left = Math.Min(left, cluster.X);
            right = Math.Max(right, cluster.X + cluster.Width);
        }

        return !double.IsPositiveInfinity(left);
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
