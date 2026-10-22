using System.Runtime.InteropServices;

using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>
/// Where the host's device access comes from: a seat manager (logind or seatd, through libseat) that hands out
/// descriptors and takes them back while another virtual terminal is active, or the device nodes directly.
/// </summary>
internal interface IFramebufferSession : IDisposable
{
    string Description { get; }

    /// <summary>Descriptor to poll for seat events, or -1 when there are none.</summary>
    int EventFd { get; }

    /// <summary>True while the seat is ours; false between <see cref="Disabling"/> and <see cref="Enabled"/>.</summary>
    bool IsActive { get; }

    /// <summary>True when <see cref="TrySwitchSession"/> can change the virtual terminal.</summary>
    bool CanSwitchSession { get; }

    /// <summary>Opens a device node, returning its descriptor or the negated errno.</summary>
    int OpenDevice(string path, int flags);

    void CloseDevice(int fd);

    void DispatchEvents();

    /// <summary>Raised when the seat is being taken away; the host stops using devices and then calls <see cref="AcknowledgeDisable"/>.</summary>
    event Action? Disabling;

    /// <summary>Raised when the seat is back and the devices work again.</summary>
    event Action? Enabled;

    void AcknowledgeDisable();

    bool TrySwitchSession(int session);
}

/// <summary>Opens device nodes directly, which needs group permissions or a session ACL and no other DRM master.</summary>
internal sealed class DirectFramebufferSession : IFramebufferSession
{
    public string Description => "direct device access";

    public int EventFd => -1;

    public bool IsActive => true;

    public bool CanSwitchSession => false;

    public event Action? Disabling
    {
        add { }
        remove { }
    }

    public event Action? Enabled
    {
        add { }
        remove { }
    }

    public int OpenDevice(string path, int flags)
    {
        int fd = LibC.Open(path, flags | LibC.O_CLOEXEC);
        return fd >= 0 ? fd : -Marshal.GetLastPInvokeError();
    }

    public void CloseDevice(int fd) => LibC.Close(fd);

    public void DispatchEvents()
    {
    }

    public void AcknowledgeDisable()
    {
    }

    public bool TrySwitchSession(int session) => false;

    public void Dispose()
    {
    }
}

/// <summary>Takes devices from the seat manager through libseat, as wlroots and its compositors do.</summary>
internal sealed unsafe class SeatFramebufferSession : IFramebufferSession
{
    private static readonly EnvDebugLogger Logger = new("MEWUI_FB_DEBUG", "[FB][Seat]");

    private const int EACCES = 13;

    private readonly LibSeatListener* _listener;
    private readonly Dictionary<int, int> _deviceIds = [];
    private GCHandle _selfHandle;
    private nint _seat;
    private bool _disposed;

    private SeatFramebufferSession(LibSeatListener* listener)
    {
        _listener = listener;
    }

    public string Description { get; private set; } = string.Empty;

    public int EventFd => _seat == 0 ? -1 : LibSeat.GetFd(_seat);

    public bool IsActive { get; private set; }

    public bool CanSwitchSession => true;

    public event Action? Disabling;

    public event Action? Enabled;

    /// <summary>Opens the seat of the calling session; null with the reason when libseat is missing or no manager accepts the process.</summary>
    public static SeatFramebufferSession? TryOpen(out string failure)
    {
        if (!LibSeat.IsAvailable)
        {
            failure = "libseat.so.1 is not installed";
            return null;
        }

        LibSeat.SetLogLevel(Logger.Enabled ? LibSeat.LOG_LEVEL_INFO : LibSeat.LOG_LEVEL_ERROR);

        // libseat keeps the listener pointer for the seat's whole life, so it lives in native memory.
        var listener = (LibSeatListener*)NativeMemory.Alloc((nuint)sizeof(LibSeatListener));
        listener->EnableSeat = &OnEnableSeat;
        listener->DisableSeat = &OnDisableSeat;
        var session = new SeatFramebufferSession(listener);
        session._selfHandle = GCHandle.Alloc(session, GCHandleType.Normal);
        nint seat = LibSeat.OpenSeat(listener, GCHandle.ToIntPtr(session._selfHandle));
        if (seat == 0)
        {
            int errno = Marshal.GetLastSystemError();
            session._selfHandle.Free();
            NativeMemory.Free(listener);
            failure = $"no seat manager accepted the process (errno {errno}); it has to run inside an active session on the seat";
            return null;
        }

        session._seat = seat;
        session.Description = $"seat '{LibSeat.SeatName(seat)}' through libseat";
        failure = string.Empty;
        return session;
    }

    public int OpenDevice(string path, int flags)
    {
        int id = LibSeat.OpenDevice(_seat, path, out int fd);
        if (id < 0)
        {
            int errno = Marshal.GetLastSystemError();
            return -(errno == 0 ? EACCES : errno);
        }

        _deviceIds[fd] = id;
        return fd;
    }

    public void CloseDevice(int fd)
    {
        // libseat forgets the device; the descriptor itself is ours to close.
        if (_deviceIds.Remove(fd, out int id) && !_disposed)
        {
            LibSeat.CloseDevice(_seat, id);
        }

        LibC.Close(fd);
    }

    public void DispatchEvents()
    {
        if (!_disposed && LibSeat.Dispatch(_seat, 0) < 0)
        {
            Logger.Write($"libseat_dispatch failed (errno {Marshal.GetLastSystemError()})");
        }
    }

    public void AcknowledgeDisable()
    {
        if (!_disposed)
        {
            LibSeat.DisableSeat(_seat);
        }
    }

    public bool TrySwitchSession(int session)
    {
        if (_disposed || LibSeat.SwitchSession(_seat, session) != 0)
        {
            Logger.Write($"switch to session {session} refused (errno {Marshal.GetLastSystemError()})");
            return false;
        }

        return true;
    }

    [UnmanagedCallersOnly]
    private static void OnEnableSeat(nint seat, nint userData)
    {
        if (GCHandle.FromIntPtr(userData).Target is SeatFramebufferSession session)
        {
            session.IsActive = true;
            session.Enabled?.Invoke();
        }
    }

    [UnmanagedCallersOnly]
    private static void OnDisableSeat(nint seat, nint userData)
    {
        if (GCHandle.FromIntPtr(userData).Target is SeatFramebufferSession session)
        {
            session.IsActive = false;
            session.Disabling?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LibSeat.CloseSeat(_seat);
        _seat = 0;
        NativeMemory.Free(_listener);
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }
}
