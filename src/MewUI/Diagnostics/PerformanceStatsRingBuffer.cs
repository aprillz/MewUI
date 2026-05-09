namespace Aprillz.MewUI.Diagnostics;

internal sealed class PerformanceStatsRingBuffer
{
    private readonly FramePerformanceStats[] _frames;
    private int _next;
    private int _count;

    public PerformanceStatsRingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _frames = new FramePerformanceStats[capacity];
    }

    public int Count => _count;
    public int Capacity => _frames.Length;
    public FramePerformanceStats Latest { get; private set; }

    public void Add(FramePerformanceStats stats)
    {
        _frames[_next] = stats;
        Latest = stats;
        _next = (_next + 1) % _frames.Length;
        if (_count < _frames.Length)
        {
            _count++;
        }
    }

    public int CopyTo(Span<FramePerformanceStats> destination)
    {
        int count = Math.Min(_count, destination.Length);
        int start = (_next - _count + _frames.Length) % _frames.Length;
        for (int i = 0; i < count; i++)
        {
            destination[i] = _frames[(start + i) % _frames.Length];
        }

        return count;
    }

    public RollingPerformanceStats GetRollingStats()
        => GetRollingStats(sourceId: null);

    public RollingPerformanceStats GetRollingStats(long sourceId)
        => GetRollingStats((long?)sourceId);

    private RollingPerformanceStats GetRollingStats(long? sourceId)
    {
        if (_count == 0)
        {
            return default;
        }

        double total = 0;
        double min = double.MaxValue;
        double max = 0;
        int count = 0;

        int start = (_next - _count + _frames.Length) % _frames.Length;
        for (int i = 0; i < _count; i++)
        {
            var frame = _frames[(start + i) % _frames.Length];
            if (sourceId.HasValue && frame.SourceId != sourceId.Value)
            {
                continue;
            }

            double frameMs = frame.FrameMs;
            total += frameMs;
            min = Math.Min(min, frameMs);
            max = Math.Max(max, frameMs);
            count++;
        }

        if (count == 0)
        {
            return default;
        }

        double avg = total / count;
        double fps = avg > 0 ? 1000.0 / avg : 0;
        return new RollingPerformanceStats(avg, min, max, fps, count);
    }

    public int CopyTo(Span<FramePerformanceStats> destination, long sourceId)
    {
        int copied = 0;
        int start = (_next - _count + _frames.Length) % _frames.Length;
        for (int i = 0; i < _count && copied < destination.Length; i++)
        {
            var frame = _frames[(start + i) % _frames.Length];
            if (frame.SourceId != sourceId)
            {
                continue;
            }

            destination[copied++] = frame;
        }

        return copied;
    }

    public FramePerformanceStats GetLatest(long sourceId)
    {
        for (int i = 1; i <= _count; i++)
        {
            int index = (_next - i + _frames.Length) % _frames.Length;
            var frame = _frames[index];
            if (frame.SourceId == sourceId)
            {
                return frame;
            }
        }

        return default;
    }
}

internal readonly struct RollingPerformanceStats
{
    public RollingPerformanceStats(double averageFrameMs, double minFrameMs, double maxFrameMs, double fps, int sampleCount)
    {
        AverageFrameMs = averageFrameMs;
        MinFrameMs = minFrameMs;
        MaxFrameMs = maxFrameMs;
        Fps = fps;
        SampleCount = sampleCount;
    }

    public double AverageFrameMs { get; }
    public double MinFrameMs { get; }
    public double MaxFrameMs { get; }
    public double Fps { get; }
    public int SampleCount { get; }
}
