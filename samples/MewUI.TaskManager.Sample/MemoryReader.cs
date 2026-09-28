using System.Globalization;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.TaskManager.Sample;

/// <summary>
/// Physical memory, in the terms each platform's own monitor uses: Windows splits it into in use,
/// modified, standby and free; Linux into used, buffers and cache, and free; macOS into app, wired and
/// compressed memory and cached files.
/// </summary>
internal sealed class MemoryReader
{
    private readonly IReadOnlyList<Metric> _hardware = ReadHardware();

    public ResourceSample Read()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows();
            if (OperatingSystem.IsLinux()) return ReadLinux();
            if (OperatingSystem.IsMacOS()) return ReadMac();
        }
        catch { }

        long total = Math.Max(1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        return Build(total, 0, [], []);
    }

    private ResourceSample Build(long total, long used, IReadOnlyList<Metric> metrics, IReadOnlyList<CompositionPart> composition)
    {
        double percent = Math.Clamp(used * 100.0 / Math.Max(1, total), 0, 100);
        return new ResourceSample(
            "memory",
            ResourceKind.Memory,
            "Memory",
            string.Empty,
            $"{Format.Gigabytes(used)}/{Format.Gigabytes(total)} GB ({percent:0}%)",
            Format.Bytes(total),
            new ChartSample("Memory usage", percent),
            null,
            metrics,
            _hardware,
            composition,
            total);
    }

    private ResourceSample ReadWindows()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref status);
        var performance = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        GetPerformanceInfo(ref performance, performance.Size);
        long page = (long)performance.PageSize;
        long total = (long)status.TotalPhysical;
        long available = (long)status.AvailablePhysical;
        long used = total - available;

        var metrics = new List<Metric>
        {
            new("In use", Format.Bytes(used)),
            new("Available", Format.Bytes(available)),
            new("Committed", $"{Format.Gigabytes((long)performance.CommitTotal * page)}/{Format.Gigabytes((long)performance.CommitLimit * page)} GB"),
            new("Cached", Format.Bytes((long)performance.SystemCache * page)),
            new("Paged pool", Format.Bytes((long)performance.KernelPaged * page)),
            new("Non-paged pool", Format.Bytes((long)performance.KernelNonpaged * page)),
        };

        // The standby and modified lists need the profile privilege, which only an elevated process holds.
        IReadOnlyList<CompositionPart> composition = WindowsMemoryLists(page) is var (modified, standby, free)
            ? [
                new CompositionPart("In use", total - modified - standby - free, 140),
                new CompositionPart("Modified", modified, 90),
                new CompositionPart("Standby", standby, 45),
                new CompositionPart("Free", free, 0),
            ]
            : [
                new CompositionPart("In use", used, 140),
                new CompositionPart("Available", available, 0),
            ];
        return Build(total, used, metrics, composition);
    }

    private static (long Modified, long Standby, long Free)? WindowsMemoryLists(long page)
    {
        const int SYSTEM_MEMORY_LIST_INFORMATION = 80;
        if (!PrivilegeService.IsElevated) return null;
        WindowsPrivileges.Enable("SeProfileSingleProcessPrivilege");
        int size = Marshal.SizeOf<MemoryListInformation>();
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQuerySystemInformation(SYSTEM_MEMORY_LIST_INFORMATION, buffer, size, out _) != 0) return null;
            var lists = Marshal.PtrToStructure<MemoryListInformation>(buffer);
            long standby = 0;
            for (int priority = 0; priority < 8; priority++) standby += (long)lists.PageCountByPriority(priority);
            return ((long)lists.ModifiedPageCount * page, standby * page, (long)(lists.FreePageCount + lists.ZeroPageCount) * page);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private ResourceSample ReadLinux()
    {
        var values = File.ReadLines("/proc/meminfo")
            .Select(line => line.Split(':', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => long.Parse(parts[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture) * 1024);
        long total = values.GetValueOrDefault("MemTotal");
        long available = values.GetValueOrDefault("MemAvailable");
        long free = values.GetValueOrDefault("MemFree");
        long buffers = values.GetValueOrDefault("Buffers");
        long cached = values.GetValueOrDefault("Cached") + values.GetValueOrDefault("SReclaimable") - values.GetValueOrDefault("Shmem");
        long used = total - available;
        long swapTotal = values.GetValueOrDefault("SwapTotal");
        long swapFree = values.GetValueOrDefault("SwapFree");

        var metrics = new List<Metric>
        {
            new("In use", Format.Bytes(used)),
            new("Available", Format.Bytes(available)),
            new("Buffers/cache", Format.Bytes(buffers + cached)),
            new("Shared", Format.Bytes(values.GetValueOrDefault("Shmem"))),
            new("Committed", $"{Format.Gigabytes(values.GetValueOrDefault("Committed_AS"))}/{Format.Gigabytes(values.GetValueOrDefault("CommitLimit"))} GB"),
            new("Swap", swapTotal > 0 ? $"{Format.Gigabytes(swapTotal - swapFree)}/{Format.Gigabytes(swapTotal)} GB" : "None"),
        };
        // The bar splits the total three ways: what is in use as above, what is free, and the rest, which
        // the kernel holds as buffers and cache it can drop.
        IReadOnlyList<CompositionPart> composition =
        [
            new CompositionPart("In use", used, 140),
            new CompositionPart("Buffers/cache", Math.Max(0, total - used - free), 45),
            new CompositionPart("Free", free, 0),
        ];
        return Build(total, used, metrics, composition);
    }

    private ResourceSample ReadMac()
    {
        const int HOST_VM_INFO64 = 4;
        long total = MacNative.SysctlInt64("hw.memsize") ?? 0;
        uint count = (uint)(Marshal.SizeOf<VmStatistics64>() / sizeof(int));
        host_page_size(mach_host_self(), out uint pageSize);
        host_statistics64(mach_host_self(), HOST_VM_INFO64, out var stats, ref count);
        long page = pageSize;

        // Activity Monitor: app memory is anonymous pages outside the purgeable ones; used adds wired and
        // compressed; cached files are the file-backed pages plus the purgeable ones.
        long app = ((long)stats.Internal - stats.Purgeable) * page;
        long wired = (long)stats.Wired * page;
        long compressed = (long)stats.CompressorPages * page;
        long cachedFiles = ((long)stats.External + stats.Purgeable) * page;
        long used = Math.Clamp(app + wired + compressed, 0, total);
        long free = Math.Max(0, total - used - cachedFiles);

        var swap = MacSwapUsage();
        var metrics = new List<Metric>
        {
            new("Memory used", Format.Bytes(used)),
            new("App memory", Format.Bytes(app)),
            new("Wired memory", Format.Bytes(wired)),
            new("Compressed", Format.Bytes(compressed)),
            new("Cached files", Format.Bytes(cachedFiles)),
            new("Swap used", swap is var (swapUsed, swapTotal) && swapTotal > 0 ? $"{Format.Bytes(swapUsed)} of {Format.Bytes(swapTotal)}" : "None"),
        };
        IReadOnlyList<CompositionPart> composition =
        [
            new CompositionPart("App memory", app, 140),
            new CompositionPart("Wired", wired, 110),
            new CompositionPart("Compressed", compressed, 80),
            new CompositionPart("Cached files", cachedFiles, 40),
            new CompositionPart("Free", free, 0),
        ];
        return Build(total, used, metrics, composition);
    }

    private static (long Used, long Total)? MacSwapUsage()
    {
        nuint length = (nuint)Marshal.SizeOf<SwapUsage>();
        return sysctlbyname("vm.swapusage", out var usage, ref length, 0, 0) == 0 ? ((long)usage.Used, (long)usage.Total) : null;
    }

    /// <summary>Installed module details from SMBIOS on Windows; the others need root to read them.</summary>
    private static IReadOnlyList<Metric> ReadHardware()
    {
        var properties = new List<Metric>();
        try
        {
            if (!OperatingSystem.IsWindows()) return properties;

            var modules = WindowsSmbios.MemoryDevices();
            var populated = modules.Where(module => module.SizeBytes > 0).ToList();
            if (populated.Count > 0)
            {
                int speed = populated.Max(module => module.ConfiguredSpeedMhz > 0 ? module.ConfiguredSpeedMhz : module.SpeedMhz);
                if (speed > 0) properties.Add(new Metric("Speed", $"{speed} MHz"));
                properties.Add(new Metric("Slots used", $"{populated.Count} of {modules.Count}"));
                properties.Add(new Metric("Form factor", populated[0].FormFactor));
            }

            if (GetPhysicallyInstalledSystemMemory(out ulong installedKilobytes))
            {
                var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                GlobalMemoryStatusEx(ref status);
                long reserved = (long)installedKilobytes * 1024 - (long)status.TotalPhysical;
                if (reserved > 0) properties.Add(new Metric("Hardware reserved", Format.Bytes(reserved)));
            }
        }
        catch { }
        return properties;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryListInformation
    {
        public nuint ZeroPageCount;
        public nuint FreePageCount;
        public nuint ModifiedPageCount;
        public nuint ModifiedNoWritePageCount;
        public nuint BadPageCount;
        public nuint PageCountByPriority0;
        public nuint PageCountByPriority1;
        public nuint PageCountByPriority2;
        public nuint PageCountByPriority3;
        public nuint PageCountByPriority4;
        public nuint PageCountByPriority5;
        public nuint PageCountByPriority6;
        public nuint PageCountByPriority7;

        public readonly nuint PageCountByPriority(int priority) => priority switch
        {
            0 => PageCountByPriority0,
            1 => PageCountByPriority1,
            2 => PageCountByPriority2,
            3 => PageCountByPriority3,
            4 => PageCountByPriority4,
            5 => PageCountByPriority5,
            6 => PageCountByPriority6,
            _ => PageCountByPriority7,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VmStatistics64
    {
        public uint Free;
        public uint Active;
        public uint Inactive;
        public uint Wired;
        public ulong ZeroFill;
        public ulong Reactivations;
        public ulong PageIns;
        public ulong PageOuts;
        public ulong Faults;
        public ulong CopyOnWriteFaults;
        public ulong Lookups;
        public ulong Hits;
        public ulong Purges;
        public uint Purgeable;
        public uint Speculative;
        public ulong Decompressions;
        public ulong Compressions;
        public ulong SwapIns;
        public ulong SwapOuts;
        public uint CompressorPages;
        public uint Throttled;
        public uint External;
        public uint Internal;
        public ulong TotalUncompressedPagesInCompressor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SwapUsage
    {
        public ulong Total;
        public ulong Available;
        public ulong Used;
        public uint PageSize;
        public int Encrypted;
    }

    [DllImport("kernel32")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32", EntryPoint = "K32GetPerformanceInfo")]
    private static extern bool GetPerformanceInfo(ref PerformanceInformation information, uint size);

    [DllImport("kernel32")]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [DllImport("ntdll")]
    private static extern int NtQuerySystemInformation(int informationClass, nint information, int informationLength, out int returnLength);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint mach_host_self();

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int host_page_size(uint host, out uint pageSize);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int host_statistics64(uint host, int flavor, out VmStatistics64 statistics, ref uint count);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int sysctlbyname(string name, out SwapUsage oldValue, ref nuint oldLength, nint newValue, nuint newLength);
}
