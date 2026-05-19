using LibreHardwareMonitor.Hardware;

namespace Aprillz.MewUI.Video.Sample.Diagnostics;

/// <summary>
/// Device-level GPU load sampler backed by LibreHardwareMonitorLib. Replaces the Windows
/// <c>"GPU Engine\Utilization Percentage"</c> PerformanceCounter loop that the video sample
/// used to run from a background thread — that path was the dominant CPU consumer of the
/// stats poll because each pid_* instance required a separate <c>NextValue()</c> call.
/// LibreHardwareMonitor reads from NVML / ADL / Intel D3DKMT directly and exposes one
/// "GPU Core" load sensor per adapter.
/// </summary>
/// <remarks>
/// The figure is <b>device-wide</b>, not process-scoped. On an otherwise-idle machine that's
/// fine for "how hard is this app driving the GPU"; with other GPU consumers running the
/// number reflects total load.
/// </remarks>
internal sealed class GpuLoadSampler : IDisposable
{
    private readonly Computer _computer;
    private readonly List<ISensor> _loadSensors = new();
    private bool _opened;
    private bool _primed;

    public GpuLoadSampler()
    {
        _computer = new Computer
        {
            IsGpuEnabled = true,
        };

        try
        {
            _computer.Open();
            _opened = true;
            CollectSensors();
        }
        catch
        {
            // Best-effort — some sensors require elevated privileges. Degrade silently.
        }
    }

    public bool IsAvailable => _loadSensors.Count > 0;

    public bool IsPrimed => _primed;

    /// <summary>
    /// Samples the current GPU core load (0–100). Returns <see langword="null"/> when no
    /// GPU sensors were discovered (no supported adapter, missing driver, etc.).
    /// </summary>
    public double? Sample()
    {
        if (!_opened || _loadSensors.Count == 0) return null;

        double maxLoad = 0;
        bool any = false;
        foreach (var sensor in _loadSensors)
        {
            sensor.Hardware.Update();
            if (sensor.Value is float v && !float.IsNaN(v))
            {
                if (v > maxLoad) maxLoad = v;
                any = true;
            }
        }

        if (!any) return null;
        _primed = true;
        return Math.Clamp(maxLoad, 0, 100);
    }

    private void CollectSensors()
    {
        foreach (var hw in _computer.Hardware)
        {
            if (hw.HardwareType is not (HardwareType.GpuNvidia
                or HardwareType.GpuAmd
                or HardwareType.GpuIntel))
            {
                continue;
            }

            hw.Update();
            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Load &&
                    string.Equals(sensor.Name, "GPU Core", StringComparison.OrdinalIgnoreCase))
                {
                    _loadSensors.Add(sensor);
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_opened) return;
        try { _computer.Close(); } catch { }
        _opened = false;
    }
}
