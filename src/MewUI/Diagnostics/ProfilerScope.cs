namespace Aprillz.MewUI.Diagnostics;

internal readonly struct ProfilerScope : IDisposable
{
    private readonly PerformanceProfiler? _profiler;
    private readonly int _sampleIndex;

    internal ProfilerScope(PerformanceProfiler? profiler, int sampleIndex)
    {
        _profiler = profiler;
        _sampleIndex = sampleIndex;
    }

    public void Dispose()
    {
        _profiler?.EndSample(_sampleIndex);
    }
}
