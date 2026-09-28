using System.Globalization;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.TaskManager.Sample;

/// <summary>What does not change about the processor, read once, and its current clock where the platform reports it.</summary>
internal sealed class CpuInfoReader : IDisposable
{
    private const string PERFORMANCE_COUNTER = @"\Processor Information(_Total)\% Processor Performance";

    private readonly PdhQuery? _pdh;
    private readonly double? _baseMhz;

    public CpuInfoReader()
    {
        (Name, _baseMhz, Properties) = ReadStatic();
        if (OperatingSystem.IsWindows())
        {
            _pdh = new PdhQuery();
            if (!_pdh.Add(PERFORMANCE_COUNTER)) _pdh.Dispose();
        }
    }

    public string Name { get; }

    public IReadOnlyList<Metric> Properties { get; }

    /// <summary>The average clock across the processors now, in MHz, or null where the platform does not tell.</summary>
    public double? ReadSpeedMhz()
    {
        if (OperatingSystem.IsWindows())
        {
            // The same derivation Task Manager uses: the share of the rated clock the processors run at.
            _pdh?.Collect();
            if (_pdh?.Read(PERFORMANCE_COUNTER) is double performance && _baseMhz is double baseMhz)
                return baseMhz * performance / 100;
            return WindowsPowerInformation().CurrentMhz;
        }

        if (OperatingSystem.IsLinux())
        {
            var speeds = new List<double>();
            foreach (var directory in Directory.EnumerateDirectories("/sys/devices/system/cpu", "cpu*"))
            {
                if (ReadLong(Path.Combine(directory, "cpufreq", "scaling_cur_freq")) is long kilohertz)
                    speeds.Add(kilohertz / 1000.0);
            }
            if (speeds.Count > 0) return speeds.Average();
            var cpuinfo = ReadLinuxCpuInfo().Select(entry => entry.GetValueOrDefault("cpu MHz")).Where(value => value != null).ToList();
            return cpuinfo.Count > 0 ? cpuinfo.Average(value => double.Parse(value!, CultureInfo.InvariantCulture)) : null;
        }

        // Apple silicon reports no clock without root; an Intel Mac reports its nominal one.
        return OperatingSystem.IsMacOS() ? _baseMhz : null;
    }

    public void Dispose() => _pdh?.Dispose();

    private static (string Name, double? BaseMhz, IReadOnlyList<Metric> Properties) ReadStatic()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows();
            if (OperatingSystem.IsLinux()) return ReadLinux();
            if (OperatingSystem.IsMacOS()) return ReadMac();
        }
        catch { }

        return ("CPU", null, [new Metric("Logical processors", Environment.ProcessorCount.ToString(CultureInfo.CurrentCulture))]);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static (string, double?, IReadOnlyList<Metric>) ReadWindows()
    {
        string name = "CPU";
        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
        {
            if (key?.GetValue("ProcessorNameString") is string text) name = text.Trim();
        }

        var power = WindowsPowerInformation();
        var topology = WindowsTopology();
        var properties = new List<Metric>();
        if (power.MaxMhz is double baseMhz) properties.Add(new Metric("Base speed", Format.Megahertz(baseMhz)));
        properties.Add(new Metric("Sockets", topology.Sockets.ToString(CultureInfo.CurrentCulture)));
        properties.Add(new Metric("Cores", topology.Cores.ToString(CultureInfo.CurrentCulture)));
        properties.Add(new Metric("Logical processors", Environment.ProcessorCount.ToString(CultureInfo.CurrentCulture)));
        properties.AddRange(WindowsVirtualization());
        AddCaches(properties, topology.Caches);
        return (name, power.MaxMhz, properties);
    }

    private const uint PF_VIRT_FIRMWARE_ENABLED = 21;

    /// <summary>
    /// Firmware virtualization, as Task Manager reports it. Under a running hypervisor the firmware flag
    /// reads as off even in the host, so the hypervisor's own report decides: the host (root partition)
    /// has virtualization enabled, a guest is a virtual machine.
    /// </summary>
    private static IEnumerable<Metric> WindowsVirtualization()
    {
        if (System.Runtime.Intrinsics.X86.X86Base.IsSupported)
        {
            const int HYPERVISOR_PRESENT = 1 << 31;
            const int CREATE_PARTITIONS = 1;
            var (_, _, features, _) = System.Runtime.Intrinsics.X86.X86Base.CpuId(1, 0);
            if ((features & HYPERVISOR_PRESENT) != 0)
            {
                // Leaf 0x40000003 is Hyper-V's feature leaf; CreatePartitions is held only by the root partition.
                var (_, privileges, _, _) = System.Runtime.Intrinsics.X86.X86Base.CpuId(0x40000003, 0);
                bool root = (privileges & CREATE_PARTITIONS) != 0;
                yield return root ? new Metric("Virtualization", "Enabled") : new Metric("Virtual machine", "Yes");
                yield break;
            }
        }
        yield return new Metric("Virtualization", IsProcessorFeaturePresent(PF_VIRT_FIRMWARE_ENABLED) ? "Enabled" : "Disabled");
    }

    private static (double? MaxMhz, double? CurrentMhz) WindowsPowerInformation()
    {
        const int PROCESSOR_INFORMATION = 11;
        int count = Math.Max(1, Environment.ProcessorCount);
        var buffer = new ProcessorPowerInformation[count];
        int size = Marshal.SizeOf<ProcessorPowerInformation>() * count;
        if (CallNtPowerInformation(PROCESSOR_INFORMATION, 0, 0, buffer, size) != 0) return (null, null);
        return (buffer[0].MaxMhz, buffer.Average(entry => (double)entry.CurrentMhz));
    }

    private static (int Sockets, int Cores, Dictionary<int, long> Caches) WindowsTopology()
    {
        const int RELATION_ALL = 0xFFFF;
        const int RELATION_PROCESSOR_CORE = 0;
        const int RELATION_CACHE = 2;
        const int RELATION_PROCESSOR_PACKAGE = 3;

        int sockets = 0;
        int cores = 0;
        var caches = new Dictionary<int, long>();
        int length = 0;
        GetLogicalProcessorInformationEx(RELATION_ALL, 0, ref length);
        if (length <= 0) return (1, Environment.ProcessorCount, caches);

        nint buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetLogicalProcessorInformationEx(RELATION_ALL, buffer, ref length)) return (1, Environment.ProcessorCount, caches);
            int offset = 0;
            while (offset < length)
            {
                int relationship = Marshal.ReadInt32(buffer, offset);
                int size = Marshal.ReadInt32(buffer, offset + 4);
                switch (relationship)
                {
                    case RELATION_PROCESSOR_CORE:
                        cores++;
                        break;
                    case RELATION_PROCESSOR_PACKAGE:
                        sockets++;
                        break;
                    case RELATION_CACHE:
                        // CACHE_RELATIONSHIP: Level (byte), Associativity (byte), LineSize (ushort), CacheSize (uint).
                        int level = Marshal.ReadByte(buffer, offset + 8);
                        long cacheSize = (uint)Marshal.ReadInt32(buffer, offset + 12);
                        caches[level] = caches.GetValueOrDefault(level) + cacheSize;
                        break;
                }
                if (size <= 0) break;
                offset += size;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return (Math.Max(1, sockets), Math.Max(1, cores), caches);
    }

    private static (string, double?, IReadOnlyList<Metric>) ReadLinux()
    {
        var processors = ReadLinuxCpuInfo();
        var first = processors.FirstOrDefault() ?? [];
        string name = first.GetValueOrDefault("model name")
            ?? first.GetValueOrDefault("Model")
            ?? File.ReadAllText("/proc/device-tree/model").TrimEnd('\0');

        int sockets = processors.Select(entry => entry.GetValueOrDefault("physical id")).Where(id => id != null).Distinct().Count();
        int cores = processors
            .Select(entry => (entry.GetValueOrDefault("physical id"), entry.GetValueOrDefault("core id")))
            .Where(pair => pair.Item2 != null)
            .Distinct()
            .Count();

        // Only intel_pstate reports the base clock; cpuinfo_max_freq is the boost ceiling, shown under its own name.
        double? baseMhz = ReadLong("/sys/devices/system/cpu/cpu0/cpufreq/base_frequency") is long baseKilohertz ? baseKilohertz / 1000.0 : null;
        double? maxMhz = ReadLong("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq") is long maxKilohertz ? maxKilohertz / 1000.0 : null;

        var flags = first.GetValueOrDefault("flags") ?? string.Empty;
        var flagSet = flags.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var properties = new List<Metric>();
        if (baseMhz is double mhz) properties.Add(new Metric("Base speed", Format.Megahertz(mhz)));
        if (maxMhz is double max) properties.Add(new Metric("Maximum speed", Format.Megahertz(max)));
        properties.Add(new Metric("Sockets", Math.Max(1, sockets).ToString(CultureInfo.CurrentCulture)));
        properties.Add(new Metric("Cores", (cores > 0 ? cores : Environment.ProcessorCount).ToString(CultureInfo.CurrentCulture)));
        properties.Add(new Metric("Logical processors", Environment.ProcessorCount.ToString(CultureInfo.CurrentCulture)));
        if (flags.Length > 0)
        {
            properties.Add(new Metric("Virtualization", flagSet.Contains("vmx") ? "VT-x" : flagSet.Contains("svm") ? "AMD-V" : "Not supported"));
            if (flagSet.Contains("hypervisor")) properties.Add(new Metric("Virtual machine", "Yes"));
        }
        AddCaches(properties, LinuxCaches());
        return (name, baseMhz, properties);
    }

    /// <summary>Each distinct cache counted once, summed per level, as Task Manager totals them.</summary>
    private static Dictionary<int, long> LinuxCaches()
    {
        var seen = new HashSet<(int Level, string Type, string Shared)>();
        var caches = new Dictionary<int, long>();
        foreach (var cpu in Directory.EnumerateDirectories("/sys/devices/system/cpu", "cpu*"))
        {
            var cacheRoot = Path.Combine(cpu, "cache");
            if (!Directory.Exists(cacheRoot)) continue;
            foreach (var index in Directory.EnumerateDirectories(cacheRoot, "index*"))
            {
                if (ReadLong(Path.Combine(index, "level")) is not long level) continue;
                string type = ReadText(Path.Combine(index, "type")) ?? string.Empty;
                string shared = ReadText(Path.Combine(index, "shared_cpu_list")) ?? cpu;
                if (!seen.Add(((int)level, type, shared))) continue;
                string size = ReadText(Path.Combine(index, "size")) ?? "0K";
                long bytes = size.EndsWith('K') ? long.Parse(size[..^1], CultureInfo.InvariantCulture) * 1024
                    : size.EndsWith('M') ? long.Parse(size[..^1], CultureInfo.InvariantCulture) * 1024 * 1024
                    : long.Parse(size, CultureInfo.InvariantCulture);
                caches[(int)level] = caches.GetValueOrDefault((int)level) + bytes;
            }
        }
        return caches;
    }

    private static (string, double?, IReadOnlyList<Metric>) ReadMac()
    {
        string name = MacNative.SysctlString("machdep.cpu.brand_string") ?? "CPU";
        double? baseMhz = MacNative.SysctlInt64("hw.cpufrequency") is long hertz ? hertz / 1_000_000.0 : null;
        var properties = new List<Metric>();
        if (baseMhz is double mhz) properties.Add(new Metric("Base speed", Format.Megahertz(mhz)));
        if (MacNative.SysctlInt64("hw.packages") is long packages) properties.Add(new Metric("Sockets", packages.ToString(CultureInfo.CurrentCulture)));
        if (MacNative.SysctlInt64("hw.physicalcpu") is long physical) properties.Add(new Metric("Cores", physical.ToString(CultureInfo.CurrentCulture)));

        // Apple silicon splits the cores into performance levels, each with its own caches.
        int levels = (int)(MacNative.SysctlInt64("hw.nperflevels") ?? 0);
        for (int level = 0; level < levels; level++)
        {
            string label = MacNative.SysctlString($"hw.perflevel{level}.name") ?? $"Level {level}";
            if (MacNative.SysctlInt64($"hw.perflevel{level}.physicalcpu") is long count)
                properties.Add(new Metric($"{label} cores", count.ToString(CultureInfo.CurrentCulture)));
        }

        properties.Add(new Metric("Logical processors", Environment.ProcessorCount.ToString(CultureInfo.CurrentCulture)));
        if (MacNative.SysctlInt64("kern.hv_support") is long hypervisor)
            properties.Add(new Metric("Virtualization", hypervisor != 0 ? "Supported" : "Not supported"));

        var caches = new Dictionary<int, long>();
        if (levels > 0)
        {
            for (int level = 0; level < levels; level++)
            {
                long cores = MacNative.SysctlInt64($"hw.perflevel{level}.physicalcpu") ?? 0;
                long perL2 = MacNative.SysctlInt64($"hw.perflevel{level}.cpusperl2") ?? cores;
                caches[1] = caches.GetValueOrDefault(1) + cores * ((MacNative.SysctlInt64($"hw.perflevel{level}.l1dcachesize") ?? 0) + (MacNative.SysctlInt64($"hw.perflevel{level}.l1icachesize") ?? 0));
                if (MacNative.SysctlInt64($"hw.perflevel{level}.l2cachesize") is long l2 && perL2 > 0)
                    caches[2] = caches.GetValueOrDefault(2) + l2 * Math.Max(1, cores / perL2);
            }
        }
        else
        {
            long cores = MacNative.SysctlInt64("hw.physicalcpu") ?? 1;
            caches[1] = cores * ((MacNative.SysctlInt64("hw.l1dcachesize") ?? 0) + (MacNative.SysctlInt64("hw.l1icachesize") ?? 0));
            if (MacNative.SysctlInt64("hw.l2cachesize") is long l2) caches[2] = l2 * cores;
            if (MacNative.SysctlInt64("hw.l3cachesize") is long l3) caches[3] = l3;
        }
        AddCaches(properties, caches);
        return (name, baseMhz, properties);
    }

    private static void AddCaches(List<Metric> properties, Dictionary<int, long> caches)
    {
        foreach (var (level, bytes) in caches.OrderBy(pair => pair.Key))
        {
            if (bytes > 0) properties.Add(new Metric($"L{level} cache", Format.Bytes(bytes)));
        }
    }

    internal static List<Dictionary<string, string>> ReadLinuxCpuInfo()
    {
        var processors = new List<Dictionary<string, string>>();
        var current = new Dictionary<string, string>();
        foreach (var line in File.ReadLines("/proc/cpuinfo"))
        {
            if (line.Length == 0)
            {
                if (current.Count > 0) processors.Add(current);
                current = [];
                continue;
            }
            int colon = line.IndexOf(':');
            if (colon > 0) current[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        if (current.Count > 0) processors.Add(current);
        return processors;
    }

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch { return null; }
    }

    private static long? ReadLong(string path)
        => long.TryParse(ReadText(path), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("powrprof")]
    private static extern uint CallNtPowerInformation(int level, nint inputBuffer, int inputBufferLength, [Out] ProcessorPowerInformation[] outputBuffer, int outputBufferLength);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationshipType, nint buffer, ref int returnedLength);

    [DllImport("kernel32")]
    private static extern bool IsProcessorFeaturePresent(uint feature);
}
