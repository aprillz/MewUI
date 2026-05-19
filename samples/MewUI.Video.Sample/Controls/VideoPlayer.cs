using System.Diagnostics;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Video.Sample.Diagnostics;
using Aprillz.MewUI.Video.Sample.Playback;

namespace Aprillz.MewUI.Video.Sample.Controls;

/// <summary>
/// Owns the playback engine (<see cref="VideoPlayback"/>) and its visual surface
/// (<see cref="VideoView"/>) together with the surrounding lifecycle — file loading,
/// play/pause / seek / drag-preview seeking, GPU interop reload on device/display
/// change. Originally inlined into Program.cs; pulled out so the sample host code
/// only deals with UI element wiring and stats/overlay concerns.
/// </summary>
public sealed class VideoPlayer : IDisposable
{
    private readonly Window _owner;
    private VideoPlayback? _playback;
    private int _loadRequestId;
    private int _gpuInteropReloadRequestId;
    private bool _isLoading;
    private bool _isSeekDragActive;
    private bool _dragPreviewSeekInFlight;
    private double _pendingDragSeekSeconds = -1;
    private bool _gpuInteropReloadInFlight;
    private long _lastGpuInteropInvalidationLogTicks;
    private long _lastGpuInteropReloadTicks;

    /// <summary>
    /// The visible video surface. Passed in by the host (typically already present in the
    /// window's content tree) — the player subscribes to its GPU-interop events and pushes
    /// playback updates to it but does not own the element's layout/visibility.
    /// </summary>
    public VideoView View { get; }

    /// <summary>Active playback session, or <see langword="null"/> when no file is loaded.</summary>
    public VideoPlayback? Playback => _playback;

    /// <summary>True between <see cref="LoadAsync"/> entry and its completion.</summary>
    public bool IsLoading => _isLoading;

    /// <summary>True while the user is mid-drag on a seek slider. Drives drag-preview seek behavior.</summary>
    public bool IsSeekDragActive => _isSeekDragActive;

    /// <summary>
    /// When true, a GPU-interop invalidation that involves both a render-target device change
    /// AND a display change triggers an automatic playback recreate. Off by default — the host
    /// can opt in if it wants seamless display-move support.
    /// </summary>
    public bool EnableGpuInteropAutoReload { get; set; }

    /// <summary>
    /// Fired with a short human-readable message whenever the player wants the host's status
    /// area to display something (loading, seek position, error). The host decides how to render.
    /// </summary>
    public event Action<string>? StatusChanged;

    /// <summary>Fired when <see cref="IsLoading"/> transitions, true on entry / false on exit.</summary>
    public event Action<bool>? LoadingStateChanged;

    /// <summary>
    /// Fired after the active <see cref="Playback"/> reference changes (new playback assigned
    /// after a successful load, or cleared by Dispose / reload). Host typically resets sliders,
    /// stats counters, etc.
    /// </summary>
    public event Action? PlaybackReplaced;

    public VideoPlayer(Window owner, VideoView view)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(view);
        _owner = owner;
        View = view;
        View.GpuInteropInvalidated += OnVideoGpuInteropInvalidated;
    }

    /// <summary>
    /// Loads a video file off the UI thread, then assigns it as the active playback. Subsequent
    /// calls cancel any in-flight earlier load. <paramref name="initialPosition"/> seeks the
    /// new playback before <paramref name="startPlaying"/> takes effect.
    /// </summary>
    public async Task LoadAsync(
        string? path,
        TimeSpan? initialPosition = null,
        bool startPlaying = true)
    {
        SampleLog.Write($"VideoPlayer.LoadAsync called. path={path ?? "<null>"}");
        int requestId = Interlocked.Increment(ref _loadRequestId);
        Interlocked.Increment(ref _gpuInteropReloadRequestId);

        if (string.IsNullOrWhiteSpace(path))
        {
            SampleLog.Write("LoadAsync aborted: path is empty.");
            StatusChanged?.Invoke("Path is empty.");
            return;
        }

        if (!File.Exists(path))
        {
            SampleLog.Write("LoadAsync aborted: file does not exist.");
            StatusChanged?.Invoke($"File not found: {path}");
            return;
        }

        SetLoading(true);
        StatusChanged?.Invoke($"Loading: {Path.GetFileName(path)}");
        IBusyIndicator? busyIndicator = _owner.CreateBusyIndicator($"Loading {Path.GetFileName(path)}...");

        try
        {
            SampleLog.Write("Creating VideoPlayback on background thread.");
            nint preferredD3D11Device = _owner.GraphicsFactory is ID3D11RenderTargetDeviceProvider d3d11Provider
                ? d3d11Provider.RetainD3D11DeviceForRenderTarget(_owner.Handle)
                : 0;
            VideoPlayback newPlayback;
            try
            {
                newPlayback = await Task.Run(() => new VideoPlayback(path, preferredD3D11Device));
            }
            finally
            {
                if (preferredD3D11Device != 0)
                {
                    Marshal.Release(preferredD3D11Device);
                }
            }

            if (requestId != Volatile.Read(ref _loadRequestId))
            {
                SampleLog.Write($"Discarding stale load result. requestId={requestId}");
                newPlayback.Dispose();
                return;
            }

            ReplacePlayback(newPlayback);
            SampleLog.Write($"Playback created. size={newPlayback.PixelWidth}x{newPlayback.PixelHeight}, duration={newPlayback.Duration}");

            if (initialPosition is { } seekPosition && seekPosition > TimeSpan.Zero)
            {
                var clampedPosition = newPlayback.Duration > TimeSpan.Zero && seekPosition > newPlayback.Duration
                    ? newPlayback.Duration
                    : seekPosition;
                newPlayback.Seek(clampedPosition);
                SampleLog.Write($"Playback initial seek: {clampedPosition}");
            }

            if (startPlaying)
            {
                newPlayback.Play();
                SampleLog.Write("Playback.Play invoked.");
            }

            View.InvalidateVisual();
            UpdateRenderLoopMode();
            StatusChanged?.Invoke($"Loaded: {Path.GetFileName(path)} ({newPlayback.PixelWidth}x{newPlayback.PixelHeight})");
            SampleLog.Write("LoadAsync completed successfully.");
        }
        catch (DllNotFoundException ex)
        {
            SampleLog.Write($"DllNotFoundException: {ex}");
            StatusChanged?.Invoke("FFmpeg native DLLs were not found. See samples/MewUI.Video.Sample/README.md.");
            Console.WriteLine(ex.ToString());
        }
        catch (Exception ex)
        {
            SampleLog.Write($"LoadAsync failed: {ex}");
            StatusChanged?.Invoke($"Failed to load video: {ex.Message}");
        }
        finally
        {
            busyIndicator?.Dispose();
            if (requestId == Volatile.Read(ref _loadRequestId))
            {
                SetLoading(false);
            }
        }
    }

    /// <summary>
    /// Toggles play/pause on the active playback. Caller is responsible for triggering a
    /// fresh <see cref="LoadAsync"/> if no playback exists (this method just reports status).
    /// </summary>
    public void TogglePlayPause()
    {
        if (_playback is null)
        {
            if (_isLoading)
            {
                SampleLog.Write("TogglePlayPause ignored while load is in progress.");
                StatusChanged?.Invoke("Loading video...");
                return;
            }
            // No playback and not loading — caller should kick off LoadAsync from its text box.
            return;
        }

        if (_playback.IsPlaying)
        {
            SampleLog.Write("Playback paused by user.");
            _playback.Pause();
            StatusChanged?.Invoke("Paused.");
        }
        else
        {
            ResetDragPreviewState();
            SampleLog.Write("Playback resumed by user.");
            _playback.Play();
            StatusChanged?.Invoke($"Playing: {Path.GetFileName(_playback.SourcePath)}");
            View.InvalidateVisual();
        }

        UpdateRenderLoopMode();
    }

    /// <summary>
    /// Called by the host's seek slider on every value change. Splits into "live seek" (when
    /// not dragging or playback is running) versus "drag preview seek" (paused + dragging —
    /// rapid seeks coalesced through <see cref="TryStartDragPreviewSeek"/>).
    /// </summary>
    public void OnSeekValueChanged(double seconds)
    {
        if (_playback is null)
        {
            return;
        }

        if (_isSeekDragActive && !_playback.IsPlaying)
        {
            _pendingDragSeekSeconds = seconds;
            TryStartDragPreviewSeek(_playback);
            StatusChanged?.Invoke($"Preview: {FormatTime(TimeSpan.FromSeconds(seconds))}");
            return;
        }

        ResetDragPreviewState();
        _playback.Seek(TimeSpan.FromSeconds(seconds));
        View.InvalidateVisual();

        if (!_isSeekDragActive)
        {
            StatusChanged?.Invoke($"Seek: {FormatTime(TimeSpan.FromSeconds(seconds))}");
        }
    }

    public void BeginSeekDrag()
    {
        _isSeekDragActive = true;
    }

    public void EndSeekDrag()
    {
        _isSeekDragActive = false;

        if (_playback is null || _playback.IsPlaying || _pendingDragSeekSeconds < 0)
        {
            return;
        }

        double targetSeconds = _pendingDragSeekSeconds;
        ResetDragPreviewState();
        _playback.Seek(TimeSpan.FromSeconds(targetSeconds));
        View.InvalidateVisual();
        StatusChanged?.Invoke($"Seek: {FormatTime(TimeSpan.FromSeconds(targetSeconds))}");
    }

    public void SetForceCpuReadback(bool enabled)
    {
        if (_playback is not null)
        {
            _playback.ForceCpuReadback = enabled;
        }
        SampleLog.Write($"ForceCpuReadback toggled: {enabled}");
    }

    /// <summary>
    /// Forces the render loop into request-driven mode for the active playback. Host's
    /// per-frame UI tick calls this so a paused playback doesn't drive continuous redraws.
    /// </summary>
    public void UpdateRenderLoopMode()
    {
        if (!Application.IsRunning)
        {
            return;
        }

        Application.Current.RenderLoopSettings.SetContinuous(false);
    }

    public void Dispose()
    {
        View.GpuInteropInvalidated -= OnVideoGpuInteropInvalidated;
        ReplacePlayback(null);
    }

    private void SetLoading(bool isLoading)
    {
        if (_isLoading == isLoading)
        {
            return;
        }

        _isLoading = isLoading;
        LoadingStateChanged?.Invoke(isLoading);
    }

    private void ReplacePlayback(VideoPlayback? newPlayback)
    {
        ResetDragPreviewState();
        SampleLog.Write($"ReplacePlayback: {(newPlayback is null ? "clear" : Path.GetFileName(newPlayback.SourcePath))}");

        var oldPlayback = _playback;
        if (oldPlayback is not null)
        {
            oldPlayback.FrameReady -= OnPlaybackFrameReadyForDragPreview;
        }

        _playback = newPlayback;
        View.Playback = newPlayback;
        if (newPlayback is not null)
        {
            newPlayback.FrameReady += OnPlaybackFrameReadyForDragPreview;
        }

        oldPlayback?.Dispose();
        UpdateRenderLoopMode();
        PlaybackReplaced?.Invoke();
    }

    private void OnPlaybackFrameReadyForDragPreview()
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher is null || dispatcher.IsOnUIThread)
        {
            CompleteDragPreviewSeek();
            return;
        }

        dispatcher.BeginInvoke(CompleteDragPreviewSeek);
    }

    private void CompleteDragPreviewSeek()
    {
        _dragPreviewSeekInFlight = false;

        if (_playback is null || _playback.IsPlaying || !_isSeekDragActive)
        {
            return;
        }

        TryStartDragPreviewSeek(_playback);
    }

    private void TryStartDragPreviewSeek(VideoPlayback target)
    {
        if (_dragPreviewSeekInFlight || !_isSeekDragActive || target.IsPlaying || _pendingDragSeekSeconds < 0)
        {
            return;
        }

        double targetSeconds = _pendingDragSeekSeconds;
        _pendingDragSeekSeconds = -1;
        _dragPreviewSeekInFlight = true;
        target.Seek(TimeSpan.FromSeconds(targetSeconds));
    }

    private void ResetDragPreviewState()
    {
        _dragPreviewSeekInFlight = false;
        _pendingDragSeekSeconds = -1;
    }

    private void OnVideoGpuInteropInvalidated(object? sender, GpuInteropInvalidatedEventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        bool shouldLog = _lastGpuInteropInvalidationLogTicks == 0
            || now - _lastGpuInteropInvalidationLogTicks >= Stopwatch.Frequency;
        if (shouldLog)
        {
            _lastGpuInteropInvalidationLogTicks = now;
            SampleLog.Write(
                $"GPU interop invalidation observed. reason={e.Reason}, renderTarget=0x{e.RenderTargetHandle:X}, displayChanged={e.DisplayChanged}, automaticReload={e.RenderTargetDeviceChanged && e.DisplayChanged}");
        }

        if (!EnableGpuInteropAutoReload || !e.RenderTargetDeviceChanged || !e.DisplayChanged)
        {
            return;
        }

        if (_gpuInteropReloadInFlight || _isLoading || _playback is null)
        {
            return;
        }

        if (_lastGpuInteropReloadTicks != 0
            && now - _lastGpuInteropReloadTicks < Stopwatch.Frequency * 2)
        {
            return;
        }

        var currentPlayback = _playback;
        string path = currentPlayback.SourcePath;
        TimeSpan position = currentPlayback.Position;
        bool wasPlaying = currentPlayback.IsPlaying;
        _gpuInteropReloadInFlight = true;
        _lastGpuInteropReloadTicks = now;

        SampleLog.Write(
            $"Reloading playback after display/device change. renderTarget=0x{e.RenderTargetHandle:X}, position={position}, wasPlaying={wasPlaying}");

        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher is null)
        {
            _gpuInteropReloadInFlight = false;
            return;
        }

        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await Task.Delay(150);
                await ReloadPlaybackForGpuInteropAsync(
                    path,
                    position,
                    wasPlaying,
                    Interlocked.Increment(ref _gpuInteropReloadRequestId));
            }
            finally
            {
                _gpuInteropReloadInFlight = false;
            }
        });
    }

    private async Task ReloadPlaybackForGpuInteropAsync(string path, TimeSpan position, bool startPlaying, int requestId)
    {
        SampleLog.Write($"GPU interop decoder reload started. path={path}, requestId={requestId}, position={position}, startPlaying={startPlaying}");

        try
        {
            var targetPlayback = _playback;
            if (targetPlayback is null)
            {
                return;
            }

            nint preferredD3D11Device = _owner.GraphicsFactory is ID3D11RenderTargetDeviceProvider d3d11Provider
                ? d3d11Provider.RetainD3D11DeviceForRenderTarget(_owner.Handle)
                : 0;
            try
            {
                await Task.Run(() => targetPlayback.RecreateDecoder(preferredD3D11Device, position));
            }
            finally
            {
                if (preferredD3D11Device != 0)
                {
                    Marshal.Release(preferredD3D11Device);
                }
            }

            if (requestId != Volatile.Read(ref _gpuInteropReloadRequestId))
            {
                SampleLog.Write($"GPU interop decoder reload completed but request is stale. requestId={requestId}");
                return;
            }

            if (startPlaying)
            {
                targetPlayback.Play();
            }
            else
            {
                targetPlayback.Pause();
            }

            View.InvalidateVisual();
            UpdateRenderLoopMode();
            SampleLog.Write("GPU interop decoder reload completed.");
        }
        catch (Exception ex)
        {
            SampleLog.Write($"GPU interop decoder reload failed: {ex}");
        }
    }

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return time.ToString(@"hh\:mm\:ss");
    }
}
