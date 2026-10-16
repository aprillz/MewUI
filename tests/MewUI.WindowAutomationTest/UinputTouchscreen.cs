using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A virtual multi-touch screen made through /dev/uinput. The X server picks it up by hotplug like a real
/// panel, so contacts written here arrive as XInput2 touch sequences. Its range is the root window in
/// pixels, which the server maps one to one onto the screen.
/// </summary>
internal sealed class UinputTouchscreen
{
    private const string DEVICE_NAME = "MewUI test touchscreen";

    private const int O_WRONLY = 1;
    private const int O_NONBLOCK = 0x800;

    private const nuint UI_DEV_CREATE = 0x5501;
    private const nuint UI_DEV_SETUP = 0x405C5503;
    private const nuint UI_ABS_SETUP = 0x401C5504;
    private const nuint UI_SET_EVBIT = 0x40045564;
    private const nuint UI_SET_KEYBIT = 0x40045565;
    private const nuint UI_SET_ABSBIT = 0x40045567;
    private const nuint UI_SET_PROPBIT = 0x4004556E;

    private const ushort EV_SYN = 0x00;
    private const ushort EV_KEY = 0x01;
    private const ushort EV_ABS = 0x03;
    private const ushort SYN_REPORT = 0;
    private const ushort BTN_TOUCH = 0x14A;
    private const ushort ABS_X = 0x00;
    private const ushort ABS_Y = 0x01;
    private const ushort ABS_MT_SLOT = 0x2F;
    private const ushort ABS_MT_POSITION_X = 0x35;
    private const ushort ABS_MT_POSITION_Y = 0x36;
    private const ushort ABS_MT_TRACKING_ID = 0x39;
    private const int INPUT_PROP_DIRECT = 0x01;
    private const ushort BUS_VIRTUAL = 0x06;

    private const int INPUT_EVENT_SIZE = 24;
    private const int ABS_RESOLUTION = 10;
    private const int HOTPLUG_WAIT_MS = 5000;

    private static UinputTouchscreen? _instance;
    private static string? _failure;

    private readonly int _fd;
    private int _trackingId;

    private UinputTouchscreen(int fd) => _fd = fd;

    /// <summary>Creates the device once per process and waits until the X server lists it.</summary>
    public static bool TryGet(out UinputTouchscreen? device, out string reason)
    {
        if (_instance == null && _failure == null)
        {
            _instance = Create(out _failure);
        }

        device = _instance;
        reason = _failure ?? string.Empty;
        return device != null;
    }

    private static UinputTouchscreen? Create(out string? failure)
    {
        if (!TryReadRootSize(out int width, out int height))
        {
            failure = "the X display could not be opened";
            return null;
        }

        int fd = open("/dev/uinput", O_WRONLY | O_NONBLOCK);
        if (fd < 0)
        {
            failure = $"/dev/uinput cannot be opened for writing (errno {Marshal.GetLastPInvokeError()}); grant it with 'sudo setfacl -m u:$USER:rw /dev/uinput'";
            return null;
        }

        bool configured =
            ioctl(fd, UI_SET_EVBIT, EV_KEY) == 0
            && ioctl(fd, UI_SET_EVBIT, EV_ABS) == 0
            && ioctl(fd, UI_SET_EVBIT, EV_SYN) == 0
            && ioctl(fd, UI_SET_KEYBIT, BTN_TOUCH) == 0
            && ioctl(fd, UI_SET_ABSBIT, ABS_X) == 0
            && ioctl(fd, UI_SET_ABSBIT, ABS_Y) == 0
            && ioctl(fd, UI_SET_ABSBIT, ABS_MT_SLOT) == 0
            && ioctl(fd, UI_SET_ABSBIT, ABS_MT_POSITION_X) == 0
            && ioctl(fd, UI_SET_ABSBIT, ABS_MT_POSITION_Y) == 0
            && ioctl(fd, UI_SET_ABSBIT, ABS_MT_TRACKING_ID) == 0
            && ioctl(fd, UI_SET_PROPBIT, INPUT_PROP_DIRECT) == 0
            && SetupAbs(fd, ABS_X, 0, width - 1)
            && SetupAbs(fd, ABS_Y, 0, height - 1)
            && SetupAbs(fd, ABS_MT_SLOT, 0, 9)
            && SetupAbs(fd, ABS_MT_POSITION_X, 0, width - 1)
            && SetupAbs(fd, ABS_MT_POSITION_Y, 0, height - 1)
            && SetupAbs(fd, ABS_MT_TRACKING_ID, 0, 65535)
            && SetupDevice(fd)
            && ioctl(fd, UI_DEV_CREATE, 0) == 0;
        if (!configured)
        {
            failure = $"uinput setup failed (errno {Marshal.GetLastPInvokeError()})";
            close(fd);
            return null;
        }

        var watch = Stopwatch.StartNew();
        while (!IsListedByServer())
        {
            if (watch.ElapsedMilliseconds > HOTPLUG_WAIT_MS)
            {
                failure = "the X server did not pick up the virtual touchscreen";
                close(fd);
                return null;
            }

            Thread.Sleep(100);
        }

        // The server opens the device a moment after listing it; contacts written before then are lost.
        Thread.Sleep(300);
        failure = null;
        return new UinputTouchscreen(fd);
    }

    public void Down(int x, int y)
    {
        _trackingId = (_trackingId + 1) & 0xFFFF;
        Write(
            (EV_ABS, ABS_MT_SLOT, 0),
            (EV_ABS, ABS_MT_TRACKING_ID, _trackingId),
            (EV_ABS, ABS_MT_POSITION_X, x),
            (EV_ABS, ABS_MT_POSITION_Y, y),
            (EV_KEY, BTN_TOUCH, 1),
            (EV_ABS, ABS_X, x),
            (EV_ABS, ABS_Y, y),
            (EV_SYN, SYN_REPORT, 0));
    }

    public void Move(int x, int y)
        => Write(
            (EV_ABS, ABS_MT_SLOT, 0),
            (EV_ABS, ABS_MT_POSITION_X, x),
            (EV_ABS, ABS_MT_POSITION_Y, y),
            (EV_ABS, ABS_X, x),
            (EV_ABS, ABS_Y, y),
            (EV_SYN, SYN_REPORT, 0));

    public void Up()
        => Write(
            (EV_ABS, ABS_MT_SLOT, 0),
            (EV_ABS, ABS_MT_TRACKING_ID, -1),
            (EV_KEY, BTN_TOUCH, 0),
            (EV_SYN, SYN_REPORT, 0));

    private void Write(params (ushort Type, ushort Code, int Value)[] events)
    {
        // The kernel stamps the time of each event; the timeval part stays zero.
        var buffer = new byte[events.Length * INPUT_EVENT_SIZE];
        for (int index = 0; index < events.Length; index++)
        {
            var slice = buffer.AsSpan(index * INPUT_EVENT_SIZE + 16);
            BinaryPrimitives.WriteUInt16LittleEndian(slice, events[index].Type);
            BinaryPrimitives.WriteUInt16LittleEndian(slice[2..], events[index].Code);
            BinaryPrimitives.WriteInt32LittleEndian(slice[4..], events[index].Value);
        }

        nint written = write(_fd, buffer, buffer.Length);
        if (written != buffer.Length)
        {
            Assert.Fail($"uinput write failed (errno {Marshal.GetLastPInvokeError()}).");
        }
    }

    private static bool SetupAbs(int fd, ushort code, int minimum, int maximum)
    {
        // struct uinput_abs_setup: u16 code, padding, then input_absinfo (value, min, max, fuzz, flat, resolution).
        var setup = new byte[28];
        var span = setup.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span, code);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], minimum);
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], maximum);
        if (code is ABS_X or ABS_Y or ABS_MT_POSITION_X or ABS_MT_POSITION_Y)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[24..], ABS_RESOLUTION);
        }

        return ioctl(fd, UI_ABS_SETUP, setup) == 0;
    }

    private static bool SetupDevice(int fd)
    {
        // struct uinput_setup: input_id (bustype, vendor, product, version), char name[80], u32 ff_effects_max.
        var setup = new byte[92];
        var span = setup.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span, BUS_VIRTUAL);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 0x1234);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 0x5678);
        Encoding.ASCII.GetBytes(DEVICE_NAME, span.Slice(8, 79));
        return ioctl(fd, UI_DEV_SETUP, setup) == 0;
    }

    private static bool IsListedByServer()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("xinput", "list --name-only")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            });
            if (process == null)
            {
                return false;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Contains(DEVICE_NAME, StringComparison.Ordinal);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool TryReadRootSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        nint display = XOpenDisplay(0);
        if (display == 0)
        {
            return false;
        }

        int screen = XDefaultScreen(display);
        width = XDisplayWidth(display, screen);
        height = XDisplayHeight(display, screen);
        XCloseDisplay(display);
        return width > 0 && height > 0;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern nint write(int fd, byte[] buffer, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, nint argument);

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, byte[] argument);

    [DllImport("libX11.so.6")]
    private static extern nint XOpenDisplay(nint name);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(nint display);

    [DllImport("libX11.so.6")]
    private static extern int XDisplayWidth(nint display, int screen);

    [DllImport("libX11.so.6")]
    private static extern int XDisplayHeight(nint display, int screen);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(nint display);
}
