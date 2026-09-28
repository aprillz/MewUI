using System.Globalization;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.TaskManager.Sample;

/// <summary>
/// GPU utilization where the platform reports it without a vendor library: the GPU engine counters on
/// Windows, IOKit accelerator statistics on macOS, and the amdgpu driver's busy percentage on Linux.
/// </summary>
internal sealed class GpuReader : IDisposable
{
    private const string ENGINE_COUNTER = @"\GPU Engine(*)\Utilization Percentage";
    private const string DEDICATED_COUNTER = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const string SHARED_COUNTER = @"\GPU Adapter Memory(*)\Shared Usage";

    private readonly PdhQuery? _pdh;
    private readonly List<WindowsAdapter> _windowsAdapters = [];

    private readonly record struct WindowsAdapter(string Name, string Luid, long DedicatedBytes, long SharedBytes);

    public GpuReader()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            _windowsAdapters.AddRange(DxgiAdapters());
            _pdh = new PdhQuery();
            _pdh.Add(ENGINE_COUNTER);
            _pdh.Add(DEDICATED_COUNTER);
            _pdh.Add(SHARED_COUNTER);
        }
        catch { }
    }

    public IReadOnlyList<ResourceSample> Read()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows();
            if (OperatingSystem.IsMacOS()) return ReadMac();
            if (OperatingSystem.IsLinux()) return ReadLinux();
        }
        catch { }
        return [];
    }

    public void Dispose() => _pdh?.Dispose();

    private IReadOnlyList<ResourceSample> ReadWindows()
    {
        if (_pdh == null || _windowsAdapters.Count == 0) return [];
        _pdh.Collect();
        var engines = _pdh.ReadArray(ENGINE_COUNTER);
        var dedicated = _pdh.ReadArray(DEDICATED_COUNTER);
        var shared = _pdh.ReadArray(SHARED_COUNTER);

        var result = new List<ResourceSample>();
        for (int index = 0; index < _windowsAdapters.Count; index++)
        {
            var adapter = _windowsAdapters[index];
            // Instances read pid_<pid>_luid_<luid>_phys_<n>_eng_<n>_engtype_<type>: summed per engine over the
            // processes, the busiest engine is the adapter's utilization, as Task Manager shows it.
            var perEngine = new Dictionary<string, (string Type, double Value)>();
            foreach (var (instance, value) in engines)
            {
                int luid = instance.IndexOf("luid_" + adapter.Luid, StringComparison.OrdinalIgnoreCase);
                if (luid < 0) continue;
                string engine = instance[luid..];
                int typeAt = engine.IndexOf("engtype_", StringComparison.Ordinal);
                string type = typeAt >= 0 ? engine[(typeAt + "engtype_".Length)..] : "Other";
                perEngine[engine] = (type, perEngine.GetValueOrDefault(engine).Value + value);
            }
            double utilization = perEngine.Count > 0 ? Math.Clamp(perEngine.Values.Max(entry => entry.Value), 0, 100) : 0;
            var byType = perEngine.Values
                .GroupBy(entry => entry.Type)
                .ToDictionary(group => group.Key, group => Math.Clamp(group.Max(entry => entry.Value), 0, 100));

            long dedicatedUsed = (long)dedicated.Where(entry => entry.Instance.Contains(adapter.Luid, StringComparison.OrdinalIgnoreCase)).Sum(entry => entry.Value);
            long sharedUsed = (long)shared.Where(entry => entry.Instance.Contains(adapter.Luid, StringComparison.OrdinalIgnoreCase)).Sum(entry => entry.Value);

            var metrics = new List<Metric> { new("Utilization", Format.Percent(utilization)) };
            foreach (var type in new[] { "3D", "Copy", "VideoDecode", "VideoEncode" })
            {
                if (byType.TryGetValue(type, out double value)) metrics.Add(new Metric(type switch { "VideoDecode" => "Video decode", "VideoEncode" => "Video encode", _ => type }, Format.Percent(value)));
            }
            metrics.Add(new Metric("Dedicated GPU memory", $"{Format.Gigabytes(dedicatedUsed)}/{Format.Gigabytes(adapter.DedicatedBytes)} GB"));
            metrics.Add(new Metric("Shared GPU memory", $"{Format.Gigabytes(sharedUsed)}/{Format.Gigabytes(adapter.SharedBytes)} GB"));

            result.Add(Sample($"gpu:{index}", $"GPU {index}", adapter.Name, utilization, metrics, []));
        }
        return result;
    }

    private static IReadOnlyList<ResourceSample> ReadMac()
    {
        var result = new List<ResourceSample>();
        MacNative.VisitServices("IOAccelerator", (_, properties) =>
        {
            nint statistics = MacNative.Dictionary(properties, "PerformanceStatistics");
            if (statistics == 0) return;
            double utilization = Math.Clamp(MacNative.Int64(statistics, "Device Utilization %") ?? 0, 0, 100);
            string name = MacNative.String(properties, "model") ?? MacNative.String(properties, "IOClass") ?? "GPU";
            var metrics = new List<Metric> { new("Utilization", Format.Percent(utilization)) };
            if (MacNative.Int64(statistics, "Renderer Utilization %") is long renderer) metrics.Add(new Metric("Renderer", Format.Percent(renderer)));
            if (MacNative.Int64(statistics, "Tiler Utilization %") is long tiler) metrics.Add(new Metric("Tiler", Format.Percent(tiler)));
            if (MacNative.Int64(statistics, "In use system memory") is long inUse) metrics.Add(new Metric("GPU memory in use", Format.Bytes(inUse)));
            var details = new List<Metric>();
            if (MacNative.Int64(properties, "gpu-core-count") is long cores) details.Add(new Metric("GPU cores", cores.ToString(CultureInfo.CurrentCulture)));
            int index = result.Count;
            result.Add(Sample($"gpu:{index}", $"GPU {index}", name, utilization, metrics, details));
        });
        return result;
    }

    private static IReadOnlyList<ResourceSample> ReadLinux()
    {
        var result = new List<ResourceSample>();
        foreach (var card in Directory.EnumerateDirectories("/sys/class/drm", "card*").Where(path => !Path.GetFileName(path).Contains('-')).OrderBy(path => path, StringComparer.Ordinal))
        {
            string device = Path.Combine(card, "device");
            if (!long.TryParse(ReadText(Path.Combine(device, "gpu_busy_percent")), out long busy)) continue;
            var metrics = new List<Metric> { new("Utilization", Format.Percent(busy)) };
            if (long.TryParse(ReadText(Path.Combine(device, "mem_info_vram_used")), out long used) &&
                long.TryParse(ReadText(Path.Combine(device, "mem_info_vram_total")), out long total))
                metrics.Add(new Metric("Dedicated GPU memory", $"{Format.Gigabytes(used)}/{Format.Gigabytes(total)} GB"));
            string name = ReadText(Path.Combine(device, "product_name")) ?? $"{Path.GetFileName(card)} ({ReadText(Path.Combine(device, "vendor"))})";
            int index = result.Count;
            result.Add(Sample($"gpu:{index}", $"GPU {index}", name, busy, metrics, [new Metric("Driver", "amdgpu")]));
        }
        return result;
    }

    private static ResourceSample Sample(string id, string title, string name, double utilization, IReadOnlyList<Metric> metrics, IReadOnlyList<Metric> properties) =>
        new(id, ResourceKind.Gpu, title, name, Format.Percent(utilization), name, new ChartSample("Utilization", utilization), null, metrics, properties);

    private static List<WindowsAdapter> DxgiAdapters()
    {
        const int SOFTWARE_ADAPTER = 2;
        var result = new List<WindowsAdapter>();
        var factoryId = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref factoryId, out nint factory) != 0 || factory == 0) return result;
        try
        {
            unsafe
            {
                var factoryTable = *(nint**)factory;
                var enumAdapters1 = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)factoryTable[12];
                var descBuffer = stackalloc byte[312];
                for (uint index = 0; ; index++)
                {
                    nint adapter;
                    if (enumAdapters1(factory, index, &adapter) != 0 || adapter == 0) break;
                    try
                    {
                        var adapterTable = *(nint**)adapter;
                        var getDesc1 = (delegate* unmanaged[Stdcall]<nint, byte*, int>)adapterTable[10];
                        var desc = descBuffer;
                        if (getDesc1(adapter, desc) != 0) continue;
                        int flags = *(int*)(desc + 304);
                        if ((flags & SOFTWARE_ADAPTER) != 0) continue;
                        string name = new string((char*)desc).Trim();
                        uint low = *(uint*)(desc + 296);
                        int high = *(int*)(desc + 300);
                        long dedicated = (long)*(nuint*)(desc + 272);
                        long shared = (long)*(nuint*)(desc + 288);
                        result.Add(new WindowsAdapter(name, $"0x{high:X8}_0x{low:X8}", dedicated, shared));
                    }
                    finally
                    {
                        var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)adapter)[2];
                        release(adapter);
                    }
                }
            }
        }
        finally
        {
            unsafe
            {
                var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)factory)[2];
                release(factory);
            }
        }
        return result;
    }

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch { return null; }
    }

    [DllImport("dxgi")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out nint factory);
}
