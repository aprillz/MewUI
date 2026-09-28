using System.Diagnostics;
using System.Globalization;

namespace Aprillz.MewUI.TaskManager.Sample;

/// <summary>
/// Physical disks and how busy they are. Each platform names disks its own way: Windows by number with
/// the drive letters on them, Linux by block device with its mount points, macOS by BSD name.
/// </summary>
internal sealed class DiskReader
{
    // Counters of the previous sample, per disk, for the rates.
    private readonly Dictionary<string, (DiskCounters Counters, long Timestamp)> _previous = [];

    private readonly record struct DiskCounters(
        long BytesRead,
        long BytesWritten,
        long Reads,
        long Writes,
        // Time the disk spent serving reads and writes, in milliseconds.
        double ServiceMilliseconds,
        // Time the disk had at least one request, in milliseconds, where the platform keeps it.
        double? BusyMilliseconds);

    private sealed record DiskInfo(
        string Id,
        string Title,
        string Model,
        string Type,
        long CapacityBytes,
        IReadOnlyList<Metric> Properties,
        DiskCounters Counters);

    public IReadOnlyList<ResourceSample> Read()
    {
        IReadOnlyList<DiskInfo> disks;
        try
        {
            disks = OperatingSystem.IsWindows() ? ReadWindows()
                : OperatingSystem.IsLinux() ? ReadLinux()
                : OperatingSystem.IsMacOS() ? ReadMac()
                : [];
        }
        catch
        {
            disks = [];
        }

        long now = Stopwatch.GetTimestamp();
        var result = new List<ResourceSample>();
        foreach (var disk in disks)
        {
            double activePercent = 0, readRate = 0, writeRate = 0, responseMs = 0;
            if (_previous.TryGetValue(disk.Id, out var previous))
            {
                double seconds = (now - previous.Timestamp) / (double)Stopwatch.Frequency;
                var delta = disk.Counters;
                var before = previous.Counters;
                if (seconds > 0)
                {
                    readRate = Math.Max(0, delta.BytesRead - before.BytesRead) / seconds;
                    writeRate = Math.Max(0, delta.BytesWritten - before.BytesWritten) / seconds;
                    long operations = delta.Reads - before.Reads + delta.Writes - before.Writes;
                    double service = delta.ServiceMilliseconds - before.ServiceMilliseconds;
                    responseMs = operations > 0 ? Math.Max(0, service) / operations : 0;
                    activePercent = delta.BusyMilliseconds is double busy && before.BusyMilliseconds is double busyBefore
                        ? Math.Clamp((busy - busyBefore) / (seconds * 1000) * 100, 0, 100)
                        // Without a busy clock, the time spent serving requests stands in for it.
                        : Math.Clamp(service / (seconds * 1000) * 100, 0, 100);
                }
            }
            _previous[disk.Id] = (disk.Counters, now);

            var metrics = new List<Metric>
            {
                new("Active time", Format.Percent(activePercent)),
                new("Average response time", $"{responseMs:0.0} ms"),
                new("Read speed", Format.ByteRate(readRate)),
                new("Write speed", Format.ByteRate(writeRate)),
            };
            var properties = new List<Metric>();
            if (disk.CapacityBytes > 0) properties.Add(new Metric("Capacity", Format.Bytes(disk.CapacityBytes)));
            properties.AddRange(disk.Properties);
            properties.Add(new Metric("Type", disk.Type));

            result.Add(new ResourceSample(
                disk.Id,
                ResourceKind.Disk,
                disk.Title,
                disk.Type,
                Format.Percent(activePercent),
                disk.Model,
                new ChartSample(OperatingSystem.IsMacOS() ? "Active time (estimated)" : "Active time", activePercent),
                new ChartSample("Disk transfer rate", readRate, writeRate, IsRate: true, PrimaryName: "Read speed", SecondaryName: "Write speed"),
                metrics,
                properties));
        }

        foreach (var id in _previous.Keys.Where(id => disks.All(disk => disk.Id != id)).ToArray()) _previous.Remove(id);
        return result;
    }

    // Windows: \\.\PhysicalDriveN, with the volumes mapped to it through their disk extents.

    private static IReadOnlyList<DiskInfo> ReadWindows()
    {
        var volumes = WindowsVolumesByDisk();
        string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var result = new List<DiskInfo>();
        var performanceBuffer = new byte[88];
        var geometryBuffer = new byte[256];
        for (int number = 0; number < 32; number++)
        {
            nint device = WindowsDeviceIo.Open($@"\\.\PhysicalDrive{number}");
            if (device == 0) continue;
            try
            {
                Span<byte> performance = performanceBuffer;
                if (!WindowsDeviceIo.Control(device, WindowsDeviceIo.IOCTL_DISK_PERFORMANCE, [], performance, out _)) continue;

                // DISK_PERFORMANCE: BytesRead, BytesWritten, ReadTime, WriteTime, IdleTime (100 ns), then
                // ReadCount, WriteCount, QueueDepth, SplitCount (uint), QueryTime (100 ns).
                long idle = BitConverter.ToInt64(performance[32..]);
                long query = BitConverter.ToInt64(performance[56..]);
                var counters = new DiskCounters(
                    BitConverter.ToInt64(performance),
                    BitConverter.ToInt64(performance[8..]),
                    BitConverter.ToUInt32(performance[40..]),
                    BitConverter.ToUInt32(performance[44..]),
                    (BitConverter.ToInt64(performance[16..]) + BitConverter.ToInt64(performance[24..])) / 10_000.0,
                    (query - idle) / 10_000.0);

                var (model, type) = WindowsDescribe(device);
                long capacity = 0;
                Span<byte> geometry = geometryBuffer;
                if (WindowsDeviceIo.Control(device, WindowsDeviceIo.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, [], geometry, out _))
                    capacity = BitConverter.ToInt64(geometry[24..]);

                var letters = volumes.GetValueOrDefault(number) ?? [];
                var properties = new List<Metric>();
                long formatted = letters.Sum(letter => SafeDriveSize(letter));
                if (formatted > 0) properties.Add(new Metric("Formatted", Format.Bytes(formatted)));
                bool system = letters.Any(letter => string.Equals(letter, systemRoot, StringComparison.OrdinalIgnoreCase));
                bool pageFile = letters.Any(letter => File.Exists(Path.Combine(letter, "pagefile.sys")));
                properties.Add(new Metric("System disk", system ? "Yes" : "No"));
                properties.Add(new Metric("Page file", pageFile ? "Yes" : "No"));

                string title = letters.Count > 0
                    ? $"Disk {number} ({string.Join(' ', letters.Select(letter => letter.TrimEnd('\\')))})"
                    : $"Disk {number}";
                result.Add(new DiskInfo($"disk:{number}", title, model, type, capacity, properties, counters));
            }
            finally
            {
                WindowsDeviceIo.Close(device);
            }
        }
        return result;
    }

    private static (string Model, string Type) WindowsDescribe(nint device)
    {
        const int STORAGE_DEVICE_PROPERTY = 0;
        const int STORAGE_DEVICE_SEEK_PENALTY_PROPERTY = 7;
        string model = "Disk";
        int busType = 0;
        bool removable = false;
        var descriptor = new byte[1024];
        if (WindowsDeviceIo.Control(device, WindowsDeviceIo.IOCTL_STORAGE_QUERY_PROPERTY, WindowsDeviceIo.PropertyQuery(STORAGE_DEVICE_PROPERTY), descriptor, out int length) && length >= 32)
        {
            // STORAGE_DEVICE_DESCRIPTOR: RemovableMedia at 10, ProductIdOffset at 16, BusType at 28.
            removable = descriptor[10] != 0;
            model = WindowsDeviceIo.AnsiAt(descriptor.AsSpan(0, length), BitConverter.ToInt32(descriptor, 16)) ?? model;
            busType = BitConverter.ToInt32(descriptor, 28);
        }

        bool? seekPenalty = null;
        var penalty = new byte[12];
        if (WindowsDeviceIo.Control(device, WindowsDeviceIo.IOCTL_STORAGE_QUERY_PROPERTY, WindowsDeviceIo.PropertyQuery(STORAGE_DEVICE_SEEK_PENALTY_PROPERTY), penalty, out int penaltyLength) && penaltyLength >= 9)
            seekPenalty = penalty[8] != 0;

        const int BUS_USB = 7;
        const int BUS_SD = 12;
        const int BUS_MMC = 13;
        const int BUS_VIRTUAL = 14;
        const int BUS_FILE_BACKED_VIRTUAL = 15;
        const int BUS_NVME = 17;
        string type = busType switch
        {
            BUS_USB or BUS_SD or BUS_MMC => "Removable",
            BUS_VIRTUAL or BUS_FILE_BACKED_VIRTUAL => "Virtual",
            BUS_NVME => "SSD (NVMe)",
            _ when removable => "Removable",
            _ => seekPenalty switch
            {
                false => "SSD",
                true => "HDD",
                null => "Unknown",
            },
        };
        return (model, type);
    }

    private static Dictionary<int, List<string>> WindowsVolumesByDisk()
    {
        var result = new Dictionary<int, List<string>>();
        var extentsBuffer = new byte[8 + 24 * 8];
        var numberBuffer = new byte[12];
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
            // A subst drive is a folder of another volume, not a mount point, and has no volume name.
            var volumeName = new char[64];
            if (!GetVolumeNameForVolumeMountPointW(drive.Name, volumeName, volumeName.Length)) continue;
            nint volume = WindowsDeviceIo.Open($@"\\.\{drive.Name.TrimEnd('\\')}");
            if (volume == 0) continue;
            try
            {
                // Only a volume a disk driver serves sits on a physical disk; a file system a driver makes up
                // (a cloud drive) can still answer the extents query with a disk it does not live on.
                const int FILE_DEVICE_DISK = 7;
                Span<byte> number = numberBuffer;
                if (!WindowsDeviceIo.Control(volume, WindowsDeviceIo.IOCTL_STORAGE_GET_DEVICE_NUMBER, [], number, out _) ||
                    BitConverter.ToInt32(number) != FILE_DEVICE_DISK)
                    continue;

                // VOLUME_DISK_EXTENTS: count, then per extent DiskNumber (uint, padded to 8), offset, length.
                Span<byte> extents = extentsBuffer;
                if (!WindowsDeviceIo.Control(volume, WindowsDeviceIo.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, [], extents, out _)) continue;
                int count = Math.Min(8, BitConverter.ToInt32(extents));
                for (int index = 0; index < count; index++)
                {
                    int disk = BitConverter.ToInt32(extents[(8 + index * 24)..]);
                    if (!result.TryGetValue(disk, out var letters)) result[disk] = letters = [];
                    if (!letters.Contains(drive.Name)) letters.Add(drive.Name);
                }
            }
            finally
            {
                WindowsDeviceIo.Close(volume);
            }
        }
        return result;
    }

    private static long SafeDriveSize(string root)
    {
        try { return new DriveInfo(root).TotalSize; }
        catch { return 0; }
    }

    // Linux: /proc/diskstats for the counters, /sys/block for what each disk is, mounts through its partitions.

    private static IReadOnlyList<DiskInfo> ReadLinux()
    {
        var stats = new Dictionary<string, string[]>();
        foreach (var line in File.ReadLines("/proc/diskstats"))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 14) stats[fields[2]] = fields;
        }

        var mounts = LinuxMounts();
        var swaps = File.Exists("/proc/swaps")
            ? File.ReadLines("/proc/swaps").Skip(1).Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]).ToList()
            : [];
        var result = new List<DiskInfo>();
        int number = 0;
        foreach (var block in Directory.EnumerateDirectories("/sys/block").OrderBy(path => path, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(block);
            // A physical disk has a device behind it; loop, RAM and mapper devices do not.
            if (!Directory.Exists(Path.Combine(block, "device")) || !stats.TryGetValue(name, out var fields)) continue;

            const long SECTOR = 512;
            var counters = new DiskCounters(
                long.Parse(fields[5], CultureInfo.InvariantCulture) * SECTOR,
                long.Parse(fields[9], CultureInfo.InvariantCulture) * SECTOR,
                long.Parse(fields[3], CultureInfo.InvariantCulture),
                long.Parse(fields[7], CultureInfo.InvariantCulture),
                long.Parse(fields[6], CultureInfo.InvariantCulture) + long.Parse(fields[10], CultureInfo.InvariantCulture),
                long.Parse(fields[12], CultureInfo.InvariantCulture));

            string model = ReadText(Path.Combine(block, "device", "model")) ?? ReadText(Path.Combine(block, "device", "name")) ?? name;
            bool rotational = ReadText(Path.Combine(block, "queue", "rotational")) == "1";
            bool removable = ReadText(Path.Combine(block, "removable")) == "1";
            string devicePath = Path.GetFullPath(Path.Combine(block, "device"));
            try { devicePath = new DirectoryInfo(Path.Combine(block, "device")).ResolveLinkTarget(true)?.FullName ?? devicePath; }
            catch { }
            bool usb = devicePath.Contains("/usb", StringComparison.Ordinal);
            string type = removable || usb ? "Removable"
                : rotational ? "HDD"
                : name.StartsWith("nvme", StringComparison.Ordinal) ? "SSD (NVMe)"
                : name.StartsWith("mmcblk", StringComparison.Ordinal) ? "eMMC"
                : name.StartsWith("vd", StringComparison.Ordinal) ? "Virtual"
                : "SSD";
            long capacity = (long.TryParse(ReadText(Path.Combine(block, "size")), out long sectors) ? sectors : 0) * 512;

            // The disk's own name and those of its partitions, and of anything stacked on them (LVM, LUKS).
            var devices = new HashSet<string>(StringComparer.Ordinal) { name };
            foreach (var partition in Directory.EnumerateDirectories(block, name + "*")) devices.Add(Path.GetFileName(partition));
            foreach (var holder in devices.ToArray().SelectMany(device => LinuxHolders(device))) devices.Add(holder);
            var mountPoints = mounts.Where(mount => devices.Contains(mount.Device)).Select(mount => mount.Path).Distinct().ToList();
            bool swap = swaps.Any(entry => entry.StartsWith("/dev/", StringComparison.Ordinal)
                ? devices.Contains(Path.GetFileName(entry))
                : devices.Contains(LinuxMountOf(entry, mounts)));

            var properties = new List<Metric>
            {
                new("Device", "/dev/" + name),
                new("Mount points", mountPoints.Count > 0 ? string.Join(' ', mountPoints) : "None"),
                new("System disk", mountPoints.Contains("/") ? "Yes" : "No"),
                new("Swap", swap ? "Yes" : "No"),
            };
            result.Add(new DiskInfo("disk:" + name, $"Disk {number} ({name})", model, type, capacity, properties, counters));
            number++;
        }
        return result;
    }

    private static IEnumerable<string> LinuxHolders(string device)
    {
        string path = File.Exists($"/sys/class/block/{device}/dev") ? $"/sys/class/block/{device}/holders" : string.Empty;
        if (path.Length == 0 || !Directory.Exists(path)) yield break;
        foreach (var holder in Directory.EnumerateFileSystemEntries(path))
        {
            string name = Path.GetFileName(holder);
            yield return name;
            // A mapper device mounts under its /dev/mapper name.
            if (ReadText($"/sys/class/block/{name}/dm/name") is string mapped) yield return mapped;
            foreach (var nested in LinuxHolders(name)) yield return nested;
        }
    }

    /// <summary>The device of the mount a file lives on: the one with the longest matching mount point.</summary>
    private static string LinuxMountOf(string file, List<(string Device, string Path)> mounts)
    {
        var best = mounts
            .Where(mount => mount.Path == "/" || file.StartsWith(mount.Path + "/", StringComparison.Ordinal))
            .OrderByDescending(mount => mount.Path.Length)
            .FirstOrDefault();
        return best.Device ?? string.Empty;
    }

    private static List<(string Device, string Path)> LinuxMounts()
    {
        var result = new List<(string, string)>();
        foreach (var line in File.ReadLines("/proc/self/mounts"))
        {
            var fields = line.Split(' ');
            if (fields.Length < 2 || !fields[0].StartsWith("/dev/", StringComparison.Ordinal)) continue;
            string device = fields[0]["/dev/".Length..];
            if (device.StartsWith("mapper/", StringComparison.Ordinal)) device = device["mapper/".Length..];
            result.Add((device, fields[1].Replace("\\040", " ", StringComparison.Ordinal)));
        }
        return result;
    }

    // macOS: IOKit block storage drivers carry the statistics; the media under them the BSD name and size.

    private static IReadOnlyList<DiskInfo> ReadMac()
    {
        var result = new List<DiskInfo>();
        MacNative.VisitServices("IOBlockStorageDriver", (service, properties) =>
        {
            nint statistics = MacNative.Dictionary(properties, "Statistics");
            if (statistics == 0) return;
            nint media = MacNative.ChildProperties(service);
            nint device = MacNative.ParentProperties(service);
            try
            {
                string name = MacNative.String(media, "BSD Name") ?? $"disk{result.Count}";
                long capacity = MacNative.Int64(media, "Size") ?? 0;
                nint characteristics = MacNative.Dictionary(device, "Device Characteristics");
                nint protocol = MacNative.Dictionary(device, "Protocol Characteristics");
                string model = MacNative.String(characteristics, "Product Name")?.Trim() ?? name;
                string? medium = MacNative.String(characteristics, "Medium Type");
                string? interconnect = MacNative.String(protocol, "Physical Interconnect");
                string? location = MacNative.String(protocol, "Physical Interconnect Location");
                string type = location == "External" || interconnect == "USB" ? "External"
                    : medium == "Rotational" ? "HDD"
                    : interconnect == "PCI-Express" || interconnect == "Apple Fabric" ? "SSD (PCIe)"
                    : medium == "Solid State" ? "SSD"
                    : "Disk";

                // Total times are in nanoseconds.
                var counters = new DiskCounters(
                    MacNative.Int64(statistics, "Bytes (Read)") ?? 0,
                    MacNative.Int64(statistics, "Bytes (Write)") ?? 0,
                    MacNative.Int64(statistics, "Operations (Read)") ?? 0,
                    MacNative.Int64(statistics, "Operations (Write)") ?? 0,
                    ((MacNative.Int64(statistics, "Total Time (Read)") ?? 0) + (MacNative.Int64(statistics, "Total Time (Write)") ?? 0)) / 1_000_000.0,
                    null);
                var details = new List<Metric> { new("Device", "/dev/" + name) };
                if (interconnect != null) details.Add(new Metric("Interconnect", interconnect));
                result.Add(new DiskInfo("disk:" + name, $"Disk {result.Count} ({name})", model, type, capacity, details, counters));
            }
            finally
            {
                MacNative.Release(media);
                MacNative.Release(device);
            }
        });
        return result;
    }

    [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, char[] volumeName, int length);

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch { return null; }
    }
}
