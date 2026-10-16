using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

// libinput (with its udev seat backend) for keyboards, pointers and touch screens without a display server.
internal static unsafe partial class LibInput
{
    private const string LIBRARY_NAME = "libinput.so.10";
    private const string UDEV_LIBRARY_NAME = "libudev.so.1";

    public const int EVENT_DEVICE_ADDED = 1;
    public const int EVENT_DEVICE_REMOVED = 2;
    public const int EVENT_KEYBOARD_KEY = 300;
    public const int EVENT_POINTER_MOTION = 400;
    public const int EVENT_POINTER_MOTION_ABSOLUTE = 401;
    public const int EVENT_POINTER_BUTTON = 402;
    public const int EVENT_POINTER_SCROLL_WHEEL = 404;
    public const int EVENT_POINTER_SCROLL_FINGER = 405;
    public const int EVENT_POINTER_SCROLL_CONTINUOUS = 406;
    public const int EVENT_TOUCH_DOWN = 500;
    public const int EVENT_TOUCH_UP = 501;
    public const int EVENT_TOUCH_MOTION = 502;
    public const int EVENT_TOUCH_CANCEL = 503;
    public const int EVENT_TOUCH_FRAME = 504;

    public const int KEY_STATE_PRESSED = 1;
    public const int BUTTON_STATE_PRESSED = 1;
    public const int AXIS_SCROLL_VERTICAL = 0;
    public const int AXIS_SCROLL_HORIZONTAL = 1;

    [LibraryImport(UDEV_LIBRARY_NAME)]
    public static partial nint udev_new();

    [LibraryImport(UDEV_LIBRARY_NAME)]
    public static partial nint udev_unref(nint udev);

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint libinput_udev_create_context(LibInputInterface* inputInterface, nint userData, nint udev);

    [LibraryImport(LIBRARY_NAME, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int libinput_udev_assign_seat(nint libinput, string seatId);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int libinput_get_fd(nint libinput);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int libinput_dispatch(nint libinput);

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint libinput_get_event(nint libinput);

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint libinput_unref(nint libinput);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int libinput_event_get_type(nint inputEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial void libinput_event_destroy(nint inputEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint libinput_event_get_keyboard_event(nint inputEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint libinput_event_get_pointer_event(nint inputEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint libinput_event_get_touch_event(nint inputEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial uint libinput_event_keyboard_get_key(nint keyboardEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int libinput_event_keyboard_get_key_state(nint keyboardEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial double libinput_event_pointer_get_dx(nint pointerEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial double libinput_event_pointer_get_dy(nint pointerEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial double libinput_event_pointer_get_absolute_x_transformed(nint pointerEvent, uint width);

    [LibraryImport(LIBRARY_NAME)]
    public static partial double libinput_event_pointer_get_absolute_y_transformed(nint pointerEvent, uint height);

    [LibraryImport(LIBRARY_NAME)]
    public static partial uint libinput_event_pointer_get_button(nint pointerEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int libinput_event_pointer_get_button_state(nint pointerEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int libinput_event_pointer_has_axis(nint pointerEvent, int axis);

    [LibraryImport(LIBRARY_NAME)]
    public static partial double libinput_event_pointer_get_scroll_value(nint pointerEvent, int axis);

    [LibraryImport(LIBRARY_NAME)]
    public static partial double libinput_event_pointer_get_scroll_value_v120(nint pointerEvent, int axis);

    [LibraryImport(LIBRARY_NAME)]
    public static partial ulong libinput_event_pointer_get_time_usec(nint pointerEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int libinput_event_touch_get_seat_slot(nint touchEvent);

    [LibraryImport(LIBRARY_NAME)]
    public static partial double libinput_event_touch_get_x_transformed(nint touchEvent, uint width);

    [LibraryImport(LIBRARY_NAME)]
    public static partial double libinput_event_touch_get_y_transformed(nint touchEvent, uint height);

    [LibraryImport(LIBRARY_NAME)]
    public static partial ulong libinput_event_touch_get_time_usec(nint touchEvent);
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LibInputInterface
{
    public delegate* unmanaged<byte*, int, nint, int> OpenRestricted;
    public delegate* unmanaged<int, nint, void> CloseRestricted;
}
