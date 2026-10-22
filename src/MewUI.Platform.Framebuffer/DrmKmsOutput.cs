using System.Runtime.InteropServices;

using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>
/// A DRM/KMS output: one connector driven at one mode from a ring of three GBM buffers the backend renders into,
/// presented with page flips. The flip completes asynchronously; one more frame may wait behind it, and
/// <see cref="CanTakeFrame"/> holds the host off beyond that.
/// </summary>
internal sealed unsafe class DrmKmsOutput : IFramebufferOutput
{
    private static readonly EnvDebugLogger Logger = new("MEWUI_FB_DEBUG", "[FB][DRM]");

    private const int MAX_CARD_INDEX = 8;
    private const int CURSOR_SIZE = 64;
    private const int DISPOSE_FLIP_WAIT_MS = 500;

    // Shown, flipping and being drawn: the host draws at most one frame ahead of the flip, so three suffice.
    private const int SCANOUT_BUFFER_COUNT = 3;

    private readonly int _fd;
    private readonly uint _connectorId;
    private readonly uint _crtcId;
    private readonly DrmModeModeInfo _mode;
    private readonly DrmModeCrtc _savedCrtc;
    private readonly bool _hasSavedCrtc;
    private readonly nint _gbmDevice;
    private readonly ScanoutBuffer[] _buffers;
    private readonly bool _cursorAllowed;
    private readonly DrmKmsWindowSurface _surface;
    private GCHandle _selfHandle;
    private ScanoutBuffer? _front;
    private ScanoutBuffer? _pending;
    private ScanoutBuffer? _queued;
    private long _presentCount;
    private bool _modeSet;
    private bool _disposed;
    private readonly double _dpiScale;
    private readonly Dictionary<CursorType, CursorPlaneImage> _cursorImages = [];
    private CursorType? _cursorShape;
    private CursorPlaneImage? _shownCursor;
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
        nint[] scanoutBuffers,
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
        _buffers = new ScanoutBuffer[scanoutBuffers.Length];
        for (int index = 0; index < scanoutBuffers.Length; index++)
        {
            _buffers[index] = new ScanoutBuffer(scanoutBuffers[index]);
        }

        _cursorAllowed = cursorAllowed;
        _dpiScale = dpiScale;
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

    public bool FlipPending => _pending != null;

    public bool CanTakeFrame => _queued == null && FindBufferToDraw() != null;

    public int EventFd => _fd;

    public IWindowSurface WindowSurface => _surface;

    /// <summary>Raised when a page flip completes and the screen can take the next frame.</summary>
    public event Action? FlipCompleted;

    /// <summary>
    /// Opens the first usable card (or the one in <paramref name="options"/>), returning null with the reason
    /// when no card has a connected display this process may drive.
    /// </summary>
    public static DrmKmsOutput? TryOpen(FramebufferPlatformOptions options, IFramebufferSession session, out string failure)
    {
        var failures = new List<string>();
        foreach (var path in EnumerateCardPaths(options.DrmDevicePath))
        {
            var output = TryOpenCard(path, options, session, out string reason);
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

    private static DrmKmsOutput? TryOpenCard(string path, FramebufferPlatformOptions options, IFramebufferSession session, out string failure)
    {
        int fd = session.OpenDevice(path, LibC.O_RDWR);
        if (fd < 0)
        {
            failure = $"open failed (errno {-fd})";
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

                var scanoutBuffers = new nint[SCANOUT_BUFFER_COUNT];
                for (int index = 0; index < scanoutBuffers.Length; index++)
                {
                    scanoutBuffers[index] = LibGbm.gbm_bo_create(gbmDevice, selection.Mode.HDisplay, selection.Mode.VDisplay,
                        LibGbm.GBM_FORMAT_XRGB8888, LibGbm.GBM_BO_USE_SCANOUT | LibGbm.GBM_BO_USE_RENDERING);
                    if (scanoutBuffers[index] == 0)
                    {
                        for (int created = 0; created < index; created++)
                        {
                            LibGbm.gbm_bo_destroy(scanoutBuffers[created]);
                        }

                        LibGbm.gbm_device_destroy(gbmDevice);
                        failure = "gbm_bo_create for a scan-out buffer failed";
                        return null;
                    }
                }

                keepFd = true;
                failure = string.Empty;
                return new DrmKmsOutput(fd, path, selection.ConnectorName, selection.ConnectorId, selection.CrtcId, selection.Mode,
                    savedCrtc, hasSavedCrtc, gbmDevice, scanoutBuffers, options.DpiScale > 0 ? options.DpiScale : 1.0, options.ShowCursor);
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
                session.CloseDevice(fd);
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

    /// <summary>
    /// The buffer the next frame draws into: the one a frame that never presented left behind, else the free
    /// buffer shown most recently, which has the least to restore. 0 when every buffer is on its way to the screen.
    /// </summary>
    internal nint AcquireBuffer()
    {
        var buffer = FindBufferToDraw();
        if (buffer == null)
        {
            return 0;
        }

        buffer.State = ScanoutBufferState.Drawing;
        return buffer.Bo;
    }

    private ScanoutBuffer? FindBufferToDraw()
    {
        ScanoutBuffer? newest = null;
        foreach (var buffer in _buffers)
        {
            if (buffer.State == ScanoutBufferState.Drawing)
            {
                return buffer;
            }

            if (buffer.State == ScanoutBufferState.Free && (newest == null || buffer.LastPresent > newest.LastPresent))
            {
                newest = buffer;
            }
        }

        return newest;
    }

    internal DrmKmsBufferExport ExportBuffer(nint bo)
    {
        if (FindBuffer(bo) == null)
        {
            throw new ArgumentException("The buffer does not belong to this output.", nameof(bo));
        }

        int fd = LibGbm.gbm_bo_get_fd(bo);
        if (fd < 0)
        {
            throw new InvalidOperationException("GBM: the scan-out buffer cannot be exported as a dma-buf.");
        }

        return new DrmKmsBufferExport(fd, LibGbm.gbm_bo_get_offset(bo, 0), LibGbm.gbm_bo_get_stride(bo), LibGbm.gbm_bo_get_modifier(bo));
    }

    /// <summary>How many presents ago <paramref name="bo"/> was presented, or 0 when it never was.</summary>
    internal int GetBufferAge(nint bo)
    {
        var buffer = FindBuffer(bo);
        return buffer == null || buffer.LastPresent == 0 ? 0 : (int)(_presentCount - buffer.LastPresent + 1);
    }

    /// <summary>Puts a buffer the backend finished drawing on screen: a mode set the first time, a page flip after.</summary>
    internal void Present(nint bo)
    {
        if (_disposed)
        {
            return;
        }

        var buffer = FindBuffer(bo) ?? throw new ArgumentException("The buffer does not belong to this output.", nameof(bo));
        buffer.LastPresent = ++_presentCount;
        uint framebufferId = GetFramebufferId(buffer);
        if (framebufferId == 0)
        {
            buffer.State = ScanoutBufferState.Free;
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
                buffer.State = ScanoutBufferState.Free;
                throw new InvalidOperationException(
                    $"drmModeSetCrtc failed (errno {errno}). Another process holds the display; stop the display server (Xorg, Wayland compositor) before starting a framebuffer app.");
            }

            _modeSet = true;
            _front = buffer;
            buffer.State = ScanoutBufferState.Shown;
            ApplyCursor();
            return;
        }

        if (_pending != null)
        {
            // Legacy KMS takes one flip at a time; this frame goes up the moment the current flip lands.
            if (_queued != null)
            {
                _queued.State = ScanoutBufferState.Free;
            }

            _queued = buffer;
            buffer.State = ScanoutBufferState.Queued;
            return;
        }

        SubmitFlip(buffer);
    }

    private void SubmitFlip(ScanoutBuffer buffer)
    {
        bool vsync = !Application.IsRunning || Application.Current.RenderLoopSettings.VSyncEnabled;
        nint userData = GCHandle.ToIntPtr(_selfHandle);
        int result = vsync
            ? -1
            : LibDrm.drmModePageFlip(_fd, _crtcId, buffer.FramebufferId, LibDrm.DRM_MODE_PAGE_FLIP_EVENT | LibDrm.DRM_MODE_PAGE_FLIP_ASYNC, userData);
        if (result != 0)
        {
            result = LibDrm.drmModePageFlip(_fd, _crtcId, buffer.FramebufferId, LibDrm.DRM_MODE_PAGE_FLIP_EVENT, userData);
        }

        if (result != 0)
        {
            Logger.Write($"drmModePageFlip failed (errno {Marshal.GetLastPInvokeError()}); frame dropped");
            buffer.State = ScanoutBufferState.Free;
            return;
        }

        _pending = buffer;
        buffer.State = ScanoutBufferState.Flipping;
    }

    private ScanoutBuffer? FindBuffer(nint bo)
    {
        foreach (var buffer in _buffers)
        {
            if (buffer.Bo == bo)
            {
                return buffer;
            }
        }

        return null;
    }

    private uint GetFramebufferId(ScanoutBuffer buffer)
    {
        if (buffer.FramebufferId != 0)
        {
            return buffer.FramebufferId;
        }

        nint bo = buffer.Bo;
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

        buffer.FramebufferId = framebufferId;
        return framebufferId;
    }

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
        if (_front != null)
        {
            _front.State = ScanoutBufferState.Free;
        }

        _front = _pending;
        _pending = null;
        if (_front != null)
        {
            _front.State = ScanoutBufferState.Shown;
        }

        if (_queued != null)
        {
            var queued = _queued;
            _queued = null;
            SubmitFlip(queued);
        }

        FlipCompleted?.Invoke();
    }

    public void Suspend()
    {
        // The seat manager drops the master right after this returns, so nothing may stay queued behind the flip.
        if (_queued != null)
        {
            _queued.State = ScanoutBufferState.Free;
            _queued = null;
        }

        WaitForPendingFlip();
        _cursorApplied = false;
    }

    public void Resume()
    {
        if (_disposed || !_modeSet || _front == null)
        {
            return;
        }

        uint connectorId = _connectorId;
        var mode = _mode;
        if (LibDrm.drmModeSetCrtc(_fd, _crtcId, _front.FramebufferId, 0, 0, &connectorId, 1, &mode) != 0)
        {
            Logger.Write($"drmModeSetCrtc on resume failed (errno {Marshal.GetLastPInvokeError()})");
        }

        ApplyCursor();
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

    public void SetCursor(CursorType? shape)
    {
        if (!_cursorAllowed)
        {
            shape = null;
        }

        if (_cursorShape == shape && _cursorApplied)
        {
            return;
        }

        _cursorShape = shape;
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
        var shown = _shownCursor;
        if (_modeSet && shown != null)
        {
            // The plane is placed by its top-left pixel; the hot spot handed to the driver only informs virtual
            // display hosts that draw the pointer themselves.
            LibDrm.drmModeMoveCursor(_fd, _crtcId, x - shown.HotX, y - shown.HotY);
        }
    }

    private void ApplyCursor()
    {
        _cursorApplied = true;
        var image = _cursorShape == null ? null : GetCursorImage(_cursorShape.Value);
        if (image == null)
        {
            LibDrm.drmModeSetCursor(_fd, _crtcId, 0, 0, 0);
            _shownCursor = null;
            return;
        }

        uint handle = (uint)LibGbm.gbm_bo_get_handle(image.Buffer);
        if (LibDrm.drmModeSetCursor2(_fd, _crtcId, handle, CURSOR_SIZE, CURSOR_SIZE, image.HotX, image.HotY) != 0
            && LibDrm.drmModeSetCursor(_fd, _crtcId, handle, CURSOR_SIZE, CURSOR_SIZE) != 0)
        {
            Logger.Write("drmModeSetCursor failed; pointer stays hidden");
            _shownCursor = null;
            return;
        }

        _shownCursor = image;
        MoveCursor(_cursorX, _cursorY);
    }

    private CursorPlaneImage? GetCursorImage(CursorType shape)
    {
        if (_cursorImages.TryGetValue(shape, out var cached))
        {
            return cached;
        }

        nint bo = LibGbm.gbm_bo_create(_gbmDevice, CURSOR_SIZE, CURSOR_SIZE, LibGbm.GBM_FORMAT_ARGB8888,
            LibGbm.GBM_BO_USE_CURSOR | LibGbm.GBM_BO_USE_WRITE);
        if (bo == 0)
        {
            Logger.Write("cursor buffer creation failed; pointer stays hidden");
            return null;
        }

        var bitmap = FramebufferCursorImage.Create(shape, _dpiScale, CURSOR_SIZE);
        var pixels = new uint[CURSOR_SIZE * CURSOR_SIZE];
        for (int row = 0; row < bitmap.Height; row++)
        {
            bitmap.Pixels.AsSpan(row * bitmap.Width, bitmap.Width).CopyTo(pixels.AsSpan(row * CURSOR_SIZE));
        }

        fixed (uint* data = pixels)
        {
            if (LibGbm.gbm_bo_write(bo, data, (nuint)(pixels.Length * sizeof(uint))) != 0)
            {
                LibGbm.gbm_bo_destroy(bo);
                Logger.Write("cursor buffer write failed; pointer stays hidden");
                return null;
            }
        }

        var image = new CursorPlaneImage(bo, bitmap.HotX, bitmap.HotY);
        _cursorImages[shape] = image;
        return image;
    }

    private sealed record CursorPlaneImage(nint Buffer, int HotX, int HotY);

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

        foreach (var image in _cursorImages.Values)
        {
            LibGbm.gbm_bo_destroy(image.Buffer);
        }

        _cursorImages.Clear();
        _shownCursor = null;

        foreach (var buffer in _buffers)
        {
            if (buffer.FramebufferId != 0)
            {
                LibDrm.drmModeRmFB(_fd, buffer.FramebufferId);
            }

            // The backend's images keep their own reference to the buffer memory, so this only drops the platform's.
            LibGbm.gbm_bo_destroy(buffer.Bo);
        }

        // The GBM device and card fd stay open: the graphics backend's EGL display is built on them and may be
        // torn down after the host. They go away with the process.
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

        public uint GbmFormat => LibGbm.GBM_FORMAT_XRGB8888;

        public nint AcquireBuffer() => _output.AcquireBuffer();

        public DrmKmsBufferExport ExportBuffer(nint buffer) => _output.ExportBuffer(buffer);

        public int GetBufferAge(nint buffer) => _output.GetBufferAge(buffer);

        public void Present(nint buffer) => _output.Present(buffer);
    }

    private enum ScanoutBufferState
    {
        Free,
        Drawing,
        Queued,
        Flipping,
        Shown,
    }

    private sealed class ScanoutBuffer(nint bo)
    {
        public nint Bo { get; } = bo;

        public uint FramebufferId { get; set; }

        /// <summary>The present counter when the buffer was last presented; 0 before its first.</summary>
        public long LastPresent { get; set; }

        public ScanoutBufferState State { get; set; }
    }
}
