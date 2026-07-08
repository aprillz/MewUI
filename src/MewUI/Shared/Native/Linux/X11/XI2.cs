using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// Minimal XInput2 surface used to receive high-resolution scroll deltas from
/// trackpads and modern mice. Falls back gracefully - callers must check
/// <see cref="XIQueryVersion"/> before relying on these APIs.
/// </summary>
internal static partial class XI2
{
    private const string LibraryName = "libXi.so.6";

    // XInput2 event types - used in XGenericEventCookie.evtype.
    public const int XI_ButtonPress = 4;
    public const int XI_ButtonRelease = 5;
    public const int XI_Motion = 6;

    // Special device IDs accepted by XISelectEvents / XIQueryDevice.
    public const int XIAllDevices = 0;
    public const int XIAllMasterDevices = 1;

    // XIDeviceInfo.use values - distinguishes master/slave pointer/keyboard devices.
    public const int XIMasterPointer = 1;
    public const int XIMasterKeyboard = 2;
    public const int XISlavePointer = 3;
    public const int XISlaveKeyboard = 4;
    public const int XIFloatingSlave = 5;

    // XIAnyClassInfo.type discriminator values.
    public const int XIKeyClass = 0;
    public const int XIButtonClass = 1;
    public const int XIValuatorClass = 2;
    public const int XIScrollClass = 3;
    public const int XITouchClass = 4;

    // XIScrollClassInfo.scroll_type values.
    public const int XIScrollTypeVertical = 1;
    public const int XIScrollTypeHorizontal = 2;

    [LibraryImport(LibraryName)]
    public static partial int XIQueryVersion(nint display, ref int major, ref int minor);

    [LibraryImport(LibraryName)]
    public static partial int XISelectEvents(nint display, nint window, XIEventMask[] masks, int num_masks);

    [LibraryImport(LibraryName)]
    public static partial nint XIQueryDevice(nint display, int deviceid, out int ndevices);

    [LibraryImport(LibraryName)]
    public static partial void XIFreeDeviceInfo(nint info);
}

/// <summary>
/// XInput2 event mask used with <see cref="XI2.XISelectEvents"/>.
/// The mask bytes are referenced (not copied) for the duration of the call.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XIEventMask
{
    public int deviceid;
    public int mask_len;
    public nint mask;       // pointer to byte[] with bit per evtype
}

/// <summary>
/// Header for any XInput2 class descriptor (Key / Button / Valuator / Scroll / Touch).
/// <see cref="type"/> identifies the concrete class.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XIAnyClassInfo
{
    public int type;
    public int sourceid;
    public int _padding;    // Implementations align the trailing layout to 8 bytes on 64-bit.
}

/// <summary>
/// Scroll class info - present once per scroll axis (vertical or horizontal).
/// "increment" defines how many raw valuator units equal one wheel notch.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XIScrollClassInfo
{
    public int type;        // == XIScrollClass
    public int sourceid;
    public int number;      // valuator number this scroll axis is bound to
    public int scroll_type; // XIScrollTypeVertical / XIScrollTypeHorizontal
    public double increment;
    public int flags;
}

/// <summary>
/// Valuator class info - describes one continuous axis (X, Y, scroll value, ...).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XIValuatorClassInfo
{
    public int type;        // == XIValuatorClass
    public int sourceid;
    public int number;
    public nint label;      // Atom
    public double min;
    public double max;
    public double value;    // current raw value
    public int resolution;
    public int mode;
}

/// <summary>
/// Device info - has a pointer to an array of class info pointers, one per device class.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XIDeviceInfo
{
    public int deviceid;
    public nint name;            // const char*
    public int use;
    public int attachment;
    [MarshalAs(UnmanagedType.Bool)]
    public bool enabled;
    public int num_classes;
    public nint classes;         // XIAnyClassInfo**
}

/// <summary>
/// XInput2 device event payload (referenced by <see cref="XGenericEventCookie.data"/>).
/// Only the leading fields are needed for scroll handling.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XIDeviceEvent
{
    public int type;
    public nuint serial;
    [MarshalAs(UnmanagedType.Bool)]
    public bool send_event;
    public nint display;
    public int extension;
    public int evtype;
    public nuint time;
    public int deviceid;
    public int sourceid;
    public int detail;
    public nint root;
    public nint @event;
    public nint child;
    public double root_x;
    public double root_y;
    public double event_x;
    public double event_y;
    public int flags;
    public XIButtonState buttons;
    public XIValuatorState valuators;
    public XIModifierState mods;
    public XIGroupState group;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XIButtonState
{
    public int mask_len;
    public nint mask;       // bitmask of currently held buttons (1-indexed)
}

[StructLayout(LayoutKind.Sequential)]
internal struct XIValuatorState
{
    public int mask_len;
    public nint mask;       // bit i set ⇒ values[k] is the new value for valuator i (k = popcount of bits below i)
    public nint values;     // double*
}

[StructLayout(LayoutKind.Sequential)]
internal struct XIModifierState
{
    public int @base;
    public int latched;
    public int locked;
    public int effective;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XIGroupState
{
    public int @base;
    public int latched;
    public int locked;
    public int effective;
}
