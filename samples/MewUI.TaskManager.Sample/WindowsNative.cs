using System.Runtime.InteropServices;
using System.Text;

namespace Aprillz.MewUI.TaskManager.Sample;

/// <summary>Turns on a privilege the process token holds but does not have enabled.</summary>
internal static class WindowsPrivileges
{
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    private static readonly HashSet<string> _enabled = [];

    public static bool Enable(string privilege)
    {
        lock (_enabled)
        {
            if (_enabled.Contains(privilege)) return true;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out nint token)) return false;
            try
            {
                if (!LookupPrivilegeValue(null, privilege, out long luid)) return false;
                var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                if (!AdjustTokenPrivileges(token, false, ref state, 0, 0, 0) || Marshal.GetLastWin32Error() != 0) return false;
                _enabled.Add(privilege);
                return true;
            }
            finally
            {
                CloseHandle(token);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("kernel32")]
    private static extern nint GetCurrentProcess();

    [DllImport("advapi32", SetLastError = true)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);

    [DllImport("advapi32", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(nint token, bool disableAll, ref TokenPrivileges state, uint length, nint previous, nint returnLength);

    [DllImport("kernel32")]
    private static extern bool CloseHandle(nint handle);
}

/// <summary>Memory module records (SMBIOS type 17) from the firmware table Windows exposes to any user.</summary>
internal static class WindowsSmbios
{
    internal readonly record struct MemoryDevice(long SizeBytes, int SpeedMhz, int ConfiguredSpeedMhz, string FormFactor);

    public static List<MemoryDevice> MemoryDevices()
    {
        const uint RSMB = 0x52534D42;
        var result = new List<MemoryDevice>();
        uint size = GetSystemFirmwareTable(RSMB, 0, null, 0);
        if (size == 0) return result;
        var buffer = new byte[size];
        if (GetSystemFirmwareTable(RSMB, 0, buffer, size) != size) return result;

        // RawSMBIOSData: 8 bytes of header, then the structure table.
        int offset = 8;
        while (offset + 4 <= buffer.Length)
        {
            byte type = buffer[offset];
            byte length = buffer[offset + 1];
            if (type == 127 || length < 4) break;
            if (type == 17 && offset + length <= buffer.Length) result.Add(ParseMemoryDevice(buffer.AsSpan(offset, length)));

            // The formatted part is followed by strings, ended by a double zero.
            int next = offset + length;
            while (next + 1 < buffer.Length && (buffer[next] != 0 || buffer[next + 1] != 0)) next++;
            offset = next + 2;
        }
        return result;
    }

    private static MemoryDevice ParseMemoryDevice(ReadOnlySpan<byte> record)
    {
        long size = 0;
        if (record.Length > 0x0D)
        {
            int raw = BitConverter.ToUInt16(record[0x0C..]);
            if (raw == 0x7FFF && record.Length > 0x1F) size = (long)BitConverter.ToUInt32(record[0x1C..]) * 1024 * 1024;
            else if (raw != 0 && raw != 0xFFFF) size = (raw & 0x8000) != 0 ? (long)(raw & 0x7FFF) * 1024 : (long)raw * 1024 * 1024;
        }
        int formFactor = record.Length > 0x0E ? record[0x0E] : 0;
        int speed = record.Length > 0x16 ? BitConverter.ToUInt16(record[0x15..]) : 0;
        int configured = record.Length > 0x21 ? BitConverter.ToUInt16(record[0x20..]) : 0;
        return new MemoryDevice(size, speed, configured, formFactor switch
        {
            0x09 => "DIMM",
            0x0B => "Row of chips",
            0x0D => "SODIMM",
            0x0F => "FB-DIMM",
            0x10 => "Die",
            _ => "Other",
        });
    }

    [DllImport("kernel32")]
    private static extern uint GetSystemFirmwareTable(uint provider, uint table, byte[]? buffer, uint size);
}

/// <summary>Device I/O controls the disk reader issues; a handle opened with no access rights is enough for all of them.</summary>
internal static class WindowsDeviceIo
{
    public const uint IOCTL_DISK_PERFORMANCE = 0x00070020;
    public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
    public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;

    private const uint FILE_SHARE_READ_WRITE = 0x3;
    private const uint OPEN_EXISTING = 3;

    public static nint Open(string path)
    {
        nint handle = CreateFileW(path, 0, FILE_SHARE_READ_WRITE, 0, OPEN_EXISTING, 0, 0);
        return handle == -1 ? 0 : handle;
    }

    public static bool Control(nint device, uint code, ReadOnlySpan<byte> input, Span<byte> output, out int returned)
    {
        unsafe
        {
            fixed (byte* inputPointer = input)
            fixed (byte* outputPointer = output)
            {
                bool ok = DeviceIoControl(device, code, (nint)inputPointer, input.Length, (nint)outputPointer, output.Length, out uint bytes, 0);
                returned = (int)bytes;
                return ok;
            }
        }
    }

    /// <summary>A storage property query (STORAGE_PROPERTY_QUERY) for the device itself.</summary>
    public static byte[] PropertyQuery(int propertyId)
    {
        var query = new byte[12];
        BitConverter.TryWriteBytes(query.AsSpan(0), propertyId);
        return query;
    }

    public static string? AnsiAt(ReadOnlySpan<byte> buffer, int offset)
    {
        if (offset <= 0 || offset >= buffer.Length) return null;
        int end = buffer[offset..].IndexOf((byte)0);
        return Encoding.ASCII.GetString(buffer.Slice(offset, end < 0 ? buffer.Length - offset : end)).Trim();
    }

    public static void Close(nint handle)
    {
        if (handle != 0) CloseHandle(handle);
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateFileW(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool DeviceIoControl(nint device, uint code, nint input, int inputSize, nint output, int outputSize, out uint returned, nint overlapped);

    [DllImport("kernel32")]
    private static extern bool CloseHandle(nint handle);
}
