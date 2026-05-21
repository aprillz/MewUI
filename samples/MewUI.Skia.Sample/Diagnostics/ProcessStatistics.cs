using System.Diagnostics;

namespace Aprillz.MewUI.Skia.Sample.Diagnostics;

/// <summary>
/// 1Hz CPU / GPU / memory snapshot. CPU via <see cref="Process.TotalProcessorTime"/>
/// (AOT-safe); GPU via Windows <c>PerformanceCounter "GPU Engine"</c> — Debug builds only,
/// since the counter package has trim / NativeAOT compatibility issues.
/// </summary>
internal sealed class ProcessStatistics : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private CancellationTokenSource? _cts;

    private TimeSpan _lastCpuTime;
    private long _lastCpuSampleTicks;

#if DEBUG
    private GpuLoadSampler? _gpuSampler;
#endif

    public event Action<StatsSnapshot>? StatsUpdated;

    public void Start()
    {
        if (_cts != null) return;

        _lastCpuTime = _process.TotalProcessorTime;
        _lastCpuSampleTicks = Stopwatch.GetTimestamp();
#if DEBUG
        if (OperatingSystem.IsWindows())
        {
            _gpuSampler = new GpuLoadSampler(_process.Id);
        }
#endif

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    public void Dispose()
    {
        Stop();
#if DEBUG
        _gpuSampler?.Dispose();
        _gpuSampler = null;
#endif
        _process.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                StatsUpdated?.Invoke(Snapshot());
            }
        }
        catch (OperationCanceledException) { }
    }

    private StatsSnapshot Snapshot()
    {
        _process.Refresh();

        var cpuTime = _process.TotalProcessorTime;
        long nowTicks = Stopwatch.GetTimestamp();
        double elapsedSeconds = (nowTicks - _lastCpuSampleTicks) / (double)Stopwatch.Frequency;
        double cpuPercent = elapsedSeconds > 0
            ? Math.Clamp((cpuTime - _lastCpuTime).TotalSeconds / (elapsedSeconds * Environment.ProcessorCount) * 100.0, 0, 999)
            : 0;
        _lastCpuTime = cpuTime;
        _lastCpuSampleTicks = nowTicks;

        double? gpuPercent = null;
#if DEBUG
        gpuPercent = _gpuSampler?.Sample();
#endif

        return new StatsSnapshot(
            cpuPercent,
            gpuPercent,
            _process.WorkingSet64,
            _process.PrivateMemorySize64,
            GC.GetTotalMemory(forceFullCollection: false));
    }
}

internal readonly record struct StatsSnapshot(
    double CpuPercent,
    double? GpuPercent,
    long WorkingSetBytes,
    long PrivateBytes,
    long ManagedHeapBytes);

#if DEBUG
/// <summary>Debug-only GPU load via Windows PerformanceCounter "GPU Engine". Trim/AOT-unsafe — gated to Debug builds.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416", Justification = "PerformanceCounter calls gated by OperatingSystem.IsWindows() at runtime.")]
internal sealed class GpuLoadSampler : IDisposable
{
    private readonly int _pid;
    private PerformanceCounter[] _counters = Array.Empty<PerformanceCounter>();
    private bool _disposed;

    public GpuLoadSampler(int pid) { _pid = pid; }

    public double? Sample()
    {
        if (_disposed || !OperatingSystem.IsWindows()) return null;
        if (_counters.Length == 0) Refresh();
        if (_counters.Length == 0) return null;

        float total = 0;
        foreach (var c in _counters)
        {
            try { total += c.NextValue(); } catch { }
        }
        return Math.Clamp(total, 0, 999);
    }

    private void Refresh()
    {
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            string pidTag = $"pid_{_pid}_";
            var matched = new List<PerformanceCounter>();
            foreach (var inst in category.GetInstanceNames())
            {
                if (inst.Contains(pidTag, StringComparison.Ordinal))
                {
                    var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, readOnly: true);
                    c.NextValue();
                    matched.Add(c);
                }
            }
            foreach (var c in _counters) c.Dispose();
            _counters = matched.ToArray();
        }
        catch { /* GPU Engine category unavailable */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var c in _counters) c.Dispose();
        _counters = Array.Empty<PerformanceCounter>();
    }
}
#endif
