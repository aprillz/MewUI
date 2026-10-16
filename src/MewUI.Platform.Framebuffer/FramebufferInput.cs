using System.Runtime.InteropServices;

using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>Receives the input the framebuffer host reads from libinput, in screen pixels.</summary>
internal interface IFramebufferInputSink
{
    void OnPointerMotion(double deltaX, double deltaY);

    void OnPointerPosition(double x, double y);

    void OnPointerButton(uint evdevButton, bool pressed);

    /// <summary>Wheel or finger scroll in notches; positive Y scrolls up and positive X scrolls left.</summary>
    void OnScroll(double notchesX, double notchesY);

    void OnKey(uint evdevCode, bool pressed);

    /// <summary>A touch contact in screen pixels; <paramref name="timestamp"/> is in Stopwatch ticks.</summary>
    void OnTouch(int slot, TouchPhase phase, double x, double y, long timestamp);
}

internal enum TouchPhase
{
    Down,
    Move,
    Up,
    Cancel,
}

/// <summary>Reads keyboards, pointers and touch screens of one seat through libinput and udev.</summary>
internal sealed unsafe class FramebufferInput : IDisposable
{
    private static readonly EnvDebugLogger Logger = new("MEWUI_FB_DEBUG", "[FB][Input]");

    private const int EACCES = 13;

    // Devices opened and devices refused for lack of permission, counted for the startup report.
    private static int _openedDevices;
    private static int _deniedDevices;

    // libinput reports wheel clicks in 120ths of a notch and finger scrolling in pointer-motion units.
    private const double V120_PER_NOTCH = 120.0;
    private const double FINGER_UNITS_PER_NOTCH = 15.0;

    private readonly nint _udev;
    private readonly nint _libinput;
    private readonly LibInputInterface* _interface;
    private bool _disposed;

    private FramebufferInput(nint udev, nint libinput, LibInputInterface* inputInterface)
    {
        _udev = udev;
        _libinput = libinput;
        _interface = inputInterface;
        Fd = LibInput.libinput_get_fd(libinput);
    }

    public int Fd { get; }

    /// <summary>
    /// Describes a seat whose devices were all refused, which happens when the user is in neither the input group
    /// nor an active session that grants device access; null otherwise.
    /// </summary>
    public static string? DescribePermissionProblem()
        => _openedDevices == 0 && _deniedDevices > 0
            ? $"input: permission denied on all {_deniedDevices} devices in /dev/input; add the user to the 'input' group (or grant an ACL) and log in again"
            : null;

    public static FramebufferInput? TryOpen(string seat, out string failure)
    {
        nint udev;
        try
        {
            udev = LibInput.udev_new();
        }
        catch (DllNotFoundException ex)
        {
            failure = $"input: {ex.Message}";
            return null;
        }

        if (udev == 0)
        {
            failure = "input: udev_new failed";
            return null;
        }

        // libinput keeps the interface pointer for its whole life, so it lives in native memory.
        var inputInterface = (LibInputInterface*)NativeMemory.Alloc((nuint)sizeof(LibInputInterface));
        inputInterface->OpenRestricted = &OpenRestricted;
        inputInterface->CloseRestricted = &CloseRestricted;

        nint libinput;
        try
        {
            libinput = LibInput.libinput_udev_create_context(inputInterface, 0, udev);
        }
        catch (DllNotFoundException ex)
        {
            NativeMemory.Free(inputInterface);
            LibInput.udev_unref(udev);
            failure = $"input: {ex.Message}";
            return null;
        }

        if (libinput == 0 || LibInput.libinput_udev_assign_seat(libinput, seat) != 0)
        {
            if (libinput != 0)
            {
                LibInput.libinput_unref(libinput);
            }

            NativeMemory.Free(inputInterface);
            LibInput.udev_unref(udev);
            failure = $"input: libinput could not take seat '{seat}'";
            return null;
        }

        failure = string.Empty;
        return new FramebufferInput(udev, libinput, inputInterface);
    }

    [UnmanagedCallersOnly]
    private static int OpenRestricted(byte* path, int flags, nint userData)
    {
        int fd = LibC.Open(path, flags | LibC.O_CLOEXEC);
        if (fd >= 0)
        {
            _openedDevices++;
            return fd;
        }

        int errno = Marshal.GetLastPInvokeError();
        if (errno == EACCES)
        {
            _deniedDevices++;
        }

        Logger.Write($"open {Marshal.PtrToStringUTF8((nint)path)} failed (errno {errno})");
        return -errno;
    }

    [UnmanagedCallersOnly]
    private static void CloseRestricted(int fd, nint userData) => LibC.Close(fd);

    /// <summary>Reads every event libinput has queued and hands it to <paramref name="sink"/>.</summary>
    public void Dispatch(IFramebufferInputSink sink, int screenWidth, int screenHeight)
    {
        if (_disposed)
        {
            return;
        }

        LibInput.libinput_dispatch(_libinput);
        nint inputEvent;
        while ((inputEvent = LibInput.libinput_get_event(_libinput)) != 0)
        {
            try
            {
                Route(sink, inputEvent, (uint)Math.Max(1, screenWidth), (uint)Math.Max(1, screenHeight));
            }
            finally
            {
                LibInput.libinput_event_destroy(inputEvent);
            }
        }
    }

    private static void Route(IFramebufferInputSink sink, nint inputEvent, uint width, uint height)
    {
        int type = LibInput.libinput_event_get_type(inputEvent);
        switch (type)
        {
            case LibInput.EVENT_KEYBOARD_KEY:
            {
                nint keyboard = LibInput.libinput_event_get_keyboard_event(inputEvent);
                sink.OnKey(LibInput.libinput_event_keyboard_get_key(keyboard),
                    LibInput.libinput_event_keyboard_get_key_state(keyboard) == LibInput.KEY_STATE_PRESSED);
                break;
            }

            case LibInput.EVENT_POINTER_MOTION:
            {
                nint pointer = LibInput.libinput_event_get_pointer_event(inputEvent);
                sink.OnPointerMotion(LibInput.libinput_event_pointer_get_dx(pointer), LibInput.libinput_event_pointer_get_dy(pointer));
                break;
            }

            case LibInput.EVENT_POINTER_MOTION_ABSOLUTE:
            {
                nint pointer = LibInput.libinput_event_get_pointer_event(inputEvent);
                sink.OnPointerPosition(
                    LibInput.libinput_event_pointer_get_absolute_x_transformed(pointer, width),
                    LibInput.libinput_event_pointer_get_absolute_y_transformed(pointer, height));
                break;
            }

            case LibInput.EVENT_POINTER_BUTTON:
            {
                nint pointer = LibInput.libinput_event_get_pointer_event(inputEvent);
                sink.OnPointerButton(LibInput.libinput_event_pointer_get_button(pointer),
                    LibInput.libinput_event_pointer_get_button_state(pointer) == LibInput.BUTTON_STATE_PRESSED);
                break;
            }

            case LibInput.EVENT_POINTER_SCROLL_WHEEL:
            {
                nint pointer = LibInput.libinput_event_get_pointer_event(inputEvent);
                double vertical = AxisValue(pointer, LibInput.AXIS_SCROLL_VERTICAL, wheel: true);
                double horizontal = AxisValue(pointer, LibInput.AXIS_SCROLL_HORIZONTAL, wheel: true);
                sink.OnScroll(-horizontal / V120_PER_NOTCH, -vertical / V120_PER_NOTCH);
                break;
            }

            case LibInput.EVENT_POINTER_SCROLL_FINGER:
            case LibInput.EVENT_POINTER_SCROLL_CONTINUOUS:
            {
                nint pointer = LibInput.libinput_event_get_pointer_event(inputEvent);
                double vertical = AxisValue(pointer, LibInput.AXIS_SCROLL_VERTICAL, wheel: false);
                double horizontal = AxisValue(pointer, LibInput.AXIS_SCROLL_HORIZONTAL, wheel: false);
                sink.OnScroll(-horizontal / FINGER_UNITS_PER_NOTCH, -vertical / FINGER_UNITS_PER_NOTCH);
                break;
            }

            case LibInput.EVENT_TOUCH_DOWN:
            case LibInput.EVENT_TOUCH_MOTION:
            {
                nint touch = LibInput.libinput_event_get_touch_event(inputEvent);
                sink.OnTouch(LibInput.libinput_event_touch_get_seat_slot(touch),
                    type == LibInput.EVENT_TOUCH_DOWN ? TouchPhase.Down : TouchPhase.Move,
                    LibInput.libinput_event_touch_get_x_transformed(touch, width),
                    LibInput.libinput_event_touch_get_y_transformed(touch, height),
                    TouchTimestamp(touch));
                break;
            }

            case LibInput.EVENT_TOUCH_UP:
            {
                nint touch = LibInput.libinput_event_get_touch_event(inputEvent);
                sink.OnTouch(LibInput.libinput_event_touch_get_seat_slot(touch), TouchPhase.Up, double.NaN, double.NaN,
                    TouchTimestamp(touch));
                break;
            }

            case LibInput.EVENT_TOUCH_CANCEL:
            {
                nint touch = LibInput.libinput_event_get_touch_event(inputEvent);
                sink.OnTouch(LibInput.libinput_event_touch_get_seat_slot(touch), TouchPhase.Cancel, double.NaN, double.NaN,
                    TouchTimestamp(touch));
                break;
            }
        }
    }

    /// <summary>When the touch sample was taken, in Stopwatch ticks.</summary>
    private static long TouchTimestamp(nint touch)
    {
        // libinput stamps events with CLOCK_MONOTONIC, the clock Stopwatch reads on Linux, so only the unit changes.
        ulong microseconds = LibInput.libinput_event_touch_get_time_usec(touch);
        return (long)(microseconds * (System.Diagnostics.Stopwatch.Frequency / 1_000_000.0));
    }

    private static double AxisValue(nint pointer, int axis, bool wheel)
    {
        if (LibInput.libinput_event_pointer_has_axis(pointer, axis) == 0)
        {
            return 0;
        }

        return wheel
            ? LibInput.libinput_event_pointer_get_scroll_value_v120(pointer, axis)
            : LibInput.libinput_event_pointer_get_scroll_value(pointer, axis);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LibInput.libinput_unref(_libinput);
        LibInput.udev_unref(_udev);
        NativeMemory.Free(_interface);
    }
}
