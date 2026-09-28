using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Aprillz.MewUI.TaskManager.Sample;

/// <summary>
/// One process at one sample. <paramref name="MemoryBytes"/> is the memory each platform's own monitor
/// shows for a process: the private working set on Windows (Task Manager), anonymous resident memory on
/// Linux (GNOME System Monitor), and the physical footprint on macOS (Activity Monitor).
/// </summary>
internal sealed record ProcessSample(
    int ProcessId,
    int ParentProcessId,
    long StartTimeTicks,
    string Name,
    string? ExecutablePath,
    double CpuPercent,
    long MemoryBytes,
    double DiskBytesPerSecond,
    bool IsAccessible);

internal sealed record PerformanceSample(
    double CpuPercent,
    IReadOnlyList<double> LogicalProcessorPercents,
    double KernelPercent,
    IReadOnlyList<double> LogicalProcessorKernelPercents,
    int ProcessCount,
    int ThreadCount,
    long? HandleCount,
    TimeSpan Uptime,
    IReadOnlyList<ResourceSample> Resources);

internal sealed class SystemSampler : IDisposable
{
    private readonly Dictionary<(int Id, long Start), (TimeSpan Cpu, long? Disk, long Timestamp)> _previous = [];
    private readonly Dictionary<(int Id, long Start), string?> _paths = [];
    private readonly int _processorCount = Math.Max(1, Environment.ProcessorCount);
    private readonly PlatformCpuReader _cpuReader = new();
    private readonly CpuInfoReader _cpuInfo = new();
    private readonly MemoryReader _memory = new();
    private readonly DiskReader _disks = new();
    private readonly NetworkReader _networks = new();
    private readonly GpuReader _gpus = new();
    private int _threadCount;
    private long? _handleCount;

    public IReadOnlyList<ProcessSample> CaptureProcesses()
    {
        var raw = OperatingSystem.IsWindows() && Environment.Is64BitProcess
            ? WindowsProcessList.Read()
            : UnixProcessList.Read();
        var now = Stopwatch.GetTimestamp();
        var nextKeys = new HashSet<(int Id, long Start)>();
        var result = new List<ProcessSample>(raw.Count);
        int threads = 0;
        long handles = 0;
        bool handlesKnown = false;

        foreach (var process in raw)
        {
            threads += process.Threads;
            if (process.Handles is int count)
            {
                handles += count;
                handlesKnown = true;
            }

            var key = (process.Id, process.StartTicks);
            if (!_paths.TryGetValue(key, out var path))
            {
                path = ExecutablePathReader.Read(process.Id);
                _paths[key] = path;
            }

            double percent = 0;
            double diskRate = 0;
            if (process.Accessible && _previous.TryGetValue(key, out var previous))
            {
                double elapsed = (now - previous.Timestamp) / (double)Stopwatch.Frequency;
                if (elapsed > 0)
                {
                    percent = Math.Clamp((process.Cpu - previous.Cpu).TotalSeconds / elapsed / _processorCount * 100, 0, 100);
                    if (process.DiskBytes is long disk && previous.Disk is long diskBefore) diskRate = Math.Max(0, disk - diskBefore) / elapsed;
                }
            }
            if (process.Accessible) _previous[key] = (process.Cpu, process.DiskBytes, now);
            nextKeys.Add(key);
            result.Add(new ProcessSample(process.Id, process.ParentId, process.StartTicks, process.Name, path, percent, process.MemoryBytes, diskRate, process.Accessible));
        }

        foreach (var key in _previous.Keys.Where(key => !nextKeys.Contains(key)).ToArray()) _previous.Remove(key);
        foreach (var key in _paths.Keys.Where(key => !nextKeys.Contains(key)).ToArray()) _paths.Remove(key);
        _threadCount = threads;
        _handleCount = handlesKnown ? handles : UnixProcessList.ReadOpenFiles();
        return result;
    }

    public PerformanceSample CapturePerformance(IReadOnlyList<ProcessSample> processes)
    {
        var cpu = _cpuReader.Read();
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        double? speed = _cpuInfo.ReadSpeedMhz();
        var metrics = new List<Metric>
        {
            new("Utilization", Format.Percent(cpu.TotalPercent)),
            new("Speed", speed is double mhz ? Format.Megahertz(mhz) : "Not available"),
            new("Processes", Format.Count(processes.Count)),
            new("Threads", Format.Count(_threadCount)),
        };
        if (_handleCount is long handles) metrics.Add(new Metric(OperatingSystem.IsWindows() ? "Handles" : "Open files", Format.Count(handles)));
        metrics.Add(new Metric("Up time", Format.Uptime(uptime)));

        var resources = new List<ResourceSample>
        {
            new(
                "cpu",
                ResourceKind.Cpu,
                "CPU",
                string.Empty,
                speed is double current ? $"{cpu.TotalPercent:0}% {Format.Megahertz(current)}" : Format.Percent(cpu.TotalPercent),
                _cpuInfo.Name,
                new ChartSample("% Utilization", cpu.TotalPercent),
                null,
                metrics,
                _cpuInfo.Properties),
            _memory.Read(),
        };
        resources.AddRange(_disks.Read());
        resources.AddRange(_networks.Read());
        resources.AddRange(_gpus.Read());

        return new PerformanceSample(
            cpu.TotalPercent,
            cpu.LogicalProcessorPercents,
            cpu.KernelPercent,
            cpu.LogicalProcessorKernelPercents,
            processes.Count,
            _threadCount,
            _handleCount,
            uptime,
            resources);
    }

    public void Dispose()
    {
        _cpuInfo.Dispose();
        _gpus.Dispose();
    }
}

/// <summary>A process as the platform lists it, before rates are worked out.</summary>
internal readonly record struct RawProcess(
    int Id,
    int ParentId,
    long StartTicks,
    string Name,
    TimeSpan Cpu,
    long MemoryBytes,
    int Threads,
    int? Handles,
    // Bytes read and written since the process started, where the platform tells.
    long? DiskBytes,
    bool Accessible);

/// <summary>
/// Every process in one system call (SystemProcessInformation), which is how Task Manager reads them.
/// It needs no handle to the process, so protected and other users' processes report their memory,
/// CPU time, threads and handles too. The layout read here is the 64-bit one.
/// </summary>
internal static class WindowsProcessList
{
    private const int SYSTEM_PROCESS_INFORMATION = 5;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    // SYSTEM_PROCESS_INFORMATION field offsets (64-bit).
    private const int NEXT_ENTRY_OFFSET = 0;
    private const int NUMBER_OF_THREADS = 4;
    private const int WORKING_SET_PRIVATE_SIZE = 8;
    private const int CREATE_TIME = 32;
    private const int USER_TIME = 40;
    private const int KERNEL_TIME = 48;
    private const int IMAGE_NAME_LENGTH = 56;
    private const int IMAGE_NAME_BUFFER = 64;
    private const int UNIQUE_PROCESS_ID = 80;
    private const int INHERITED_FROM_PROCESS_ID = 88;
    private const int HANDLE_COUNT = 96;
    private const int READ_TRANSFER_COUNT = 232;
    private const int WRITE_TRANSFER_COUNT = 240;

    private static int _bufferSize = 1 << 20;

    public static List<RawProcess> Read()
    {
        var result = new List<RawProcess>();
        nint buffer = 0;
        try
        {
            while (true)
            {
                buffer = Marshal.AllocHGlobal(_bufferSize);
                int status = NtQuerySystemInformation(SYSTEM_PROCESS_INFORMATION, buffer, _bufferSize, out int needed);
                if (status == STATUS_INFO_LENGTH_MISMATCH)
                {
                    Marshal.FreeHGlobal(buffer);
                    buffer = 0;
                    _bufferSize = Math.Max(_bufferSize * 2, needed + (64 << 10));
                    continue;
                }
                if (status != 0) return result;
                break;
            }

            int offset = 0;
            while (true)
            {
                nint entry = buffer + offset;
                int id = (int)Marshal.ReadInt64(entry, UNIQUE_PROCESS_ID);
                int nameLength = (ushort)Marshal.ReadInt16(entry, IMAGE_NAME_LENGTH);
                nint nameBuffer = Marshal.ReadIntPtr(entry, IMAGE_NAME_BUFFER);
                string name = id == 0 ? "System Idle Process"
                    : nameBuffer != 0 && nameLength > 0 ? Marshal.PtrToStringUni(nameBuffer, nameLength / 2)
                    : $"Process {id.ToString(CultureInfo.InvariantCulture)}";
                // The process list names images with their extension; the rest of the page names them without.
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

                result.Add(new RawProcess(
                    id,
                    (int)Marshal.ReadInt64(entry, INHERITED_FROM_PROCESS_ID),
                    Marshal.ReadInt64(entry, CREATE_TIME),
                    name,
                    TimeSpan.FromTicks(Marshal.ReadInt64(entry, USER_TIME) + Marshal.ReadInt64(entry, KERNEL_TIME)),
                    Marshal.ReadInt64(entry, WORKING_SET_PRIVATE_SIZE),
                    Marshal.ReadInt32(entry, NUMBER_OF_THREADS),
                    Marshal.ReadInt32(entry, HANDLE_COUNT),
                    Marshal.ReadInt64(entry, READ_TRANSFER_COUNT) + Marshal.ReadInt64(entry, WRITE_TRANSFER_COUNT),
                    true));

                int next = Marshal.ReadInt32(entry, NEXT_ENTRY_OFFSET);
                if (next == 0) break;
                offset += next;
            }
        }
        finally
        {
            if (buffer != 0) Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    [DllImport("ntdll")]
    private static extern int NtQuerySystemInformation(int informationClass, nint information, int informationLength, out int returnLength);
}

/// <summary>Processes on Linux and macOS, and on Windows when not running as a 64-bit process.</summary>
internal static class UnixProcessList
{
    public static List<RawProcess> Read()
    {
        var parents = ParentProcessReader.Read();
        var result = new List<RawProcess>();
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

                try
                {
                    long start = process.StartTime.ToUniversalTime().Ticks;
                    var cpu = process.TotalProcessorTime;
                    var (memory, threads, disk) = ReadMemoryThreadsAndDisk(process, id);
                    result.Add(new RawProcess(id, parents.GetValueOrDefault(id), start, name, cpu, memory, threads, null, disk, true));
                }
                catch
                {
                    result.Add(new RawProcess(id, parents.GetValueOrDefault(id), 0, name, TimeSpan.Zero, 0, 0, null, null, false));
                }
            }
        }
        return result;
    }

    private static (long Memory, int Threads, long? Disk) ReadMemoryThreadsAndDisk(Process process, int id)
    {
        if (OperatingSystem.IsLinux())
        {
            // GNOME System Monitor's "Memory" is resident memory not shared with files: RssAnon.
            long anonymous = 0;
            int threads = 0;
            foreach (var line in File.ReadLines($"/proc/{id}/status"))
            {
                if (line.StartsWith("RssAnon:", StringComparison.Ordinal))
                    anonymous = long.Parse(line["RssAnon:".Length..].Trim().Split(' ')[0], CultureInfo.InvariantCulture) * 1024;
                else if (line.StartsWith("Threads:", StringComparison.Ordinal))
                    threads = int.Parse(line["Threads:".Length..].Trim(), CultureInfo.InvariantCulture);
            }
            return (anonymous, threads, LinuxDiskBytes(id));
        }

        if (OperatingSystem.IsMacOS())
        {
            // Activity Monitor's "Memory" is the physical footprint (rusage_info_v2.ri_phys_footprint).
            const int RUSAGE_INFO_V2 = 2;
            const int PHYS_FOOTPRINT = 72;
            const int DISKIO_BYTES_READ = 144;
            const int DISKIO_BYTES_WRITTEN = 152;
            var buffer = new byte[512];
            bool read = proc_pid_rusage(id, RUSAGE_INFO_V2, buffer) == 0;
            long footprint = read ? BitConverter.ToInt64(buffer, PHYS_FOOTPRINT) : process.WorkingSet64;
            long? disk = read ? BitConverter.ToInt64(buffer, DISKIO_BYTES_READ) + BitConverter.ToInt64(buffer, DISKIO_BYTES_WRITTEN) : null;
            int threads;
            try { threads = process.Threads.Count; }
            catch { threads = 0; }
            return (footprint, threads, disk);
        }

        return (process.PrivateMemorySize64, process.Threads.Count, null);
    }

    /// <summary>Bytes the process made the storage layer fetch or send, from /proc/[pid]/io; unreadable for other users' processes.</summary>
    private static long? LinuxDiskBytes(int id)
    {
        try
        {
            long total = 0;
            foreach (var line in File.ReadLines($"/proc/{id}/io"))
            {
                if (line.StartsWith("read_bytes:", StringComparison.Ordinal) || line.StartsWith("write_bytes:", StringComparison.Ordinal))
                    total += long.Parse(line[(line.IndexOf(':') + 1)..].Trim(), CultureInfo.InvariantCulture);
            }
            return total;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Open file handles system-wide: the kernel's file table on Linux, kern.num_files on macOS.</summary>
    public static long? ReadOpenFiles()
    {
        try
        {
            if (OperatingSystem.IsLinux())
                return long.Parse(File.ReadAllText("/proc/sys/fs/file-nr").Split('\t', ' ')[0], CultureInfo.InvariantCulture);
            if (OperatingSystem.IsMacOS()) return MacNative.SysctlInt64("kern.num_files");
        }
        catch { }
        return null;
    }

    [DllImport("libproc")]
    private static extern int proc_pid_rusage(int processId, int flavor, byte[] buffer);
}

internal static class ExecutablePathReader
{
    public static string? Read(int processId)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return ReadWindows(processId);
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

    private static string? ReadWindows(int processId)
    {
        const uint QUERY_LIMITED_INFORMATION = 0x1000;
        nint handle = OpenProcess(QUERY_LIMITED_INFORMATION, false, processId);
        if (handle == 0) return null;
        try
        {
            var path = new StringBuilder(32768);
            uint length = (uint)path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

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
        _sampler.Dispose();
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

