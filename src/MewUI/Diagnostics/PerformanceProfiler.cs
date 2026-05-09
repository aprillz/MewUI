using System.Diagnostics;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Diagnostics;

internal sealed class PerformanceProfiler
{
    private const int DefaultFrameCapacity = 512;
    private const int MaxSamplesPerFrame = 4096;

    private static readonly PerformanceProfiler Shared = new();

    private readonly PerformanceStatsRingBuffer _stats = new(DefaultFrameCapacity);
    private readonly FrameProfilerData?[] _timeline = new FrameProfilerData?[DefaultFrameCapacity];
    private readonly Dictionary<Type, ElementProfilerMarkers> _elementMarkers = new();
    private readonly ProfilerSampleBuilder[] _sampleBuilders = new ProfilerSampleBuilder[MaxSamplesPerFrame];
    private readonly int[] _sampleStack = new int[256];
    private int _timelineNext;
    private int _timelineCount;
    private int _sampleCount;
    private int _sampleOverflowCount;
    private int _sampleStackCount;
    private long _nextFrameIndex;
    private bool _isCollectingFrame;

    private PerformanceProfiler()
    {
    }

    public static PerformanceProfiler Instance => Shared;

    public bool IsEnabled { get; set; }
    public FramePerformanceStats LatestFrame => _stats.Latest;
    public RollingPerformanceStats RollingStats => _stats.GetRollingStats();
    public FramePerformanceStats GetLatestFrame(long sourceId) => _stats.GetLatest(sourceId);

    public FrameTimingBuilder BeginFrame(long sourceId)
    {
        bool enabled = IsEnabled;
        var builder = new FrameTimingBuilder
        {
            Enabled = enabled,
            FrameIndex = Interlocked.Increment(ref _nextFrameIndex),
            SourceId = sourceId,
        };

        if (!enabled)
        {
            return builder;
        }

        _sampleCount = 0;
        _sampleOverflowCount = 0;
        _sampleStackCount = 0;
        _isCollectingFrame = true;
        builder.FrameStart = Stopwatch.GetTimestamp();
        builder.StartAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
        builder.StartGen0Collections = GC.CollectionCount(0);
        builder.StartGen1Collections = GC.CollectionCount(1);
        builder.StartGen2Collections = GC.CollectionCount(2);
        BeginSample(ProfilerMarkers.WindowFrame);
        return builder;
    }

    public void CommitFrame(ref FrameTimingBuilder builder, LayoutPerformanceStats layout, int drawCalls, int cullCount, RenderPrimitiveStats primitiveStats)
    {
        if (!builder.Enabled)
        {
            return;
        }

        builder.FrameEnd = Stopwatch.GetTimestamp();
        if (_sampleStackCount > 0)
        {
            EndSample(_sampleStack[_sampleStackCount - 1]);
        }

        var stats = new FramePerformanceStats(
            builder.FrameIndex,
            builder.SourceId,
            FrameTimingBuilder.ToMilliseconds(builder.FrameEnd - builder.FrameStart),
            FrameTimingBuilder.ToMilliseconds(builder.BeginFrameTicks),
            FrameTimingBuilder.ToMilliseconds(builder.AnimationTicks),
            layout.LayoutMs,
            layout.MeasureMs,
            layout.ArrangeMs,
            FrameTimingBuilder.ToMilliseconds(builder.RenderBodyTicks),
            FrameTimingBuilder.ToMilliseconds(builder.DevToolsTicks),
            FrameTimingBuilder.ToMilliseconds(builder.EndFrameTicks),
            FrameTimingBuilder.ToMilliseconds(builder.PresentTicks),
            drawCalls,
            cullCount,
            primitiveStats,
            Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - builder.StartAllocatedBytes),
            Math.Max(0, GC.CollectionCount(0) - builder.StartGen0Collections),
            Math.Max(0, GC.CollectionCount(1) - builder.StartGen1Collections),
            Math.Max(0, GC.CollectionCount(2) - builder.StartGen2Collections),
            layout.LayoutRan,
            layout.MeasureRan,
            layout.ArrangeRan);

        _stats.Add(stats);
        int gcCollections = stats.Gen0Collections + stats.Gen1Collections + stats.Gen2Collections;
        int sampleCount = _sampleCount + (gcCollections > 0 ? 1 : 0);
        var frame = _timeline[_timelineNext] ??= new FrameProfilerData();
        frame.Set(stats, sampleCount, _sampleOverflowCount, ProfilerMarkerRegistry.Version);
        var samples = frame.Samples;
        for (int i = 0; i < _sampleCount; i++)
        {
            samples[i] = _sampleBuilders[i].ToSample();
        }

        if (gcCollections > 0)
        {
            long gcTicks = Math.Max(1, (long)(Stopwatch.Frequency * Math.Min(1, Math.Max(0.05, stats.FrameMs * 0.03)) / 1000.0));
            samples[sampleCount - 1] = new ProfilerSample(
                parentIndex: 0,
                markerId: ProfilerMarkers.GCCollect.Id,
                category: ProfilerSampleCategory.GC,
                startTimestamp: Math.Max(builder.FrameStart, builder.FrameEnd - gcTicks),
                endTimestamp: builder.FrameEnd,
                depth: 1,
                target: null);
        }

        _timelineNext = (_timelineNext + 1) % _timeline.Length;
        if (_timelineCount < _timeline.Length)
        {
            _timelineCount++;
        }

        _isCollectingFrame = false;
    }

    public int CopyFrames(Span<FramePerformanceStats> destination)
        => _stats.CopyTo(destination);

    public int CopyFrames(Span<FramePerformanceStats> destination, long sourceId)
        => _stats.CopyTo(destination, sourceId);

    public RollingPerformanceStats GetRollingStats(long sourceId)
        => _stats.GetRollingStats(sourceId);

    public FrameProfilerData? GetLatestTimelineFrame()
    {
        if (_timelineCount == 0)
        {
            return null;
        }

        int index = (_timelineNext - 1 + _timeline.Length) % _timeline.Length;
        return _timeline[index];
    }

    public FrameProfilerData? GetLatestTimelineFrame(long sourceId)
    {
        if (_timelineCount == 0)
        {
            return null;
        }

        for (int i = 1; i <= _timelineCount; i++)
        {
            int index = (_timelineNext - i + _timeline.Length) % _timeline.Length;
            var frame = _timeline[index];
            if (frame?.Stats.SourceId == sourceId)
            {
                return frame;
            }
        }

        return null;
    }

    public FrameProfilerData? GetTimelineFrame(long frameIndex)
    {
        if (_timelineCount == 0)
        {
            return null;
        }

        int start = (_timelineNext - _timelineCount + _timeline.Length) % _timeline.Length;
        for (int i = 0; i < _timelineCount; i++)
        {
            var frame = _timeline[(start + i) % _timeline.Length];
            if (frame?.Stats.FrameIndex == frameIndex)
            {
                return frame;
            }
        }

        return null;
    }

    public FrameProfilerData? GetTimelineFrame(long sourceId, long frameIndex)
    {
        if (_timelineCount == 0)
        {
            return null;
        }

        int start = (_timelineNext - _timelineCount + _timeline.Length) % _timeline.Length;
        for (int i = 0; i < _timelineCount; i++)
        {
            var frame = _timeline[(start + i) % _timeline.Length];
            if (frame?.Stats.SourceId == sourceId && frame.Stats.FrameIndex == frameIndex)
            {
                return frame;
            }
        }

        return null;
    }

    public static ProfilerScope Sample(ProfilerMarker marker)
        => Instance.BeginSample(marker);

    public ProfilerScope SampleElement(Type elementType, ProfilerSampleCategory category, UIElement? target = null)
    {
        if (!IsEnabled || !_isCollectingFrame)
        {
            return default;
        }

        var markers = GetElementMarkers(elementType);
        return category switch
        {
            ProfilerSampleCategory.Measure => BeginSample(markers.Measure, target),
            ProfilerSampleCategory.Arrange => BeginSample(markers.Arrange, target),
            ProfilerSampleCategory.Render => BeginSample(markers.Render, target),
            _ => default,
        };
    }

    internal ProfilerScope BeginSample(ProfilerMarker marker)
        => BeginSample(marker, target: null);

    private ProfilerScope BeginSample(ProfilerMarker marker, UIElement? target)
    {
        if (!IsEnabled || !_isCollectingFrame || marker.Id < 0)
        {
            return default;
        }

        if (_sampleCount >= _sampleBuilders.Length)
        {
            _sampleOverflowCount++;
            return default;
        }

        int parent = _sampleStackCount > 0 ? _sampleStack[_sampleStackCount - 1] : -1;
        int depth = _sampleStackCount;
        int index = _sampleCount++;
        _sampleBuilders[index] = new ProfilerSampleBuilder(parent, marker.Id, marker.Category, Stopwatch.GetTimestamp(), depth, target);

        if (_sampleStackCount < _sampleStack.Length)
        {
            _sampleStack[_sampleStackCount++] = index;
        }

        return new ProfilerScope(this, index);
    }

    internal void EndSample(int sampleIndex)
    {
        if ((uint)sampleIndex >= (uint)_sampleCount)
        {
            return;
        }

        _sampleBuilders[sampleIndex].EndTimestamp = Stopwatch.GetTimestamp();
        if (_sampleStackCount > 0 && _sampleStack[_sampleStackCount - 1] == sampleIndex)
        {
            _sampleStackCount--;
        }
    }

    private ElementProfilerMarkers GetElementMarkers(Type elementType)
    {
        lock (_elementMarkers)
        {
            if (_elementMarkers.TryGetValue(elementType, out var markers))
            {
                return markers;
            }

            string name = elementType.Name;
            markers = new ElementProfilerMarkers(
                ProfilerMarker.Register(name + ".Measure", ProfilerSampleCategory.Measure),
                ProfilerMarker.Register(name + ".Arrange", ProfilerSampleCategory.Arrange),
                ProfilerMarker.Register(name + ".Render", ProfilerSampleCategory.Render));
            _elementMarkers.Add(elementType, markers);
            return markers;
        }
    }

    private readonly struct ElementProfilerMarkers
    {
        public ElementProfilerMarkers(ProfilerMarker measure, ProfilerMarker arrange, ProfilerMarker render)
        {
            Measure = measure;
            Arrange = arrange;
            Render = render;
        }

        public ProfilerMarker Measure { get; }
        public ProfilerMarker Arrange { get; }
        public ProfilerMarker Render { get; }
    }

    private struct ProfilerSampleBuilder
    {
        public ProfilerSampleBuilder(int parentIndex, int markerId, ProfilerSampleCategory category, long startTimestamp, int depth, UIElement? target)
        {
            ParentIndex = parentIndex;
            MarkerId = markerId;
            Category = category;
            StartTimestamp = startTimestamp;
            EndTimestamp = startTimestamp;
            Depth = depth;
            Target = target;
        }

        public int ParentIndex;
        public int MarkerId;
        public ProfilerSampleCategory Category;
        public long StartTimestamp;
        public long EndTimestamp;
        public int Depth;
        public UIElement? Target;

        public ProfilerSample ToSample()
            => new(ParentIndex, MarkerId, Category, StartTimestamp, EndTimestamp, Depth, Target);
    }
}
