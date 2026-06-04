using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

using Aprillz.MewUI.Rendering.Framebuffer;

namespace Aprillz.MewUI.Platform.Framebuffer;

internal sealed unsafe partial class TslibTouchDevice : IDisposable
{
    private const short PollIn = 0x0001;
    private readonly TslibHandle _handle;
    private readonly int _fd;
    private bool _disposed;

    private TslibTouchDevice(TslibHandle handle, int fd, string path)
    {
        _handle = handle;
        _fd = fd;
        Path = path;
    }

    public string Path { get; }

    public static TslibTouchDevice? TryOpen(FramebufferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ConfigureEnvironment(options.TouchDevicePath);

        string? path = options.TouchDevicePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = FindDefaultDevice();
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        nint nativeHandle;
        try
        {
            nativeHandle = TslibNative.ts_open(path, nonblock: 1);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }

        var handle = new TslibHandle(nativeHandle);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        if (TslibNative.ts_config(handle) != 0)
        {
            handle.Dispose();
            return null;
        }

        int fd = TslibNative.ts_fd(handle);
        if (fd < 0)
        {
            handle.Dispose();
            return null;
        }

        return new TslibTouchDevice(handle, fd, path);
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
            result = TslibNative.poll(&fds, 1, timeoutMs);
        }
        while (result < 0 && Marshal.GetLastPInvokeError() == 4); // EINTR

        return result;
    }

    public int Read(Span<TouchSample> samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (samples.IsEmpty)
        {
            return 0;
        }

        fixed (TouchSample* samplesPtr = samples)
        {
            int count = TslibNative.ts_read(_handle, samplesPtr, samples.Length);
            return count > 0 ? count : 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
    }

    private static void ConfigureEnvironment(string? touchDevicePath)
    {
        if (!string.IsNullOrWhiteSpace(touchDevicePath))
        {
            SetEnvironment("TSLIB_TSDEVICE", touchDevicePath, overwrite: true);
        }

        EnsureEnvironment("TSLIB_CONFFILE", "/etc/ts.conf");
        EnsureEnvironment("TSLIB_PLUGINDIR", "/usr/lib/ts");
    }

    private static void EnsureEnvironment(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
        {
            SetEnvironment(name, value, overwrite: false);
        }
    }

    private static void SetEnvironment(string name, string value, bool overwrite)
    {
        Environment.SetEnvironmentVariable(name, value);
        _ = TslibNative.setenv(name, value, overwrite ? 1 : 0);
    }

    private static string? FindDefaultDevice()
    {
        if (!Directory.Exists("/dev/input"))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles("/dev/input", "event*").OrderBy(static x => x, StringComparer.Ordinal))
        {
            var name = ReadDeviceName(path);
            if (name.Contains("touch", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("goodix", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("gt9", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return Directory.EnumerateFiles("/dev/input", "event*").OrderBy(static x => x, StringComparer.Ordinal).FirstOrDefault();
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

    [StructLayout(LayoutKind.Sequential)]
    public struct TouchSample
    {
        public int x;
        public int y;
        public uint pressure;
        public nint tv_sec;
        public nint tv_usec;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    private sealed partial class TslibHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private TslibHandle() : base(ownsHandle: true)
        {
        }

        public TslibHandle(nint handle) : base(ownsHandle: true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
            => TslibNative.ts_close(handle) == 0;
    }

    private static partial class TslibNative
    {
        [LibraryImport("ts", StringMarshalling = StringMarshalling.Utf8)]
        public static partial nint ts_open(string devName, int nonblock);

        [LibraryImport("ts")]
        public static partial int ts_config(TslibHandle ts);

        [LibraryImport("ts")]
        public static partial int ts_fd(TslibHandle ts);

        [LibraryImport("ts")]
        public static partial int ts_read(TslibHandle ts, TouchSample* samples, int nr);

        [LibraryImport("ts")]
        public static partial int ts_close(nint ts);

        [LibraryImport("libc", SetLastError = true)]
        public static partial int poll(PollFd* fds, nuint nfds, int timeoutMs);

        [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
        public static partial int setenv(string name, string value, int overwrite);
    }
}
