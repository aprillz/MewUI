using System.Diagnostics;

using Aprillz.MewUI.Video.Sample.Controls;
using Aprillz.MewUI.Video.Sample.Decoding;

namespace Aprillz.MewUI.Video.Sample.Diagnostics;

/// <summary>
/// 250 ms CPU sampling with 1 Hz publish + memory + (Debug-only) GPU. CPU via
/// <see cref="Process.TotalProcessorTime"/> (AOT-safe); GPU via Windows
/// <c>PerformanceCounter "GPU Engine"</c> in Debug builds only.
/// </summary>
internal sealed class ProcessStatistics : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly HashSet<string> _loggedErrors = new();
    private CancellationTokenSource? _cts;

    private TimeSpan _lastCpuTime;
    private long _lastCpuSampleTicks;
    private double _lastCpuPercent;
    private double _lastCpuAveragePercent;
    private double _lastCpuMinPercent;
    private double _lastCpuMaxPercent;
    private double _intervalCpuSeconds;
    private double _intervalElapsedSeconds;
    private double _intervalCpuMinPercent = double.PositiveInfinity;
    private double _intervalCpuMaxPercent;

#if DEBUG
    private GpuLoadSampler? _gpuSampler;
#endif

    /// <summary>Optional callback returning the active Metal device handle for macOS GPU bytes sampling. Set by the host once <c>Playback</c> is ready.</summary>
    public Func<nint>? GetMetalDevice { get; set; }

    public event Action<HostStats>? StatsUpdated;

    public void Start()
    {
        if (_cts != null) return;
        ResetSamplingState();
#if DEBUG
        if (OperatingSystem.IsWindows()) _gpuSampler = new GpuLoadSampler(_process.Id);
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

    private void ResetSamplingState()
    {
        _process.Refresh();
        _lastCpuTime = _process.TotalProcessorTime;
        _lastCpuSampleTicks = Stopwatch.GetTimestamp();
        _lastCpuPercent = 0;
        _lastCpuAveragePercent = 0;
        _lastCpuMinPercent = 0;
        _lastCpuMaxPercent = 0;
        _intervalCpuSeconds = 0;
        _intervalElapsedSeconds = 0;
        _intervalCpuMinPercent = double.PositiveInfinity;
        _intervalCpuMaxPercent = 0;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            long nextCpuPublishTicks = Stopwatch.GetTimestamp() + Stopwatch.Frequency;

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    _process.Refresh();
                    SampleCpu();

                    long nowTicks = Stopwatch.GetTimestamp();
                    if (nowTicks >= nextCpuPublishTicks)
                    {
                        FinalizeCpuInterval();
                        nextCpuPublishTicks = nowTicks + Stopwatch.Frequency;
                    }

                    double? gpuPercent = SampleGpuPercent();

                    long privateBytes = OperatingSystem.IsMacOS() && MacOsNative.TryGetPhysFootprint(out ulong physFootprint)
                        ? (long)physFootprint
                        : _process.PrivateMemorySize64;

                    ulong metalGpuBytes = 0;
                    if (OperatingSystem.IsMacOS() && GetMetalDevice is { } md)
                    {
                        metalGpuBytes = MacOsNative.GetMetalAllocatedSize(md());
                    }

                    StatsUpdated?.Invoke(new HostStats(
                        CpuAveragePercent: _lastCpuAveragePercent,
                        CpuMinPercent: _lastCpuMinPercent,
                        CpuMaxPercent: _lastCpuMaxPercent,
                        GpuPercent: gpuPercent,
                        WorkingSetBytes: _process.WorkingSet64,
                        PrivateBytes: privateBytes,
                        ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
                        MetalGpuBytes: metalGpuBytes));
                }
                catch (Exception perTickEx)
                {
                    // A single bad sample shouldn't kill the loop. Log once per unique message
                    // so a recurring failure doesn't spam the log.
                    string key = $"{perTickEx.GetType().Name}: {perTickEx.Message}";
                    if (_loggedErrors.Add(key))
                    {
                        SampleLog.Write($"ProcessStatistics tick failed (will retry): {key}\n{perTickEx.StackTrace}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SampleLog.Write($"ProcessStatistics loop terminated by exception: {ex}");
        }
    }

    private void SampleCpu()
    {
        var cpuTime = _process.TotalProcessorTime;
        long nowTicks = Stopwatch.GetTimestamp();
        double elapsedSeconds = (nowTicks - _lastCpuSampleTicks) / (double)Stopwatch.Frequency;
        if (elapsedSeconds <= 0) return;

        double cpuSeconds = (cpuTime - _lastCpuTime).TotalSeconds;
        _lastCpuPercent = Math.Clamp(cpuSeconds / (elapsedSeconds * Environment.ProcessorCount) * 100.0, 0, 999);
        _intervalCpuSeconds += Math.Max(0, cpuSeconds);
        _intervalElapsedSeconds += elapsedSeconds;
        _intervalCpuMinPercent = Math.Min(_intervalCpuMinPercent, _lastCpuPercent);
        _intervalCpuMaxPercent = Math.Max(_intervalCpuMaxPercent, _lastCpuPercent);

        _lastCpuTime = cpuTime;
        _lastCpuSampleTicks = nowTicks;
    }

    private void FinalizeCpuInterval()
    {
        if (_intervalElapsedSeconds > 0)
        {
            double intervalPercent = _intervalCpuSeconds / (_intervalElapsedSeconds * Environment.ProcessorCount) * 100.0;
            _lastCpuAveragePercent = Math.Clamp(intervalPercent, 0, 999);
            _lastCpuMinPercent = double.IsPositiveInfinity(_intervalCpuMinPercent) ? _lastCpuAveragePercent : _intervalCpuMinPercent;
            _lastCpuMaxPercent = _intervalCpuMaxPercent;
        }
        _intervalCpuSeconds = 0;
        _intervalElapsedSeconds = 0;
        _intervalCpuMinPercent = double.PositiveInfinity;
        _intervalCpuMaxPercent = 0;
    }

    private double? SampleGpuPercent()
    {
#if DEBUG
        return _gpuSampler?.Sample();
#else
        return null;
#endif
    }
}

#if DEBUG
/// <summary>Debug-only GPU load via Windows PerformanceCounter "GPU Engine". Trim/AOT-unsafe — gated to Debug builds.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416", Justification = "PerformanceCounter calls gated by OperatingSystem.IsWindows() at runtime.")]
internal sealed class GpuLoadSampler : IDisposable
{
    private readonly int _pid;
    private PerformanceCounter[] _counters = Array.Empty<PerformanceCounter>();
    private bool _primed;
    private bool _disposed;

    public GpuLoadSampler(int pid) { _pid = pid; }

    public bool IsAvailable => OperatingSystem.IsWindows();
    public bool IsPrimed => _primed;

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
        _primed = true;
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
        catch { /* category unavailable → stays empty */ }
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
