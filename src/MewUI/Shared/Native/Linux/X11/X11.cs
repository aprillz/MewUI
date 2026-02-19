using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

// Minimal Xlib surface for future Linux/X11 host.
internal static partial class X11
{
    private const string LibraryName = "libX11.so.6";

    [LibraryImport(LibraryName)]
    public static partial nint XFree(nint data);

    [LibraryImport(LibraryName)]
    public static partial nint XOpenDisplay(nint displayName);

    [LibraryImport(LibraryName)]
    public static partial int XCloseDisplay(nint display);

    [LibraryImport(LibraryName)]
    public static partial int XDefaultScreen(nint display);

    [LibraryImport(LibraryName)]
    public static partial nint XRootWindow(nint display, int screenNumber);

    [LibraryImport(LibraryName)]
    public static partial nint XCreateColormap(nint display, nint window, nint visual, int alloc);

    [LibraryImport(LibraryName)]
    public static partial nint XCreateWindow(
        nint display,
        nint parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        int depth,
        uint @class,
        nint visual,
        ulong valuemask,
        ref XSetWindowAttributes attributes);

    [LibraryImport(LibraryName)]
    public static partial void XDestroyWindow(nint display, nint window);

    [LibraryImport(LibraryName)]
    public static partial void XMapWindow(nint display, nint window);

    [LibraryImport(LibraryName)]
    public static partial int XRaiseWindow(nint display, nint window);

    [LibraryImport(LibraryName)]
    public static partial int XMoveWindow(nint display, nint window, int x, int y);

    [LibraryImport(LibraryName)]
    public static partial int XSetInputFocus(nint display, nint focus, int revert_to, nint time);

    [LibraryImport(LibraryName)]
    public static partial int XSetTransientForHint(nint display, nint w, nint prop_window);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int XStoreName(nint display, nint window, string windowName);

    [LibraryImport(LibraryName)]
    public static partial int XPending(nint display);

    [LibraryImport(LibraryName)]
    public static partial int XNextEvent(nint display, out XEvent ev);

    // NOTE: In Xlib headers this is a macro (ConnectionNumber), but on most libX11 builds
    // there's also an exported symbol XConnectionNumber that bindings can P/Invoke.
    [LibraryImport(LibraryName)]
    public static partial int XConnectionNumber(nint display);

    [LibraryImport(LibraryName)]
    public static partial int XSelectInput(nint display, nint window, nint eventMask);

    [LibraryImport(LibraryName)]
    public static partial void XFlush(nint display);

    [LibraryImport(LibraryName)]
    public static partial int XGetWindowAttributes(nint display, nint window, out XWindowAttributes attributes);

    [LibraryImport(LibraryName)]
    public static partial int XClearArea(nint display, nint window, int x, int y, uint width, uint height, [MarshalAs(UnmanagedType.Bool)] bool exposures);

    [LibraryImport(LibraryName)]
    public static partial nint XGetSelectionOwner(nint display, nint selection);

    [LibraryImport(LibraryName)]
    public static partial int XGetWindowProperty(
        nint display,
        nint window,
        nint property,
        nint long_offset,
        nint long_length,
        [MarshalAs(UnmanagedType.Bool)] bool delete,
        nint req_type,
        out nint actual_type_return,
        out int actual_format_return,
        out nuint nitems_return,
        out nuint bytes_after_return,
        out nint prop_return);

    [LibraryImport(LibraryName)]
    public static partial void XrmInitialize();

    [LibraryImport(LibraryName)]
    public static partial nint XResourceManagerString(nint display);

    [LibraryImport(LibraryName)]
    public static partial nint XrmGetStringDatabase(nint data);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int XrmGetResource(nint database, string name, string className, out nint type, out XrmValue value);

    [LibraryImport(LibraryName)]
    public static partial void XrmDestroyDatabase(nint database);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint XInternAtom(nint display, string atomName, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [LibraryImport(LibraryName)]
    public static partial int XSetWMProtocols(nint display, nint window, ref nint protocols, int count);

    [LibraryImport(LibraryName)]
    public static partial nint XLookupKeysym(ref XKeyEvent key_event, int index);

    [LibraryImport(LibraryName)]
    public static partial int XLookupString(ref XKeyEvent event_struct, byte[] buffer_return, int bytes_buffer, out nint keysym_return, out nint status_in_out);

    [LibraryImport(LibraryName)]
    public static unsafe partial int XLookupString(ref XKeyEvent event_struct, byte* buffer_return, int bytes_buffer, out nint keysym_return, out nint status_in_out);

    // XIM / UTF-8 input (minimal; XCreateIC is varargs and is bound via dynamic delegates in platform code).
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint XSetLocaleModifiers(string modifiers);

    [LibraryImport(LibraryName)]
    public static partial nint XOpenIM(nint display, nint rdb, nint res_name, nint res_class);

    [LibraryImport(LibraryName)]
    public static partial int XCloseIM(nint im);

    [LibraryImport(LibraryName)]
    public static partial void XDestroyIC(nint ic);

    [LibraryImport(LibraryName)]
    public static partial void XSetICFocus(nint ic);

    [LibraryImport(LibraryName)]
    public static partial void XUnsetICFocus(nint ic);

    [LibraryImport(LibraryName)]
    public static partial int XFilterEvent(ref XEvent ev, nint window);

    [LibraryImport(LibraryName)]
    public static unsafe partial int Xutf8LookupString(nint ic, ref XKeyEvent event_struct, byte* buffer_return, int bytes_buffer, out nint keysym_return, out int status_return);

    [LibraryImport(LibraryName)]
    public static partial int XSetWMNormalHints(nint display, nint window, ref XSizeHints hints);

    [LibraryImport(LibraryName)]
    public static partial int XChangeProperty(
        nint display,
        nint window,
        nint property,
        nint type,
        int format,
        int mode,
        nint data,
        int nelements);

    [LibraryImport(LibraryName)]
    public static partial int XDeleteProperty(nint display, nint window, nint property);

    [LibraryImport(LibraryName)]
    public static partial int XTranslateCoordinates(
        nint display,
        nint src_w,
        nint dest_w,
        int src_x,
        int src_y,
        out int dest_x_return,
        out int dest_y_return,
        out nint child_return);
}

[StructLayout(LayoutKind.Sequential)]
internal struct XrmValue
{
    public uint size;
    public nint addr;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XVisualInfo
{
    public nint visual;
    public nint visualid;
    public int screen;
    public int depth;
    public int @class;
    public ulong red_mask;
    public ulong green_mask;
    public ulong blue_mask;
    public int colormap_size;
    public int bits_per_rgb;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XSetWindowAttributes
{
    public nint background_pixmap;
    public ulong background_pixel;
    public nint border_pixmap;
    public ulong border_pixel;
    public int bit_gravity;
    public int win_gravity;
    public int backing_store;
    public ulong backing_planes;
    public ulong backing_pixel;
    [MarshalAs(UnmanagedType.Bool)]
    public bool save_under;
    public nint event_mask;
    public nint do_not_propagate_mask;
    [MarshalAs(UnmanagedType.Bool)]
    public bool override_redirect;
    public nint colormap;
    public nint cursor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XKeyEvent
{
    public int type;
    public ulong serial;
    [MarshalAs(UnmanagedType.Bool)]
    public bool send_event;
    public nint display;
    public nint window;
    public nint root;
    public nint subwindow;
    public ulong time;
    public int x, y;
    public int x_root, y_root;
    public uint state;
    public uint keycode;
    [MarshalAs(UnmanagedType.Bool)]
    public bool same_screen;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XButtonEvent
{
    public int type;
    public ulong serial;
    [MarshalAs(UnmanagedType.Bool)]
    public bool send_event;
    public nint display;
    public nint window;
    public nint root;
    public nint subwindow;
    public ulong time;
    public int x, y;
    public int x_root, y_root;
    public uint state;
    public uint button;
    [MarshalAs(UnmanagedType.Bool)]
    public bool same_screen;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XMotionEvent
{
    public int type;
    public ulong serial;
    [MarshalAs(UnmanagedType.Bool)]
    public bool send_event;
    public nint display;
    public nint window;
    public nint root;
    public nint subwindow;
    public ulong time;
    public int x, y;
    public int x_root, y_root;
    public uint state;
    public byte is_hint;
    [MarshalAs(UnmanagedType.Bool)]
    public bool same_screen;
}

[StructLayout(LayoutKind.Explicit, Size = 192)]
internal struct XEvent
{
    [FieldOffset(0)]
    public int type;

    [FieldOffset(0)]
    public XConfigureEvent xconfigure;

    [FieldOffset(0)]
    public XExposeEvent xexpose;

    [FieldOffset(0)]
    public XClientMessageEvent xclient;

    [FieldOffset(0)]
    public XDestroyWindowEvent xdestroywindow;

    [FieldOffset(0)]
    public XKeyEvent xkey;

    [FieldOffset(0)]
    public XButtonEvent xbutton;

    [FieldOffset(0)]
    public XMotionEvent xmotion;

    [FieldOffset(0)]
    public XPropertyEvent xproperty;

    [FieldOffset(0)]
    public XFocusChangeEvent xfocus;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XFocusChangeEvent
{
    public int type;
    public ulong serial;
    [MarshalAs(UnmanagedType.Bool)]
    public bool send_event;
    public nint display;
    public nint window;
    public int mode;
    public int detail;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XPropertyEvent
{
    public int type;
    public ulong serial;
    [MarshalAs(UnmanagedType.Bool)]
    public bool send_event;
    public nint display;
    public nint window;
    public nint atom;
    public ulong time;
    public int state;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XExposeEvent
{
    public int type;
    public ulong serial;
    [MarshalAs(UnmanagedType.Bool)]
    public bool send_event;
    public nint display;
    public nint window;
    public int x;
    public int y;
    public int width;
    public int height;
    public int count;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XConfigureEvent
{
    public int type;
    public ulong serial;
    [MarshalAs(UnmanagedType.Bool)]
    public bool send_event;
    public nint display;
    public nint @event;
    public nint window;
    public int x;
    public int y;
    public int width;
    public int height;
    public int border_width;
    public nint above;
    [MarshalAs(UnmanagedType.Bool)]
    public bool override_redirect;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XSizeHints
{
    public XSizeHintsFlags flags;
    public int x, y;
    public int width, height;
    public int min_width, min_height;
    public int max_width, max_height;
    public int width_inc, height_inc;
    public int min_aspect_x, min_aspect_y;
    public int max_aspect_x, max_aspect_y;
    public int base_width, base_height;
    public int win_gravity;
}

[Flags]
internal enum XSizeHintsFlags : long
{
    None = 0L,
    PMinSize = 1L << 4,
    PMaxSize = 1L << 5,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XClientMessageEvent
{
    public int type;
    public ulong serial;
    [MarshalAs(UnmanagedType.Bool)]
    public bool send_event;
    public nint display;
    public nint window;
    public nint message_type;
    public int format;
    public fixed long data[5];
}

[StructLayout(LayoutKind.Sequential)]
internal struct XDestroyWindowEvent
{
    public int type;
    public ulong serial;
    [MarshalAs(UnmanagedType.Bool)]
    public bool send_event;
    public nint display;
    public nint @event;
    public nint window;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XWindowAttributes
{
    public int x, y;
    public int width, height;
    public int border_width;
    public int depth;
    public nint visual;
    public nint root;
    public int @class;
    public int bit_gravity;
    public int win_gravity;
    public int backing_store;
    public ulong backing_planes;
    public ulong backing_pixel;
    [MarshalAs(UnmanagedType.Bool)]
    public bool save_under;
    public nint colormap;
    [MarshalAs(UnmanagedType.Bool)]
    public bool map_installed;
    public int map_state;
    public long all_event_masks;
    public long your_event_mask;
    public long do_not_propagate_mask;
    [MarshalAs(UnmanagedType.Bool)]
    public bool override_redirect;
    public nint screen;
}
