using Aprillz.MewUI.Diagnostics;

namespace Aprillz.MewUI;

partial class Window
{
    private static long _nextProfilerSourceId;

    private readonly long _profilerSourceId = Interlocked.Increment(ref _nextProfilerSourceId);
    private bool _excludeFromProfiler;

    /// <summary>
    /// Per-window identity used by the profiler ring buffer to attribute frame stats back
    /// to the originating window. Allocated once at construction; never reassigned.
    /// </summary>
    internal long ProfilerSourceId => _profilerSourceId;

    /// <summary>
    /// When true the window opts out of profiler frame collection - used by the profiler's
    /// own DevTools windows so they don't recursively profile themselves.
    /// </summary>
    internal bool ExcludeFromProfiler
    {
        get => _excludeFromProfiler;
        set => _excludeFromProfiler = value;
    }

    /// <summary>
    /// Gets the performance statistics from the most recent profiled frame, or the default value
    /// when the build did not enable the development tools (see <see cref="Window.DevTools"/>).
    /// </summary>
    public FramePerformanceStats LastFramePerformanceStats { get; private set; }
}
