using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering.Framebuffer;

namespace Aprillz.MewUI.Platform.Framebuffer;

internal sealed unsafe partial class EvdevTouchDevice : IDisposable
{
    private const short PollIn = 0x0001;
    private const int OpenReadOnlyNonBlocking = 0x800;
    private const ushort EvSyn = 0x00;
    private const ushort EvKey = 0x01;
    private const ushort EvAbs = 0x03;

    private const ushort SynReport = 0x00;
    private const ushort BtnTouch = 0x014a;

    private const ushort AbsX = 0x00;
    private const ushort AbsY = 0x01;
    private const ushort AbsMtSlot = 0x2f;
    private const ushort AbsMtPositionX = 0x35;
    private const ushort AbsMtPositionY = 0x36;
    private const ushort AbsMtTrackingId = 0x39;

    private readonly int _fd;
    private bool _disposed;

    private EvdevTouchDevice(
        int fd,
        string path,
        string name,
        AxisRange xRange,
        AxisRange yRange,
        bool usesMultitouch)
    {
        _fd = fd;
        Path = path;
        Name = name;
        XRange = xRange;
        YRange = yRange;
        UsesMultitouch = usesMultitouch;
    }

    public string Path { get; }

    public string Name { get; }

    public AxisRange XRange { get; }

    public AxisRange YRange { get; }

    public bool UsesMultitouch { get; }

    public static EvdevTouchDevice? TryOpen(FramebufferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.TouchDevicePath))
        {
            return OpenIfTouch(options.TouchDevicePath);
        }

        if (!Directory.Exists("/dev/input"))
        {
            return null;
        }

        var candidates = Directory.EnumerateFiles("/dev/input", "event*")
            .Select(path => new { Path = path, Name = ReadDeviceName(path) })
            .OrderByDescending(x => x.Name.Contains("touch", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();

        foreach (var candidate in candidates)
        {
            var device = OpenIfTouch(candidate.Path, candidate.Name);
            if (device is not null)
            {
                return device;
            }
        }

        return null;
    }

    public int Poll(int timeoutMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var fds = new PollFd
        {
            fd = _fd,
            events = PollIn,
        };

        int result;
        do
        {
            result = EvdevNative.poll(&fds, 1, timeoutMs);
        }
        while (result < 0 && Marshal.GetLastPInvokeError() == 4); // EINTR

        return result;
    }

    public int Read(Span<InputEvent> events)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (events.IsEmpty)
        {
            return 0;
        }

        nint bytes;
        fixed (InputEvent* eventsPtr = events)
        {
            do
            {
                bytes = EvdevNative.read(_fd, eventsPtr, (nuint)(events.Length * sizeof(InputEvent)));
            }
            while (bytes < 0 && Marshal.GetLastPInvokeError() == 4); // EINTR
        }

        if (bytes <= 0)
        {
            return 0;
        }

        int count = (int)bytes / sizeof(InputEvent);
        if (count <= 0)
        {
            return 0;
        }

        return count;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = EvdevNative.close(_fd);
    }

    private static EvdevTouchDevice? OpenIfTouch(string path, string? name = null)
    {
        int fd = EvdevNative.open(path, OpenReadOnlyNonBlocking);
        if (fd < 0)
        {
            return null;
        }

        try
        {
            bool hasMtX = TryGetAbsInfo(fd, AbsMtPositionX, out var mtX);
            bool hasMtY = TryGetAbsInfo(fd, AbsMtPositionY, out var mtY);
            if (hasMtX && hasMtY)
            {
                return new EvdevTouchDevice(fd, path, name ?? ReadDeviceName(path), AxisRange.From(mtX), AxisRange.From(mtY), usesMultitouch: true);
            }

            bool hasX = TryGetAbsInfo(fd, AbsX, out var x);
            bool hasY = TryGetAbsInfo(fd, AbsY, out var y);
            if (hasX && hasY)
            {
                return new EvdevTouchDevice(fd, path, name ?? ReadDeviceName(path), AxisRange.From(x), AxisRange.From(y), usesMultitouch: false);
            }
        }
        catch
        {
            // Fall through and close the descriptor below.
        }

        _ = EvdevNative.close(fd);
        return null;
    }

    private static bool TryGetAbsInfo(int fd, ushort axis, out InputAbsInfo info)
    {
        info = default;
        fixed (InputAbsInfo* infoPtr = &info)
        {
            return EvdevNative.ioctl(fd, EvIoCgAbs(axis), infoPtr) == 0 && info.maximum > info.minimum;
        }
    }

    private static uint EvIoCgAbs(ushort axis)
        => IoCtlRead((byte)'E', (byte)(0x40 + axis), sizeof(InputAbsInfo));

    private static uint IoCtlRead(byte type, byte number, int size)
    {
        const uint IocRead = 2;
        return (IocRead << 30) | ((uint)size << 16) | ((uint)type << 8) | number;
    }

    private static string ReadDeviceName(string path)
    {
        try
        {
            var eventName = System.IO.Path.GetFileName(path);
            var sysfsPath = System.IO.Path.Combine("/sys/class/input", eventName, "device/name");
            return File.Exists(sysfsPath) ? File.ReadAllText(sysfsPath).Trim() : eventName;
        }
        catch
        {
            return System.IO.Path.GetFileName(path);
        }
    }

    internal readonly record struct AxisRange(int Minimum, int Maximum)
    {
        public static AxisRange From(InputAbsInfo info) => new(info.minimum, info.maximum);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InputEvent
    {
        public nint time_sec;
        public nint time_usec;
        public ushort type;
        public ushort code;
        public int value;

        public bool IsSynReport => type == EvSyn && code == SynReport;
        public bool IsKey => type == EvKey;
        public bool IsAbsolute => type == EvAbs;
        public bool IsTouchButton => IsKey && code == BtnTouch;
        public bool IsAbsX => IsAbsolute && code == AbsX;
        public bool IsAbsY => IsAbsolute && code == AbsY;
        public bool IsMtSlot => IsAbsolute && code == AbsMtSlot;
        public bool IsMtX => IsAbsolute && code == AbsMtPositionX;
        public bool IsMtY => IsAbsolute && code == AbsMtPositionY;
        public bool IsMtTrackingId => IsAbsolute && code == AbsMtTrackingId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct InputAbsInfo
    {
        public int value;
        public int minimum;
        public int maximum;
        public int fuzz;
        public int flat;
        public int resolution;
    }

    private static partial class EvdevNative
    {
        [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
        public static partial int open(string pathname, int flags);

        [LibraryImport("libc", SetLastError = true)]
        public static partial nint read(int fd, void* buffer, nuint count);

        [LibraryImport("libc", SetLastError = true)]
        public static partial int ioctl(int fd, uint request, void* arg);

        [LibraryImport("libc", SetLastError = true)]
        public static partial int poll(PollFd* fds, nuint nfds, int timeoutMs);

        [LibraryImport("libc", SetLastError = true)]
        public static partial int close(int fd);
    }
}
