using System.Runtime.InteropServices;

using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>
/// A DRM/KMS output: one connector driven at one mode from a GBM surface, presented with page flips.
/// The flip completes asynchronously; one more frame may wait behind it, and <see cref="CanTakeFrame"/> holds
/// the host off beyond that.
/// </summary>
internal sealed unsafe class DrmKmsOutput : IFramebufferOutput
{
    private static readonly EnvDebugLogger Logger = new("MEWUI_FB_DEBUG", "[FB][DRM]");

    private const int MAX_CARD_INDEX = 8;
    private const int CURSOR_SIZE = 64;
    private const int DISPOSE_FLIP_WAIT_MS = 500;

    private readonly int _fd;
    private readonly uint _connectorId;
    private readonly uint _crtcId;
    private readonly DrmModeModeInfo _mode;
    private readonly DrmModeCrtc _savedCrtc;
    private readonly bool _hasSavedCrtc;
    private readonly nint _gbmDevice;
    private readonly nint _gbmSurface;
    private readonly bool _cursorAllowed;
    private readonly DrmKmsWindowSurface _surface;
    private GCHandle _selfHandle;
    private nint _frontBo;
    private nint _pendingBo;
    private nint _queuedBo;
    private uint _queuedFramebufferId;
    private bool _modeSet;
    private bool _disposed;
    private nint _cursorBo;
    private bool _cursorVisible;
    private bool _cursorApplied;
    private int _cursorX;
    private int _cursorY;

    private DrmKmsOutput(
        int fd,
        string devicePath,
        string connectorName,
        uint connectorId,
        uint crtcId,
        DrmModeModeInfo mode,
        DrmModeCrtc savedCrtc,
        bool hasSavedCrtc,
        nint gbmDevice,
        nint gbmSurface,
        double dpiScale,
        bool cursorAllowed)
    {
        _fd = fd;
        _connectorId = connectorId;
        _crtcId = crtcId;
        _mode = mode;
        _savedCrtc = savedCrtc;
        _hasSavedCrtc = hasSavedCrtc;
        _gbmDevice = gbmDevice;
        _gbmSurface = gbmSurface;
        _cursorAllowed = cursorAllowed;
        _selfHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        PixelWidth = mode.HDisplay;
        PixelHeight = mode.VDisplay;
        RefreshHz = (int)mode.VRefresh;
        Description = $"DRM {devicePath} {connectorName} {PixelWidth}x{PixelHeight}@{RefreshHz}Hz crtc={crtcId}";
        _surface = new DrmKmsWindowSurface(this, dpiScale);
    }

    public string Description { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public int RefreshHz { get; }

    public bool FlipPending => _pendingBo != 0;

    public bool CanTakeFrame => _queuedBo == 0;

    public int EventFd => _fd;

    public IWindowSurface WindowSurface => _surface;

    /// <summary>Raised when a page flip completes and the screen can take the next frame.</summary>
    public event Action? FlipCompleted;

    /// <summary>
    /// Opens the first usable card (or the one in <paramref name="options"/>), returning null with the reason
    /// when no card has a connected display this process may drive.
    /// </summary>
    public static DrmKmsOutput? TryOpen(FramebufferPlatformOptions options, out string failure)
    {
        var failures = new List<string>();
        foreach (var path in EnumerateCardPaths(options.DrmDevicePath))
        {
            var output = TryOpenCard(path, options, out string reason);
            if (output != null)
            {
                failure = string.Empty;
                return output;
            }

            failures.Add($"{path}: {reason}");
        }

        failure = failures.Count == 0 ? "no DRM card found" : string.Join("; ", failures);
        return null;
    }

    private static IEnumerable<string> EnumerateCardPaths(string? requested)
    {
        if (!string.IsNullOrEmpty(requested))
        {
            yield return requested;
            yield break;
        }

        for (int index = 0; index < MAX_CARD_INDEX; index++)
        {
            string path = $"/dev/dri/card{index}";
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static DrmKmsOutput? TryOpenCard(string path, FramebufferPlatformOptions options, out string failure)
    {
        int fd = LibC.Open(path, LibC.O_RDWR | LibC.O_CLOEXEC);
        if (fd < 0)
        {
            failure = $"open failed (errno {Marshal.GetLastPInvokeError()})";
            return null;
        }

        bool keepFd = false;
        try
        {
            var resources = LibDrm.drmModeGetResources(fd);
            if (resources == null)
            {
                failure = "not a KMS device";
                return null;
            }

            try
            {
                if (!TrySelectConnector(fd, resources, options, out var selection, out failure))
                {
                    return null;
                }

                var savedCrtcPointer = LibDrm.drmModeGetCrtc(fd, selection.CrtcId);
                DrmModeCrtc savedCrtc = default;
                bool hasSavedCrtc = savedCrtcPointer != null;
                if (hasSavedCrtc)
                {
                    savedCrtc = *savedCrtcPointer;
                    LibDrm.drmModeFreeCrtc(savedCrtcPointer);
                }

                nint gbmDevice = LibGbm.gbm_create_device(fd);
                if (gbmDevice == 0)
                {
                    failure = "gbm_create_device failed";
                    return null;
                }

                nint gbmSurface = LibGbm.gbm_surface_create(gbmDevice, selection.Mode.HDisplay, selection.Mode.VDisplay,
                    LibGbm.GBM_FORMAT_XRGB8888, LibGbm.GBM_BO_USE_SCANOUT | LibGbm.GBM_BO_USE_RENDERING);
                if (gbmSurface == 0)
                {
                    LibGbm.gbm_device_destroy(gbmDevice);
                    failure = "gbm_surface_create failed";
                    return null;
                }

                keepFd = true;
                failure = string.Empty;
                return new DrmKmsOutput(fd, path, selection.ConnectorName, selection.ConnectorId, selection.CrtcId, selection.Mode,
                    savedCrtc, hasSavedCrtc, gbmDevice, gbmSurface, options.DpiScale > 0 ? options.DpiScale : 1.0, options.ShowCursor);
            }
            finally
            {
                LibDrm.drmModeFreeResources(resources);
            }
        }
        finally
        {
            if (!keepFd)
            {
                LibC.Close(fd);
            }
        }
    }

    private readonly record struct ConnectorSelection(uint ConnectorId, string ConnectorName, uint CrtcId, DrmModeModeInfo Mode);

    private static bool TrySelectConnector(int fd, DrmModeRes* resources, FramebufferPlatformOptions options,
        out ConnectorSelection selection, out string failure)
    {
        selection = default;
        var seen = new List<string>();
        for (int index = 0; index < resources->CountConnectors; index++)
        {
            var connector = LibDrm.drmModeGetConnector(fd, resources->Connectors[index]);
            if (connector == null)
            {
                continue;
            }

            try
            {
                string name = LibDrm.GetConnectorName(connector->ConnectorType, connector->ConnectorTypeId);
                bool connected = connector->Connection == LibDrm.DRM_MODE_CONNECTED && connector->CountModes > 0;
                seen.Add(connected ? name : $"{name}(disconnected)");
                if (!connected || (options.ConnectorName != null && !string.Equals(options.ConnectorName, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var modes = new FramebufferModeSelection.Mode[connector->CountModes];
                for (int modeIndex = 0; modeIndex < modes.Length; modeIndex++)
                {
                    var mode = connector->Modes[modeIndex];
                    modes[modeIndex] = new FramebufferModeSelection.Mode(mode.HDisplay, mode.VDisplay, mode.VRefresh,
                        (mode.Type & LibDrm.DRM_MODE_TYPE_PREFERRED) != 0);
                }

                int chosen = FramebufferModeSelection.SelectMode(modes, options.VideoMode);
                if (chosen < 0)
                {
                    seen[^1] = $"{name}(no {options.VideoMode!.Value.Width}x{options.VideoMode!.Value.Height} mode)";
                    continue;
                }

                uint crtcId = FindCrtc(fd, resources, connector);
                if (crtcId == 0)
                {
                    seen[^1] = $"{name}(no CRTC)";
                    continue;
                }

                selection = new ConnectorSelection(connector->ConnectorId, name, crtcId, connector->Modes[chosen]);
                failure = string.Empty;
                return true;
            }
            finally
            {
                LibDrm.drmModeFreeConnector(connector);
            }
        }

        failure = seen.Count == 0 ? "no connectors" : $"no usable connector ({string.Join(", ", seen)})";
        return false;
    }

    private static uint FindCrtc(int fd, DrmModeRes* resources, DrmModeConnector* connector)
    {
        if (connector->EncoderId != 0)
        {
            var current = LibDrm.drmModeGetEncoder(fd, connector->EncoderId);
            if (current != null)
            {
                uint crtcId = current->CrtcId;
                LibDrm.drmModeFreeEncoder(current);
                if (crtcId != 0)
                {
                    return crtcId;
                }
            }
        }

        for (int encoderIndex = 0; encoderIndex < connector->CountEncoders; encoderIndex++)
        {
            var encoder = LibDrm.drmModeGetEncoder(fd, connector->Encoders[encoderIndex]);
            if (encoder == null)
            {
                continue;
            }

            uint possible = encoder->PossibleCrtcs;
            LibDrm.drmModeFreeEncoder(encoder);
            for (int crtcIndex = 0; crtcIndex < resources->CountCrtcs; crtcIndex++)
            {
                if ((possible & (1u << crtcIndex)) != 0)
                {
                    return resources->Crtcs[crtcIndex];
                }
            }
        }

        return 0;
    }

    /// <summary>Puts the buffer the backend just swapped on screen: a mode set the first time, a page flip after.</summary>
    internal void Present()
    {
        if (_disposed)
        {
            return;
        }

        nint bo = LibGbm.gbm_surface_lock_front_buffer(_gbmSurface);
        if (bo == 0)
        {
            Logger.Write("gbm_surface_lock_front_buffer returned no buffer; frame dropped");
            return;
        }

        uint framebufferId = GetFramebufferId(bo);
        if (framebufferId == 0)
        {
            LibGbm.gbm_surface_release_buffer(_gbmSurface, bo);
            Logger.Write("drmModeAddFB failed; frame dropped");
            return;
        }

        if (!_modeSet)
        {
            uint connectorId = _connectorId;
            var mode = _mode;
            if (LibDrm.drmModeSetCrtc(_fd, _crtcId, framebufferId, 0, 0, &connectorId, 1, &mode) != 0)
            {
                int errno = Marshal.GetLastPInvokeError();
                LibGbm.gbm_surface_release_buffer(_gbmSurface, bo);
                throw new InvalidOperationException(
                    $"drmModeSetCrtc failed (errno {errno}). Another process holds the display; stop the display server (Xorg, Wayland compositor) before starting a framebuffer app.");
            }

            _modeSet = true;
            _frontBo = bo;
            ApplyCursor();
            return;
        }

        if (_pendingBo != 0)
        {
            // Legacy KMS takes one flip at a time; this frame goes up the moment the current flip lands.
            if (_queuedBo != 0)
            {
                LibGbm.gbm_surface_release_buffer(_gbmSurface, _queuedBo);
            }

            _queuedBo = bo;
            _queuedFramebufferId = framebufferId;
            return;
        }

        SubmitFlip(bo, framebufferId);
    }

    private void SubmitFlip(nint bo, uint framebufferId)
    {
        bool vsync = !Application.IsRunning || Application.Current.RenderLoopSettings.VSyncEnabled;
        nint userData = GCHandle.ToIntPtr(_selfHandle);
        int result = vsync
            ? -1
            : LibDrm.drmModePageFlip(_fd, _crtcId, framebufferId, LibDrm.DRM_MODE_PAGE_FLIP_EVENT | LibDrm.DRM_MODE_PAGE_FLIP_ASYNC, userData);
        if (result != 0)
        {
            result = LibDrm.drmModePageFlip(_fd, _crtcId, framebufferId, LibDrm.DRM_MODE_PAGE_FLIP_EVENT, userData);
        }

        if (result != 0)
        {
            Logger.Write($"drmModePageFlip failed (errno {Marshal.GetLastPInvokeError()}); frame dropped");
            LibGbm.gbm_surface_release_buffer(_gbmSurface, bo);
            return;
        }

        _pendingBo = bo;
    }

    private uint GetFramebufferId(nint bo)
    {
        nint data = LibGbm.gbm_bo_get_user_data(bo);
        if (data != 0)
        {
            return (uint)(data & 0xFFFFFFFF);
        }

        uint width = LibGbm.gbm_bo_get_width(bo);
        uint height = LibGbm.gbm_bo_get_height(bo);
        uint stride = LibGbm.gbm_bo_get_stride(bo);
        uint handle = (uint)LibGbm.gbm_bo_get_handle(bo);

        uint* handles = stackalloc uint[4] { handle, 0, 0, 0 };
        uint* pitches = stackalloc uint[4] { stride, 0, 0, 0 };
        uint* offsets = stackalloc uint[4] { 0, 0, 0, 0 };
        if (LibDrm.drmModeAddFB2(_fd, width, height, LibGbm.GBM_FORMAT_XRGB8888, handles, pitches, offsets, out uint framebufferId, 0) != 0
            && LibDrm.drmModeAddFB(_fd, width, height, 24, 32, stride, handle, out framebufferId) != 0)
        {
            return 0;
        }

        // The card fd rides in the high half so the destroy callback can remove the framebuffer without state.
        LibGbm.gbm_bo_set_user_data(bo, (nint)(((long)_fd << 32) | framebufferId), &DestroyFramebuffer);
        return framebufferId;
    }

    [UnmanagedCallersOnly]
    private static void DestroyFramebuffer(nint bo, nint data)
        => LibDrm.drmModeRmFB((int)(data >> 32), (uint)(data & 0xFFFFFFFF));

    [UnmanagedCallersOnly]
    private static void OnPageFlip(int fd, uint sequence, uint seconds, uint microseconds, nint userData)
    {
        if (GCHandle.FromIntPtr(userData).Target is DrmKmsOutput output)
        {
            output.CompleteFlip();
        }
    }

    private void CompleteFlip()
    {
        if (_frontBo != 0)
        {
            LibGbm.gbm_surface_release_buffer(_gbmSurface, _frontBo);
        }

        _frontBo = _pendingBo;
        _pendingBo = 0;
        if (_queuedBo != 0)
        {
            nint queued = _queuedBo;
            _queuedBo = 0;
            SubmitFlip(queued, _queuedFramebufferId);
        }

        FlipCompleted?.Invoke();
    }

    public void DispatchEvents()
    {
        var context = new DrmEventContext
        {
            Version = LibDrm.DRM_EVENT_CONTEXT_VERSION,
            PageFlipHandler = &OnPageFlip,
        };
        LibDrm.drmHandleEvent(_fd, &context);
    }

    public void SetCursorVisible(bool visible)
    {
        visible &= _cursorAllowed;
        if (_cursorVisible == visible && _cursorApplied)
        {
            return;
        }

        _cursorVisible = visible;
        _cursorApplied = false;
        if (_modeSet)
        {
            ApplyCursor();
        }
    }

    public void MoveCursor(int x, int y)
    {
        _cursorX = x;
        _cursorY = y;
        if (_modeSet && _cursorVisible)
        {
            LibDrm.drmModeMoveCursor(_fd, _crtcId, x, y);
        }
    }

    private void ApplyCursor()
    {
        if (!_cursorVisible)
        {
            LibDrm.drmModeSetCursor(_fd, _crtcId, 0, 0, 0);
            _cursorApplied = true;
            return;
        }

        if (_cursorBo == 0)
        {
            _cursorBo = CreateArrowCursor();
            if (_cursorBo == 0)
            {
                Logger.Write("cursor buffer creation failed; pointer stays hidden");
                _cursorApplied = true;
                return;
            }
        }

        uint handle = (uint)LibGbm.gbm_bo_get_handle(_cursorBo);
        if (LibDrm.drmModeSetCursor(_fd, _crtcId, handle, CURSOR_SIZE, CURSOR_SIZE) != 0)
        {
            Logger.Write("drmModeSetCursor failed; pointer stays hidden");
        }

        LibDrm.drmModeMoveCursor(_fd, _crtcId, _cursorX, _cursorY);
        _cursorApplied = true;
    }

    private nint CreateArrowCursor()
    {
        nint bo = LibGbm.gbm_bo_create(_gbmDevice, CURSOR_SIZE, CURSOR_SIZE, LibGbm.GBM_FORMAT_ARGB8888,
            LibGbm.GBM_BO_USE_CURSOR | LibGbm.GBM_BO_USE_WRITE);
        if (bo == 0)
        {
            return 0;
        }

        var pixels = new uint[CURSOR_SIZE * CURSOR_SIZE];
        FramebufferCursorImage.RenderArrow(pixels, CURSOR_SIZE);
        fixed (uint* data = pixels)
        {
            if (LibGbm.gbm_bo_write(bo, data, (nuint)(pixels.Length * sizeof(uint))) != 0)
            {
                LibGbm.gbm_bo_destroy(bo);
                return 0;
            }
        }

        return bo;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        WaitForPendingFlip();
        _disposed = true;

        if (_modeSet)
        {
            LibDrm.drmModeSetCursor(_fd, _crtcId, 0, 0, 0);
            if (_hasSavedCrtc && _savedCrtc.ModeValid != 0)
            {
                uint connectorId = _connectorId;
                var mode = _savedCrtc.Mode;
                LibDrm.drmModeSetCrtc(_fd, _savedCrtc.CrtcId, _savedCrtc.BufferId, _savedCrtc.X, _savedCrtc.Y, &connectorId, 1, &mode);
            }
        }

        if (_cursorBo != 0)
        {
            LibGbm.gbm_bo_destroy(_cursorBo);
            _cursorBo = 0;
        }

        // The GBM device, surface and card fd stay open: the graphics backend's EGL display and surface are
        // built on them and may be torn down after the host. They go away with the process.
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private void WaitForPendingFlip()
    {
        long deadline = Environment.TickCount64 + DISPOSE_FLIP_WAIT_MS;
        while (FlipPending)
        {
            int remaining = (int)(deadline - Environment.TickCount64);
            if (remaining <= 0)
            {
                break;
            }

            var pollFd = new PollFd { fd = _fd, events = PollEvents.POLLIN };
            if (LibC.Poll(&pollFd, 1, remaining) > 0)
            {
                DispatchEvents();
            }
        }
    }

    private sealed class DrmKmsWindowSurface : IDrmKmsWindowSurface
    {
        private readonly DrmKmsOutput _output;

        public DrmKmsWindowSurface(DrmKmsOutput output, double dpiScale)
        {
            _output = output;
            DpiScale = dpiScale;
        }

        public nint Handle => FramebufferWindowBackend.WINDOW_HANDLE;

        public int PixelWidth => _output.PixelWidth;

        public int PixelHeight => _output.PixelHeight;

        public double DpiScale { get; }

        public PlatformDisplayIdentity DisplayIdentity => new(_output._connectorId, _output._crtcId, _output._fd);

        public nint GbmDevice => _output._gbmDevice;

        public nint GbmSurface => _output._gbmSurface;

        public uint GbmFormat => LibGbm.GBM_FORMAT_XRGB8888;

        public void OnBuffersSwapped() => _output.Present();
    }
}
