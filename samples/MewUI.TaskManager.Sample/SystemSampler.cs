using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Aprillz.MewUI.TaskManager.Sample;

internal sealed record ProcessSample(
    int ProcessId,
    int ParentProcessId,
    long StartTimeTicks,
    string Name,
    string? ExecutablePath,
    double CpuPercent,
    long WorkingSetBytes,
    bool IsAccessible);

internal sealed record PerformanceSample(
    double CpuPercent,
    IReadOnlyList<double> LogicalProcessorPercents,
    double KernelPercent,
    IReadOnlyList<double> LogicalProcessorKernelPercents,
    double MemoryPercent,
    long UsedMemoryBytes,
    long TotalMemoryBytes,
    int ProcessCount,
    int ThreadCount,
    TimeSpan Uptime);

internal sealed class SystemSampler
{
    private readonly Dictionary<(int Id, long Start), (TimeSpan Cpu, long Timestamp)> _previous = [];
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);
    private readonly PlatformCpuReader _cpuReader = new();

    public IReadOnlyList<ProcessSample> CaptureProcesses()
    {
        var parentIds = ParentProcessReader.Read();
        var now = Stopwatch.GetTimestamp();
        var nextKeys = new HashSet<(int Id, long Start)>();
        var result = new List<ProcessSample>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                int id;
                try { id = process.Id; }
                catch { continue; }

                string name;
                try { name = process.ProcessName; }
                catch { name = $"Process {id.ToString(CultureInfo.InvariantCulture)}"; }

                if (ProcessMetricsReader.TryRead(
                    process,
                    out long start,
                    out var cpu,
                    out long workingSet,
                    out string? executablePath))
                {
                    var key = (id, start);
                    double percent = 0;

                    if (_previous.TryGetValue(key, out var previous))
                    {
                        double elapsed = (now - previous.Timestamp) / (double)Stopwatch.Frequency;
                        if (elapsed > 0)
                        {
                            percent = Math.Clamp(
                                (cpu - previous.Cpu).TotalSeconds / elapsed / _processorCount * 100,
                                0,
                                100);
                        }
                    }

                    _previous[key] = (cpu, now);
                    nextKeys.Add(key);
                    result.Add(new ProcessSample(
                        id,
                        parentIds.GetValueOrDefault(id),
                        start,
                        name,
                        executablePath,
                        percent,
                        workingSet,
                        true));
                }
                else
                {
                    result.Add(new ProcessSample(
                        id,
                        parentIds.GetValueOrDefault(id),
                        0,
                        name,
                        executablePath,
                        0,
                        0,
                        false));
                }
            }
        }

        foreach (var key in _previous.Keys.Where(key => !nextKeys.Contains(key)).ToArray())
        {
            _previous.Remove(key);
        }

        return result;
    }

    public PerformanceSample CapturePerformance(IReadOnlyList<ProcessSample> processes)
    {
        var cpu = _cpuReader.Read();
        var (used, total) = PlatformMemoryReader.Read();
        int threads = 0;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { threads += process.Threads.Count; }
                catch { }
            }
        }

        return new PerformanceSample(
            cpu.TotalPercent,
            cpu.LogicalProcessorPercents,
            cpu.KernelPercent,
            cpu.LogicalProcessorKernelPercents,
            Math.Clamp(used * 100.0 / total, 0, 100),
            used,
            total,
            processes.Count,
            threads,
            TimeSpan.FromMilliseconds(Environment.TickCount64));
    }
}

internal static class ProcessMetricsReader
{
    public static bool TryRead(
        Process process,
        out long start,
        out TimeSpan cpu,
        out long workingSet,
        out string? executablePath)
    {
        if (OperatingSystem.IsWindows())
            return TryReadWindows(process.Id, out start, out cpu, out workingSet, out executablePath);

        executablePath = TryReadUnixExecutablePath(process.Id);

        try
        {
            start = process.StartTime.ToUniversalTime().Ticks;
            cpu = process.TotalProcessorTime;
            workingSet = process.WorkingSet64;
            return true;
        }
        catch
        {
            start = 0;
            cpu = TimeSpan.Zero;
            workingSet = 0;
            return false;
        }
    }

    private static bool TryReadWindows(
        int processId,
        out long start,
        out TimeSpan cpu,
        out long workingSet,
        out string? executablePath)
    {
        const uint QueryLimitedInformation = 0x1000;
        const uint VirtualMemoryRead = 0x0010;
        nint handle = OpenProcess(QueryLimitedInformation | VirtualMemoryRead, false, processId);
        if (handle == 0) handle = OpenProcess(QueryLimitedInformation, false, processId);

        try
        {
            executablePath = TryReadWindowsExecutablePath(handle);
            if (handle == 0 || !GetProcessTimes(handle, out var created, out _, out var kernel, out var user))
            {
                start = 0;
                cpu = TimeSpan.Zero;
                workingSet = 0;
                return false;
            }

            start = unchecked((long)created.ToUInt64());
            cpu = TimeSpan.FromTicks(unchecked((long)(kernel.ToUInt64() + user.ToUInt64())));
            var counters = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
            bool memoryAvailable = K32GetProcessMemoryInfo(handle, ref counters, counters.Size);
            workingSet = memoryAvailable ? (long)counters.WorkingSetSize : 0;
            return memoryAvailable;
        }
        finally
        {
            if (handle != 0) CloseHandle(handle);
        }
    }

    private static string? TryReadWindowsExecutablePath(nint process)
    {
        if (process == 0) return null;
        var path = new StringBuilder(32768);
        uint length = (uint)path.Capacity;
        return QueryFullProcessImageName(process, 0, path, ref length) ? path.ToString() : null;
    }

    private static string? TryReadUnixExecutablePath(int processId)
    {
        try
        {
            if (OperatingSystem.IsLinux())
                return File.ResolveLinkTarget($"/proc/{processId}/exe", returnFinalTarget: true)?.FullName;

            if (OperatingSystem.IsMacOS())
            {
                var path = new StringBuilder(4096);
                return proc_pidpath(processId, path, (uint)path.Capacity) > 0 ? path.ToString() : null;
            }
        }
        catch { }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PageFileUsage;
        public nuint PeakPageFileUsage;
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool GetProcessTimes(nint process, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool K32GetProcessMemoryInfo(nint process, ref ProcessMemoryCounters counters, uint size);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint size);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("libproc")]
    private static extern int proc_pidpath(int processId, StringBuilder buffer, uint bufferSize);
}

internal sealed class MonitorController : IDisposable
{
    private readonly SystemSampler _sampler = new();
    private readonly DispatcherTimer _timer;
    private IDispatcher? _dispatcher;
    private bool _capturing;
    private bool _disposed;
    private bool _started;

    public MonitorController()
    {
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2));
        _timer.Tick += Capture;
    }

    public event Action<IReadOnlyList<ProcessSample>, PerformanceSample>? Updated;

    public int IntervalMilliseconds
    {
        get => (int)_timer.Interval.TotalMilliseconds;
        set => _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(250, value));
    }

    public void Start()
    {
        if (_started || _disposed) return;
        _dispatcher = Application.Current.Dispatcher
            ?? throw new InvalidOperationException("MonitorController must be started after Application.Run initializes the dispatcher.");
        _started = true;
        Capture();
        _timer.Start();
    }

    private void Capture()
    {
        if (_capturing || _disposed) return;
        _capturing = true;
        var dispatcher = _dispatcher;
        _ = Task.Run(() =>
        {
            var processes = _sampler.CaptureProcesses();
            return (Processes: processes, Performance: _sampler.CapturePerformance(processes));
        }).ContinueWith(task => dispatcher?.BeginInvoke(() =>
        {
            _capturing = false;
            if (_disposed || !task.IsCompletedSuccessfully) return;
            Updated?.Invoke(task.Result.Processes, task.Result.Performance);
        }), TaskScheduler.Default);
    }

    public void Dispose()
    {
        _disposed = true;
        _started = false;
        _timer.Dispose();
    }
}

internal static class ParentProcessReader
{
    public static Dictionary<int, int> Read()
    {
        if (OperatingSystem.IsWindows()) return ReadWindows();
        if (OperatingSystem.IsLinux()) return ReadLinux();
        if (OperatingSystem.IsMacOS()) return ReadMacOS();
        return [];
    }

    private static Dictionary<int, int> ReadLinux()
    {
        var result = new Dictionary<int, int>();
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out int pid)) continue;
            try
            {
                var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                int close = stat.LastIndexOf(')');
                if (close < 0) continue;
                var fields = stat[(close + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length > 1 && int.TryParse(fields[1], out int parent)) result[pid] = parent;
            }
            catch { }
        }
        return result;
    }

    private static Dictionary<int, int> ReadMacOS()
    {
        const int ProcPidTbsdInfo = 3;
        var result = new Dictionary<int, int>();
        try
        {
            int capacity = Math.Max(proc_listallpids(0, 0), 256) + 64;
            var pids = new int[capacity];
            var handle = GCHandle.Alloc(pids, GCHandleType.Pinned);
            try
            {
                int count = proc_listallpids(handle.AddrOfPinnedObject(), pids.Length * sizeof(int));
                for (int i = 0; i < Math.Min(count, pids.Length); i++)
                {
                    if (pids[i] <= 0) continue;
                    if (proc_pidinfo(pids[i], ProcPidTbsdInfo, 0, out var info, Marshal.SizeOf<ProcBsdInfo>()) > 0)
                        result[pids[i]] = (int)info.ParentProcessId;
                }
            }
            finally
            {
                handle.Free();
            }
        }
        catch { }

        return result.Count > 0 ? result : ReadPsFallback();
    }

    private static Dictionary<int, int> ReadPsFallback()
    {
        var result = new Dictionary<int, int>();
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/ps",
                ArgumentList = { "-axo", "pid=,ppid=" },
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            if (process == null) return result;
            while (process.StandardOutput.ReadLine() is { } line)
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 2 && int.TryParse(fields[0], out int pid) && int.TryParse(fields[1], out int parent))
                    result[pid] = parent;
            }
            process.WaitForExit(2000);
        }
        catch { }
        return result;
    }

    private static Dictionary<int, int> ReadWindows()
    {
        const uint SnapshotProcess = 0x00000002;
        var result = new Dictionary<int, int>();
        nint snapshot = CreateToolhelp32Snapshot(SnapshotProcess, 0);
        if (snapshot == -1) return result;

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry)) return result;
            do
            {
                result[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct ProcBsdInfo
    {
        public uint Flags;
        public uint Status;
        public uint ExitStatus;
        public uint ProcessId;
        public uint ParentProcessId;
        public uint UserId;
        public uint GroupId;
        public uint RealUserId;
        public uint RealGroupId;
        public uint SavedUserId;
        public uint SavedGroupId;
        public uint Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Command;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string Name;
        public uint OpenFileCount;
        public uint ProcessGroupId;
        public uint JobControlCount;
        public uint ControllingTerminalDevice;
        public uint ControllingTerminalProcessGroup;
        public int Nice;
        public ulong StartSeconds;
        public ulong StartMicroseconds;
    }

    [DllImport("libproc")]
    private static extern int proc_listallpids(nint buffer, int bufferSize);

    [DllImport("libproc")]
    private static extern int proc_pidinfo(int processId, int flavor, ulong argument, out ProcBsdInfo buffer, int bufferSize);
}

internal sealed class PlatformCpuReader
{
    private CpuTimes? _previous;
    private CpuTimes[]? _previousLogical;

    public CpuSample Read()
    {
        var (current, logical) = ReadTimes();
        if (current is null) return new CpuSample(0, [], 0, []);
        var previous = _previous;
        _previous = current;
        double totalPercent = previous is null ? 0 : Percent(previous.Value, current.Value);
        double kernelPercent = previous is null ? 0 : KernelPercent(previous.Value, current.Value);

        var logicalPercents = new double[logical.Length];
        var logicalKernelPercents = new double[logical.Length];
        if (_previousLogical is { } previousLogical)
        {
            for (int i = 0; i < Math.Min(previousLogical.Length, logical.Length); i++)
                logicalPercents[i] = Percent(previousLogical[i], logical[i]);
            for (int i = 0; i < Math.Min(previousLogical.Length, logical.Length); i++)
                logicalKernelPercents[i] = KernelPercent(previousLogical[i], logical[i]);
        }
        _previousLogical = logical;
        return new CpuSample(totalPercent, logicalPercents, kernelPercent, logicalKernelPercents);
    }

    private static double Percent(CpuTimes previous, CpuTimes current)
    {
        ulong total = current.Total - previous.Total;
        ulong idle = current.Idle - previous.Idle;
        return total == 0 ? 0 : Math.Clamp((total - idle) * 100.0 / total, 0, 100);
    }

    private static double KernelPercent(CpuTimes previous, CpuTimes current)
    {
        ulong total = current.Total - previous.Total;
        ulong kernel = current.Kernel - previous.Kernel;
        return total == 0 ? 0 : Math.Clamp(kernel * 100.0 / total, 0, 100);
    }

    private static (CpuTimes? Total, CpuTimes[] Logical) ReadTimes()
    {
        if (OperatingSystem.IsWindows())
        {
            var logical = ReadWindowsLogicalTimes();
            if (GetSystemTimes(out var idle, out var kernel, out var user))
                return (new CpuTimes(
                    kernel.ToUInt64() + user.ToUInt64(),
                    idle.ToUInt64(),
                    kernel.ToUInt64() - idle.ToUInt64()), logical);
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var times = File.ReadLines("/proc/stat")
                    .TakeWhile(line => line.StartsWith("cpu", StringComparison.Ordinal))
                    .Select(ParseLinuxTimes)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
                    .ToArray();
                return times.Length == 0 ? (null, []) : (times[0], times[1..]);
            }
            catch { return (null, []); }
        }

        if (OperatingSystem.IsMacOS())
        {
            var logical = ReadMacLogicalTimes();
            if (logical.Length > 0)
                return (new CpuTimes(
                    logical.Aggregate(0UL, (sum, value) => sum + value.Total),
                    logical.Aggregate(0UL, (sum, value) => sum + value.Idle),
                    logical.Aggregate(0UL, (sum, value) => sum + value.Kernel)), logical);
        }

        return (null, []);
    }

    private static CpuTimes? ParseLinuxTimes(string line)
    {
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 5) return null;
        var values = fields.Skip(1).Select(ulong.Parse).ToArray();
        return new CpuTimes(
            values.Aggregate(0UL, (sum, value) => sum + value),
            values.ElementAtOrDefault(3) + values.ElementAtOrDefault(4),
            values.ElementAtOrDefault(2) + values.ElementAtOrDefault(5) + values.ElementAtOrDefault(6));
    }

    private static CpuTimes[] ReadWindowsLogicalTimes()
    {
        const int SystemProcessorPerformanceInformation = 8;
        int size = Marshal.SizeOf<SystemProcessorPerformanceInfo>();
        int capacity = Math.Max(1, Environment.ProcessorCount) * size;
        nint buffer = Marshal.AllocHGlobal(capacity);
        try
        {
            int status = NtQuerySystemInformation(SystemProcessorPerformanceInformation, buffer, capacity, out int needed);
            if (status != 0 && needed > capacity)
            {
                Marshal.FreeHGlobal(buffer);
                capacity = needed;
                buffer = Marshal.AllocHGlobal(capacity);
                status = NtQuerySystemInformation(SystemProcessorPerformanceInformation, buffer, capacity, out needed);
            }
            if (status != 0) return [];

            int count = needed > 0 ? needed / size : capacity / size;
            var result = new CpuTimes[count];
            for (int i = 0; i < count; i++)
            {
                var value = Marshal.PtrToStructure<SystemProcessorPerformanceInfo>(buffer + i * size);
                result[i] = new CpuTimes(
                    unchecked((ulong)(value.KernelTime + value.UserTime)),
                    unchecked((ulong)value.IdleTime),
                    unchecked((ulong)(value.KernelTime - value.IdleTime)));
            }
            return result;
        }
        catch { return []; }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static CpuTimes[] ReadMacLogicalTimes()
    {
        const int ProcessorCpuLoadInfo = 2;
        nint info = 0;
        uint allocatedCount = 0;
        try
        {
            if (host_processor_info(mach_host_self(), ProcessorCpuLoadInfo, out uint processorCount, out info, out uint infoCount) != 0 || info == 0)
                return [];
            allocatedCount = infoCount;

            var ticks = new int[checked((int)infoCount)];
            Marshal.Copy(info, ticks, 0, ticks.Length);
            var result = new CpuTimes[processorCount];
            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * 4;
                ulong user = unchecked((uint)ticks[offset]);
                ulong system = unchecked((uint)ticks[offset + 1]);
                ulong idle = unchecked((uint)ticks[offset + 2]);
                ulong nice = unchecked((uint)ticks[offset + 3]);
                result[i] = new CpuTimes(user + system + idle + nice, idle, system);
            }
            return result;
        }
        catch { return []; }
        finally
        {
            if (info != 0) vm_deallocate(mach_task_self(), info, (nuint)allocatedCount * sizeof(int));
        }
    }

    internal readonly record struct CpuSample(
        double TotalPercent,
        IReadOnlyList<double> LogicalProcessorPercents,
        double KernelPercent,
        IReadOnlyList<double> LogicalProcessorKernelPercents);

    private readonly record struct CpuTimes(ulong Total, ulong Idle, ulong Kernel);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemProcessorPerformanceInfo
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [DllImport("kernel32")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("ntdll")]
    private static extern int NtQuerySystemInformation(int informationClass, nint information, int informationLength, out int returnLength);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint mach_host_self();

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int host_processor_info(uint host, int flavor, out uint processorCount, out nint processorInfo, out uint processorInfoCount);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint mach_task_self();

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int vm_deallocate(uint targetTask, nint address, nuint size);
}

internal static class PlatformMemoryReader
{
    public static (long Used, long Total) Read()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
                return Normalize((long)(status.TotalPhysical - status.AvailablePhysical), (long)status.TotalPhysical);
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var values = File.ReadLines("/proc/meminfo")
                    .Select(line => line.Split(':', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0], parts => long.Parse(parts[1].Trim().Split(' ')[0]) * 1024);
                long total = values.GetValueOrDefault("MemTotal");
                long available = values.GetValueOrDefault("MemAvailable");
                return Normalize(total - available, total);
            }
            catch { }
        }

        if (OperatingSystem.IsMacOS())
        {
            nuint length = sizeof(ulong);
            if (sysctlbyname("hw.memsize", out ulong total, ref length, 0, 0) == 0)
            {
                uint count = 38;
                if (host_page_size(mach_host_self(), out uint pageSize) == 0 &&
                    host_statistics64(mach_host_self(), 4, out var stats, ref count) == 0)
                {
                    ulong available = ((ulong)stats.Free + stats.Inactive + stats.Speculative) * pageSize;
                    return Normalize((long)(total - Math.Min(total, available)), (long)total);
                }
                return Normalize(0, (long)total);
            }
        }

        long fallback = Math.Max(1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        return Normalize(GC.GetTotalMemory(false), fallback);
    }

    private static (long Used, long Total) Normalize(long used, long total)
    {
        total = Math.Max(1, total);
        return (Math.Clamp(used, 0, total), total);
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

    [DllImport("kernel32")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int sysctlbyname(string name, out ulong oldValue, ref nuint oldLength, nint newValue, nuint newLength);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern uint mach_host_self();

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int host_page_size(uint host, out uint pageSize);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int host_statistics64(uint host, int flavor, out VmStatistics64 statistics, ref uint count);
}
