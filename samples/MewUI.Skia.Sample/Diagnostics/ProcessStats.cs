using System.Diagnostics;

namespace Aprillz.MewUI.Skia.Sample.Diagnostics;

/// <summary>1Hz PerformanceCounter-backed CPU/GPU/memory snapshot. Counters are created once and reused; no per-tick allocation.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416", Justification = "PerformanceCounter calls are gated by OperatingSystem.IsWindows() at runtime.")]
internal sealed class ProcessStatistics : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private CancellationTokenSource? _cts;

    // CPU: Process / % Processor Time, normalized to total cores (= same as Task Manager).
    private PerformanceCounter? _cpuCounter;

    // GPU: GPU Engine / Utilization Percentage. One instance per (PID, engine type). We snapshot
    // instance names matching this process ONCE at startup and reuse the counters; new engines
    // appearing mid-run aren't picked up (acceptable for our diagnostic use).
    private PerformanceCounter[] _gpuCounters = Array.Empty<PerformanceCounter>();

    public event Action<StatsSnapshot>? StatsUpdated;

    public void Start()
    {
        if (_cts != null) return;
        InitCounters();

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
        _cpuCounter?.Dispose();
        foreach (var c in _gpuCounters) c.Dispose();
        _gpuCounters = Array.Empty<PerformanceCounter>();
        _process.Dispose();
    }

    private void InitCounters()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            // Find the unique instance name PerformanceCounter assigns to our process (matches
            // by process name + ordinal suffix, e.g. "Aprillz.MewUI.Skia.Sample#1").
            string instance = ResolveProcessInstance(_process.Id, _process.ProcessName);
            _cpuCounter = new PerformanceCounter("Process", "% Processor Time", instance, readOnly: true);
            _cpuCounter.NextValue();  // prime — first call returns 0
        }
        catch { /* counter unavailable → CPU shows "n/a" */ }

        RefreshGpuCounters();
    }

    /// <summary>Rescans GPU Engine instances for this PID. Instances appear lazily once the process touches the GPU, so we retry on every empty snapshot.</summary>
    private void RefreshGpuCounters()
    { 
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            string pidTag = $"pid_{_process.Id}_";
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
            // Dispose any old counters we're replacing (instance set may shrink across runs too).
            foreach (var c in _gpuCounters) c.Dispose();
            _gpuCounters = matched.ToArray();
        }
        catch { /* GPU Engine category unavailable */ }
    }

    private static string ResolveProcessInstance(int pid, string processName)
    {
        // Walk Process / ID Process counter for each instance with our process name until
        // ID matches. PerformanceCounterCategory is the cheap lookup; we call this once.
        var category = new PerformanceCounterCategory("Process");
        foreach (var inst in category.GetInstanceNames())
        {
            if (!inst.StartsWith(processName, StringComparison.Ordinal)) continue;
            try
            {
                using var idCounter = new PerformanceCounter("Process", "ID Process", inst, readOnly: true);
                if ((int)idCounter.NextValue() == pid) return inst;
            }
            catch { }
        }
        return processName;
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
        // % Processor Time returns the sum across cores (e.g. 800% on an 8-core saturated process).
        // Divide by core count to match Task Manager's 0-100% per-process scale.
        string cpu = "n/a";
        if (_cpuCounter is not null)
        {
            try { cpu = $"{_cpuCounter.NextValue() / Environment.ProcessorCount:0.0}%"; }
            catch { }
        }

        // Engine instances appear lazily once the process touches the GPU. Retry until we see some.
        if (_gpuCounters.Length == 0) RefreshGpuCounters();

        string gpu = "n/a";
        if (_gpuCounters.Length > 0)
        {
            float total = 0;
            foreach (var c in _gpuCounters)
            {
                try { total += c.NextValue(); } catch { }
            }
            gpu = $"{total:0.0}%";
        }

        _process.Refresh();
        return new StatsSnapshot(cpu, gpu, _process.WorkingSet64, _process.PrivateMemorySize64, GC.GetTotalMemory(forceFullCollection: false));
    }
}

internal readonly record struct StatsSnapshot(
    string CpuUsageText,
    string GpuUsageText,
    long WorkingSetBytes,
    long PrivateBytes,
    long ManagedHeapBytes);
