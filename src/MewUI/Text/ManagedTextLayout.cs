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
    private ManagedTextFragments? _fragments;
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

    /// <summary>Layout whose lines were assembled as runs over measured fragments, with no clusters behind them.</summary>
    public ManagedTextLayout(
        ManagedTextEngine engine,
        TextLayoutRequestSnapshot snapshot,
        List<ManagedTextLine> lines,
        Size measuredSize,
        ManagedTextFragments fragments,
        List<ManagedTextRun> runs)
        : this(engine, snapshot, lines, measuredSize, isFastPath: false)
    {
        _fragments = fragments;
        _runs = [.. runs];
        _runCount = runs.Count;
        _advances = fragments.Advances;
    }

    public TextLayoutRequestSnapshot Snapshot { get; }

    public IReadOnlyList<ManagedTextLine> ManagedLines => _lines;

    public Size MeasuredSize { get; }

    public double ContentHeight { get; }

    public IReadOnlyList<TextLayoutLineMetrics> Lines => _lineMetrics;

    internal bool IsFastPath { get; }

    internal IFont GetDefaultFont() => _engine.GetFont(Snapshot.DefaultStyle, Snapshot.Dpi);

    /// <summary>True once any line has built the runs its columns are read from.</summary>
    internal bool HasMaterializedColumns => _lines.Any(static line => line.RunCount > 0);

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

        IReadOnlyList<int> boundaries = IsFastPath
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

    internal ReadOnlySpan<ManagedTextRun> GetRuns(ManagedTextLine line)
    {
        if (line.RunCount < 0)
        {
            BuildFastPathRuns(line);
        }

        return line.RunCount <= 0 ? default : _runs.AsSpan(line.RunStart, line.RunCount);
    }

    /// <summary>
    /// Measures a fast-path line into runs. A fast-path line is laid out from whole-segment widths
    /// and answers its queries from them, but a draw that carries paint spans needs the columns
    /// inside it, and this is where they come from.
    /// </summary>
    private void BuildFastPathRuns(ManagedTextLine line)
    {
        var fragments = _engine.MeasureFragments(Snapshot);
        _fragments = fragments;
        _advances = fragments.Advances;
        _runs = new ManagedTextRun[Math.Max(1, fragments.Count)];
        _runCount = 0;

        // The line's width came from measuring whole segments, so the columns are scaled onto it and
        // the run ends where the line does.
        double measured = 0;
        for (int index = 0; index < fragments.Count; index++)
        {
            measured += fragments.Items[index].Width;
        }
        double scale = measured > 0 ? line.Metrics.Bounds.Width / measured : 1;
        if (scale != 1)
        {
            for (int index = 0; index < fragments.AdvanceCount; index++)
            {
                fragments.Advances[index] = (float)(fragments.Advances[index] * scale);
            }
        }

        double cursor = line.Metrics.Bounds.X;
        line.RunStart = 0;
        for (int index = 0; index < fragments.Count; index++)
        {
            ref readonly var fragment = ref fragments.Items[index];
            double width = fragment.Width * scale;
            _runs[_runCount++] = new ManagedTextRun
            {
                TextStart = fragment.TextStart,
                TextLength = fragment.TextLength,
                StyleIndex = fragment.StyleIndex,
                Font = fragment.Font,
                X = cursor,
                Width = width,
                AdvanceStart = fragment.AdvanceStart,
                AdvanceBase = 0,
                MeasuredHeight = fragment.MeasuredHeight,
                Baseline = fragment.Baseline,
                Kind = fragment.Kind,
                InlineIndex = fragment.InlineIndex
            };
            cursor += width;
        }

        line.RunCount = _runCount;
    }

    internal ReadOnlySpan<ManagedTextRun> GetRuns(int lineIndex) => GetRuns(_lines[lineIndex]);

    internal ref readonly ManagedTextRun GetRun(int runIndex) => ref _runs![runIndex];

    /// <summary>The object an inline run stands for.</summary>
    internal IInlineTextObject? GetInline(in ManagedTextRun run)
        => run.InlineIndex >= 0 && run.InlineIndex < Snapshot.Inlines.Length
            ? Snapshot.Inlines[run.InlineIndex].Object
            : null;

    /// <summary>Column of an insertion inside a text run, measured from the layout's advances.</summary>
    internal double GetColumnX(in ManagedTextRun run, int insertion) => GetRunX(in run, insertion);

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
    internal int[] GetRunBoundaries(int runIndex)
    {
        if (_runBoundaries.TryGetValue(runIndex, out var cached))
        {
            return cached;
        }

        ref var run = ref _runs![runIndex];
        if (run.Kind != ManagedTextRunKind.Text)
        {
            int[] single = [run.TextStart];
            _runBoundaries[runIndex] = single;
            return single;
        }

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
        => GetXForInsertionViaRuns(_lines[lineIndex], insertion);

    private double GetXForInsertionViaRuns(ManagedTextLine line, int insertion)
    {
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
        => HitTestLineViaRuns(_lines[lineIndex], x);

    private CharacterHit HitTestLineViaRuns(ManagedTextLine line, double x)
    {
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
        => TryGetLineRangeExtentViaRuns(_lines[lineIndex], start, end, out left, out right);

    private bool TryGetLineRangeExtentViaRuns(ManagedTextLine line, int start, int end, out double left, out double right)
    {
        var runs = GetRuns(line);
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

    // The queries above ask a line where a column sits; a fast-path line answers from the segments
    // it measured, and every other line from the runs it was assembled into.

    private CharacterHit HitTestLine(ManagedTextLine line, double x)
        => IsFastPath ? HitTestFastPath(line, x) : HitTestLineViaRuns(line, x);

    private double GetXForInsertion(ManagedTextLine line, int insertion)
        => IsFastPath ? GetFastPathX(line, insertion) : GetXForInsertionViaRuns(line, insertion);

    private bool TryGetLineRangeExtent(ManagedTextLine line, int start, int end, out double left, out double right)
    {
        if (IsFastPath)
        {
            left = GetFastPathX(line, start);
            right = GetFastPathX(line, end);
            return true;
        }

        return TryGetLineRangeExtentViaRuns(line, start, end, out left, out right);
    }

    private List<int> GetCaretBoundaries()
    {
        var boundaries = new List<int> { 0 };
        foreach (var line in _lines)
        {
            var runs = GetRuns(line);
            for (int index = 0; index < runs.Length; index++)
            {
                foreach (int boundary in GetRunBoundaries(line.RunStart + index))
                {
                    if (boundaries[^1] != boundary)
                    {
                        boundaries.Add(boundary);
                    }
                }

                if (boundaries[^1] != runs[index].TextEnd)
                {
                    boundaries.Add(runs[index].TextEnd);
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
