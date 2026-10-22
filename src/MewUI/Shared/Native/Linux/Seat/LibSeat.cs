using System.Runtime.InteropServices;
using System.Text;

namespace Aprillz.MewUI.Native;

// libseat: seat and device access through logind or seatd. Loaded on demand, since many devices lack it.
internal static unsafe class LibSeat
{
    private const string LIBRARY_NAME = "libseat.so.1";

    public const int LOG_LEVEL_SILENT = 0;
    public const int LOG_LEVEL_ERROR = 1;
    public const int LOG_LEVEL_INFO = 2;
    public const int LOG_LEVEL_DEBUG = 3;

    private static readonly object _gate = new();
    private static bool _probed;
    private static nint _library;
    private static delegate* unmanaged<LibSeatListener*, nint, nint> _openSeat;
    private static delegate* unmanaged<nint, int> _disableSeat;
    private static delegate* unmanaged<nint, int> _closeSeat;
    private static delegate* unmanaged<nint, byte*> _seatName;
    private static delegate* unmanaged<nint, byte*, int*, int> _openDevice;
    private static delegate* unmanaged<nint, int, int> _closeDevice;
    private static delegate* unmanaged<nint, int, int> _switchSession;
    private static delegate* unmanaged<nint, int> _getFd;
    private static delegate* unmanaged<nint, int, int> _dispatch;
    private static delegate* unmanaged<int, void> _setLogLevel;

    /// <summary>True when libseat is installed and exports every entry point used here.</summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureLoaded();
            return _library != 0;
        }
    }

    /// <summary>Opens the seat of the calling session; 0 with errno when no seat manager accepts the process.</summary>
    public static nint OpenSeat(LibSeatListener* listener, nint userData) => _openSeat(listener, userData);

    /// <summary>Confirms that the devices are no longer used after the disable callback.</summary>
    public static int DisableSeat(nint seat) => _disableSeat(seat);

    public static int CloseSeat(nint seat) => _closeSeat(seat);

    public static string? SeatName(nint seat) => Marshal.PtrToStringUTF8((nint)_seatName(seat));

    /// <summary>Opens a DRM or evdev node through the seat manager; returns the device id, or -1 with errno.</summary>
    public static int OpenDevice(nint seat, string path, out int fd)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(path + "\0");
        int descriptor = -1;
        int id;
        fixed (byte* pathPointer = bytes)
        {
            id = _openDevice(seat, pathPointer, &descriptor);
        }

        fd = descriptor;
        return id;
    }

    public static int CloseDevice(nint seat, int deviceId) => _closeDevice(seat, deviceId);

    public static int SwitchSession(nint seat, int session) => _switchSession(seat, session);

    public static int GetFd(nint seat) => _getFd(seat);

    public static int Dispatch(nint seat, int timeoutMs) => _dispatch(seat, timeoutMs);

    public static void SetLogLevel(int level) => _setLogLevel(level);

    private static void EnsureLoaded()
    {
        if (Volatile.Read(ref _probed))
        {
            return;
        }

        lock (_gate)
        {
            if (_probed)
            {
                return;
            }

            if (NativeLibrary.TryLoad(LIBRARY_NAME, out nint library))
            {
                bool complete =
                    TryExport(library, "libseat_open_seat", out nint openSeat)
                    & TryExport(library, "libseat_disable_seat", out nint disableSeat)
                    & TryExport(library, "libseat_close_seat", out nint closeSeat)
                    & TryExport(library, "libseat_seat_name", out nint seatName)
                    & TryExport(library, "libseat_open_device", out nint openDevice)
                    & TryExport(library, "libseat_close_device", out nint closeDevice)
                    & TryExport(library, "libseat_switch_session", out nint switchSession)
                    & TryExport(library, "libseat_get_fd", out nint getFd)
                    & TryExport(library, "libseat_dispatch", out nint dispatch)
                    & TryExport(library, "libseat_set_log_level", out nint setLogLevel);
                if (complete)
                {
                    _openSeat = (delegate* unmanaged<LibSeatListener*, nint, nint>)openSeat;
                    _disableSeat = (delegate* unmanaged<nint, int>)disableSeat;
                    _closeSeat = (delegate* unmanaged<nint, int>)closeSeat;
                    _seatName = (delegate* unmanaged<nint, byte*>)seatName;
                    _openDevice = (delegate* unmanaged<nint, byte*, int*, int>)openDevice;
                    _closeDevice = (delegate* unmanaged<nint, int, int>)closeDevice;
                    _switchSession = (delegate* unmanaged<nint, int, int>)switchSession;
                    _getFd = (delegate* unmanaged<nint, int>)getFd;
                    _dispatch = (delegate* unmanaged<nint, int, int>)dispatch;
                    _setLogLevel = (delegate* unmanaged<int, void>)setLogLevel;
                    _library = library;
                }
            }

            Volatile.Write(ref _probed, true);
        }
    }

    private static bool TryExport(nint library, string name, out nint address)
        => NativeLibrary.TryGetExport(library, name, out address);
}

/// <summary>Callbacks libseat raises from <see cref="LibSeat.Dispatch"/> (and the first enable from <see cref="LibSeat.OpenSeat"/>).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct LibSeatListener
{
    public delegate* unmanaged<nint, nint, void> EnableSeat;
    public delegate* unmanaged<nint, nint, void> DisableSeat;
}
