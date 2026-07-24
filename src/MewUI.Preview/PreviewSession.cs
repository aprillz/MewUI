using System.Diagnostics;
using System.Text.Json;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.HotReload;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Preview;

/// <summary>
/// Orchestrates one preview session inside the user's app process: owns the IDE channel, the
/// target list, the active preview window, and offscreen frame production. All state is mutated
/// on the UI thread; channel callbacks are marshalled through the dispatcher.
/// </summary>
internal sealed class PreviewSession : IDisposable
{
    private const double DEFAULT_VIEWPORT_WIDTH = 800;
    private const double DEFAULT_VIEWPORT_HEIGHT = 600;
    private const int ACK_TIMEOUT_MS = 5000;

    private readonly HashSet<Window> _dirtyWindows = new();
    private PreviewChannel? _channel;
    private Application? _app;
    private Window? _mainWindow;
    private Window? _activeWindow;
    private Window? _wrapperWindow;
    private bool _wrapperIsComponentHost;
    private Action? _requestWake;
    private List<PreviewTargetScanner.TargetDescriptor>? _targets;
    private string _activeTargetId = PreviewTargetScanner.MAIN_WINDOW_ID;
    private IRenderSurface? _surface;
    private int _surfaceWidthPx;
    private int _surfaceHeightPx;
    private byte[] _frameBuffer = [];
    private long _frameSeq;
    private long _pendingAckSeq;
    private long _pendingAckSentAt;
    private double _viewportWidth = DEFAULT_VIEWPORT_WIDTH;
    private double _viewportHeight = DEFAULT_VIEWPORT_HEIGHT;
    private double _clientDpi;

    public void Start(Application app, Window mainWindow, Action requestWake)
    {
        _app = app;
        _mainWindow = mainWindow;
        _activeWindow = mainWindow;
        _requestWake = requestWake;
        MewUiHotReload.DeltaApplied += OnDeltaApplied;
        _channel = new PreviewChannel(OnChannelMessage, OnChannelConnected);
        _channel.Start();
    }

    public void Stop()
    {
        MewUiHotReload.DeltaApplied -= OnDeltaApplied;
        _channel?.Dispose();
        _channel = null;
        _surface?.Dispose();
        _surface = null;
    }

    public void Dispose() => Stop();

    public void NotifyPresented(Window window) => MarkDirty(window);

    public void NotifyClosed(Window window)
    {
        _dirtyWindows.Remove(window);
        if (ReferenceEquals(_wrapperWindow, window))
        {
            _wrapperWindow = null;
        }
        if (ReferenceEquals(_activeWindow, window))
        {
            _activeWindow = _mainWindow;
            _activeTargetId = PreviewTargetScanner.MAIN_WINDOW_ID;
            if (_activeWindow != null)
            {
                ApplyClientDpi(_activeWindow);
                MarkDirty(_activeWindow);
            }
        }
    }

    public void MarkDirty(Window window)
    {
        _dirtyWindows.Add(window);
        _requestWake?.Invoke();
    }

    /// <summary>Renders and streams the active window if it is dirty and the client acked the previous frame.</summary>
    public void RenderPendingFrame()
    {
        var window = _activeWindow;
        var channel = _channel;
        if (window == null || channel == null || !channel.IsConnected)
        {
            return;
        }

        if (_pendingAckSeq != 0)
        {
            // A client that lost the frame (e.g. its view reloaded) would deadlock the stream;
            // time the ack out and resend the latest state instead of waiting forever.
            long elapsedMs = (Stopwatch.GetTimestamp() - _pendingAckSentAt) * 1000 / Stopwatch.Frequency;
            if (elapsedMs < ACK_TIMEOUT_MS)
            {
                return;
            }
            PreviewTrace.Log($"ack timeout after {elapsedMs}ms (seq={_pendingAckSeq}); resending");
            _pendingAckSeq = 0;
            _dirtyWindows.Add(window);
        }

        if (!_dirtyWindows.Contains(window))
        {
            return;
        }

        _dirtyWindows.Remove(window);

        try
        {
            SendFrame(window, channel);
        }
        catch (Exception ex)
        {
            SendStatus($"Frame render failed: {ex.Message}", hasError: true, exceptionDetail: ex.ToString());
        }
    }

    private void SendFrame(Window window, PreviewChannel channel)
    {
        // GPU backends (MewVG GL) need a current context on this thread for surface creation,
        // rendering, and pixel readback; CPU backends return a no-op scope.
        using var renderScope = window.GraphicsFactory.AcquireBackgroundRenderScope();

        double dpiScale = window.DpiScale > 0 ? window.DpiScale : 1.0;
        var clientSize = window.ClientSize;
        int widthPx = Math.Max(1, (int)Math.Round(clientSize.Width * dpiScale));
        int heightPx = Math.Max(1, (int)Math.Round(clientSize.Height * dpiScale));

        if (_surface == null || _surfaceWidthPx != widthPx || _surfaceHeightPx != heightPx)
        {
            _surface?.Dispose();
            _surface = window.GraphicsFactory.CreateSurface(
                RenderSurfaceDescriptor.CachedImage(widthPx, heightPx, dpiScale));
            _surfaceWidthPx = widthPx;
            _surfaceHeightPx = heightPx;
        }

        long renderStart = Stopwatch.GetTimestamp();
        window.PerformLayout();
        window.RenderFrameToSurface(_surface);
        long renderMs = (Stopwatch.GetTimestamp() - renderStart) * 1000 / Stopwatch.Frequency;

        if (_surface is not ICpuPixelSurface cpu)
        {
            SendStatus(
                $"The registered graphics backend does not expose CPU-readable offscreen surfaces ({_surface.GetType().Name}).",
                hasError: true);
            return;
        }

        var pixels = cpu.GetReadOnlyPixelSpan();
        if (_frameBuffer.Length < pixels.Length)
        {
            _frameBuffer = new byte[pixels.Length];
        }
        pixels.CopyTo(_frameBuffer);

        long seq = ++_frameSeq;
        _pendingAckSeq = seq;
        _pendingAckSentAt = Stopwatch.GetTimestamp();
        channel.SendFrame(new FrameMessage
        {
            Seq = seq,
            Width = widthPx,
            Height = heightPx,
            Stride = cpu.StrideBytes,
            DpiScale = dpiScale,
        }, _frameBuffer.AsSpan(0, pixels.Length));
        long sendMs = (Stopwatch.GetTimestamp() - _pendingAckSentAt) * 1000 / Stopwatch.Frequency;
        PreviewTrace.Log($"frame seq={seq} {widthPx}x{heightPx} render={renderMs}ms send={sendMs}ms");
    }

    private void OnChannelConnected()
    {
        RunOnUiThread(() =>
        {
            _pendingAckSeq = 0;
            SendTargets();
            SendStatus($"Previewing {_activeTargetId}");
            if (_activeWindow != null)
            {
                MarkDirty(_activeWindow);
            }
        });
    }

    private void OnChannelMessage(int typeId, JsonDocument json)
    {
        RunOnUiThread(() =>
        {
            using (json)
            {
                HandleMessage(typeId, json);
            }
        });
    }

    private void HandleMessage(int typeId, JsonDocument json)
    {
        switch (typeId)
        {
            case PreviewProtocol.CLIENT_INFO:
                var info = json.Deserialize(PreviewJsonContext.Default.ClientInfoMessage);
                if (info != null && info.ViewportWidth > 0 && info.ViewportHeight > 0)
                {
                    ApplyClientMetrics(info.ViewportWidth, info.ViewportHeight, info.Dpi);
                }
                break;

            case PreviewProtocol.SELECT_TARGET:
                var select = json.Deserialize(PreviewJsonContext.Default.SelectTargetMessage);
                if (select != null)
                {
                    SelectTarget(select.Id);
                }
                break;

            case PreviewProtocol.VIEWPORT_CHANGED:
                var viewport = json.Deserialize(PreviewJsonContext.Default.ViewportChangedMessage);
                if (viewport != null && viewport.Width > 0 && viewport.Height > 0)
                {
                    ApplyClientMetrics(viewport.Width, viewport.Height, viewport.Dpi);
                }
                break;

            case PreviewProtocol.SET_THEME:
                var theme = json.Deserialize(PreviewJsonContext.Default.SetThemeMessage);
                if (theme != null)
                {
                    ApplyTheme(theme.Mode);
                }
                break;

            case PreviewProtocol.FRAME_ACK:
                var ack = json.Deserialize(PreviewJsonContext.Default.FrameAckMessage);
                if (ack != null && ack.Seq == _pendingAckSeq)
                {
                    _pendingAckSeq = 0;
                    _requestWake?.Invoke();
                }
                break;

            case PreviewProtocol.REFRESH_TARGET:
                RebuildActiveTarget("refresh");
                break;

            case PreviewProtocol.POINTER_MOVED:
            case PreviewProtocol.POINTER_PRESSED:
            case PreviewProtocol.POINTER_RELEASED:
                var pointer = json.Deserialize(PreviewJsonContext.Default.PointerMessage);
                if (pointer != null)
                {
                    HandlePointer(typeId, pointer);
                }
                break;

            case PreviewProtocol.SCROLL:
                var scroll = json.Deserialize(PreviewJsonContext.Default.ScrollMessage);
                if (scroll != null && _activeWindow is Window scrollWindow)
                {
                    WindowInputRouter.MouseWheel(scrollWindow,
                        new Point(scroll.X, scroll.Y), new Point(scroll.X, scroll.Y),
                        new Vector(scroll.DeltaX, scroll.DeltaY),
                        modifiers: PreviewInputMapper.MapModifiers(scroll.Modifiers));
                }
                break;

            case PreviewProtocol.KEY:
                var key = json.Deserialize(PreviewJsonContext.Default.KeyMessage);
                if (key != null && _activeWindow is Window keyWindow)
                {
                    HandleKey(keyWindow, key);
                }
                break;

            case PreviewProtocol.TEXT_INPUT:
                var text = json.Deserialize(PreviewJsonContext.Default.TextInputMessage);
                if (text != null && _activeWindow is Window textWindow)
                {
                    HandleTextInput(textWindow, text.Text);
                }
                break;

            default:
                // Unknown message ids are ignored for forward compatibility.
                break;
        }
    }

    private void HandlePointer(int typeId, PointerMessage pointer)
    {
        var window = _activeWindow;
        if (window == null)
        {
            return;
        }

        var position = new Point(pointer.X, pointer.Y);
        bool leftDown = (pointer.Buttons & 1) != 0;
        bool rightDown = (pointer.Buttons & 2) != 0;
        bool middleDown = (pointer.Buttons & 4) != 0;
        var modifiers = PreviewInputMapper.MapModifiers(pointer.Modifiers);

        try
        {
            if (typeId == PreviewProtocol.POINTER_MOVED)
            {
                WindowInputRouter.MouseMove(window, position, position, leftDown, rightDown, middleDown, modifiers);
            }
            else
            {
                WindowInputRouter.MouseButton(window, position, position,
                    PreviewInputMapper.MapButton(pointer.Button),
                    isDown: typeId == PreviewProtocol.POINTER_PRESSED,
                    leftDown, rightDown, middleDown,
                    Math.Max(1, pointer.ClickCount),
                    modifiers);
            }
        }
        catch (Exception ex)
        {
            SendStatus($"Input dispatch failed: {ex.Message}", hasError: true, exceptionDetail: ex.ToString());
        }
    }

    private void HandleKey(Window window, KeyMessage message)
    {
        var key = PreviewInputMapper.MapKey(message.Code);
        if (key == Key.None)
        {
            return;
        }

        var args = new KeyEventArgs(key, platformKey: 0, PreviewInputMapper.MapModifiers(message.Modifiers));
        if (message.IsDown)
        {
            WindowInputRouter.KeyDown(window, args);
        }
        else
        {
            WindowInputRouter.KeyUp(window, args);
        }
    }

    /// <summary>Mirrors the native WM_CHAR dispatch: preview text goes to the focused text client.</summary>
    private static void HandleTextInput(Window window, string text)
    {
        if (text.Length == 0)
        {
            return;
        }
        foreach (char character in text)
        {
            if (char.IsControl(character) && character != '\r' && character != '\t')
            {
                return;
            }
        }

        var args = new TextInputEventArgs(text);
        window.RaisePreviewTextInput(args);
        if (!args.Handled && window.FocusManager.FocusedElement is ITextInputClient client)
        {
            client.HandleTextInput(args);
        }
    }

    /// <summary>
    /// Adopts the IDE panel's size and DPI. Per plan.md 4.5, the viewport is a constraint for
    /// auto-sized component wrappers only; Window targets keep their own size logic and IDE
    /// zoom is purely a client-side display scale.
    /// </summary>
    private void ApplyClientMetrics(double width, double height, double dpi)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        if (dpi > 0)
        {
            _clientDpi = dpi;
        }

        var window = _activeWindow;
        if (window != null)
        {
            ApplyClientDpi(window);
            if (ReferenceEquals(window, _wrapperWindow) && _wrapperIsComponentHost)
            {
                ApplyWrapperWindowSize(window, window.Content as FrameworkElement);
            }
            MarkDirty(window);
        }
    }

    private void ApplyClientDpi(Window window)
    {
        uint dpi = (uint)Math.Round(_clientDpi);
        if (dpi == 0 || window.Dpi == dpi)
        {
            return;
        }

        uint oldDpi = window.Dpi;
        window.SetDpi(dpi);
        window.RaiseDpiChanged(oldDpi, dpi);
    }

    /// <summary>
    /// Sizes a component wrapper window: DesignSize hints win per axis; unhinted axes fit the
    /// content's desired size clamped to the IDE viewport.
    /// </summary>
    private void ApplyWrapperWindowSize(Window window, FrameworkElement? content)
    {
        double designWidth = double.NaN;
        double designHeight = double.NaN;
        if (content != null)
        {
            Design.TryGetDesignSize(content, out designWidth, out designHeight);
        }

        bool hasWidth = !double.IsNaN(designWidth) && designWidth > 0;
        bool hasHeight = !double.IsNaN(designHeight) && designHeight > 0;
        if (hasWidth && hasHeight)
        {
            window.WindowSize = WindowSize.Resizable(designWidth, designHeight);
        }
        else if (hasWidth)
        {
            window.WindowSize = WindowSize.FitContentHeight(designWidth, _viewportHeight);
        }
        else if (hasHeight)
        {
            window.WindowSize = WindowSize.FitContentWidth(_viewportWidth, designHeight);
        }
        else
        {
            window.WindowSize = WindowSize.FitContentSize(_viewportWidth, _viewportHeight);
        }
    }

    private void ApplyTheme(string mode)
    {
        var app = _app;
        if (app == null)
        {
            return;
        }

        ThemeVariant variant;
        if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase))
        {
            variant = ThemeVariant.Light;
        }
        else if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase))
        {
            variant = ThemeVariant.Dark;
        }
        else
        {
            variant = ThemeVariant.System;
        }

        try
        {
            app.SetTheme(variant);
            SendStatus($"Previewing {_activeTargetId}");
            if (_activeWindow != null)
            {
                MarkDirty(_activeWindow);
            }
        }
        catch (Exception ex)
        {
            SendStatus($"Theme switch failed: {ex.Message}", hasError: true, exceptionDetail: ex.ToString());
        }
    }

    private void SendTargets()
    {
        if (_targets == null)
        {
            long start = Stopwatch.GetTimestamp();
            _targets = PreviewTargetScanner.Scan();
            long elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency;
            PreviewTrace.Log($"target scan {elapsedMs}ms ({_targets.Count} targets)");
        }

        var infos = new PreviewTargetInfo[_targets.Count];
        for (int i = 0; i < _targets.Count; i++)
        {
            var target = _targets[i];
            infos[i] = new PreviewTargetInfo
            {
                Id = target.Id,
                DisplayName = target.DisplayName,
                Kind = target.Kind,
                Available = target.Available,
                Reason = target.UnavailableReason,
                SourcePath = target.SourcePath,
                SourceLine = target.SourceLine,
            };
        }

        _channel?.Send(PreviewProtocol.PREVIEW_TARGETS, new PreviewTargetsMessage
        {
            Targets = infos,
            ActiveId = _activeTargetId,
        }, PreviewJsonContext.Default.PreviewTargetsMessage);
    }

    private void SelectTarget(string id)
    {
        if (string.Equals(id, _activeTargetId, StringComparison.Ordinal))
        {
            return;
        }

        _targets ??= PreviewTargetScanner.Scan();
        var descriptor = _targets.Find(target => string.Equals(target.Id, id, StringComparison.Ordinal));
        if (descriptor == null)
        {
            SendStatus($"Unknown preview target: {id}", hasError: true);
            return;
        }
        if (!descriptor.Available)
        {
            SendStatus($"{id} cannot be previewed: {descriptor.UnavailableReason}", hasError: true);
            return;
        }

        var previousWrapper = _wrapperWindow;
        _wrapperWindow = null;

        try
        {
            if (string.Equals(id, PreviewTargetScanner.MAIN_WINDOW_ID, StringComparison.Ordinal))
            {
                _activeWindow = _mainWindow;
                if (_activeWindow != null)
                {
                    // The main window may have missed viewport DPI updates while another
                    // target was active; reapply on every activation.
                    ApplyClientDpi(_activeWindow);
                }
            }
            else
            {
                _activeWindow = CreateTargetWindow(descriptor);
            }

            _activeTargetId = id;
            SendStatus($"Previewing {id}");
            SendTargets();
            if (_activeWindow != null)
            {
                MarkDirty(_activeWindow);
            }
        }
        catch (Exception ex)
        {
            _activeWindow = _mainWindow;
            _activeTargetId = PreviewTargetScanner.MAIN_WINDOW_ID;
            SendStatus($"Failed to open target {id}: {ex.Message}", hasError: true, exceptionDetail: ex.ToString());
        }
        finally
        {
            previousWrapper?.Close();
        }
    }

    private Window CreateTargetWindow(PreviewTargetScanner.TargetDescriptor descriptor)
    {
        var instance = PreviewTargetScanner.CreateInstance(descriptor);

        Window window;
        if (instance is Window targetWindow)
        {
            // A real Window keeps its own size logic; a DesignSize hint overrides per axis.
            window = targetWindow;
            _wrapperIsComponentHost = false;
            window.Show();
            Design.TryGetDesignSize(window, out double designWidth, out double designHeight);
            if (designWidth > 0 || designHeight > 0)
            {
                var clientSize = window.ClientSize;
                window.SetClientSizeDip(
                    designWidth > 0 ? designWidth : clientSize.Width,
                    designHeight > 0 ? designHeight : clientSize.Height);
            }
        }
        else
        {
            window = new Window { Content = (Element)instance };
            _wrapperIsComponentHost = true;
            ApplyWrapperWindowSize(window, instance as FrameworkElement);
            window.Show();
        }

        ApplyClientDpi(window);
        _wrapperWindow = window;
        return window;
    }

    private void OnDeltaApplied()
    {
        long start = Stopwatch.GetTimestamp();

        // Deltas can add whole new types (no restart involved), so the cached target list would
        // silently miss controls created during the session.
        _targets = null;
        SendTargets();
        RebuildActiveTarget("delta");

        long elapsedMs = (Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency;
        PreviewTrace.Log($"delta handled in {elapsedMs}ms (rescan + rebuild)");
    }

    private void RebuildActiveTarget(string updateKind)
    {
        var window = _activeWindow;
        if (window == null)
        {
            return;
        }

        try
        {
            // Build ownership mirrors the framework rule: composition-site callback first,
            // then the window's virtual OnBuild hook.
            if (window.BuildCallback is Action<Window> build)
            {
                build(window);
            }
            else if (window.HasBuildHookRegistered)
            {
                window.InvokeOnBuildHook();
            }
            else if (!string.Equals(_activeTargetId, PreviewTargetScanner.MAIN_WINDOW_ID, StringComparison.Ordinal)
                && ReferenceEquals(window, _wrapperWindow) && window.Content != null)
            {
                // Component wrappers recreate the instance so helper-method edits are reflected
                // even when the OnBuild override itself did not change.
                _targets ??= PreviewTargetScanner.Scan();
                var descriptor = _targets.Find(target => string.Equals(target.Id, _activeTargetId, StringComparison.Ordinal));
                if (descriptor is { Type: not null, Available: true } && !typeof(Window).IsAssignableFrom(descriptor.Type))
                {
                    var instance = PreviewTargetScanner.CreateInstance(descriptor);
                    window.Content = (Element)instance;
                    ApplyWrapperWindowSize(window, instance as FrameworkElement);
                }
            }

            SendStatus($"Previewing {_activeTargetId}", updateKind: updateKind);
            MarkDirty(window);
        }
        catch (Exception ex)
        {
            SendStatus($"Rebuild failed: {ex.Message}", hasError: true, exceptionDetail: ex.ToString(), updateKind: updateKind);
        }
    }

    private void SendStatus(string message, bool hasError = false, string? exceptionDetail = null, string? updateKind = null)
        => _channel?.Send(PreviewProtocol.STATUS, new StatusMessage
        {
            Message = message,
            HasError = hasError,
            ExceptionDetail = exceptionDetail,
            UpdateKind = updateKind,
            ThemeMode = _app?.ThemeMode.ToString().ToLowerInvariant(),
        }, PreviewJsonContext.Default.StatusMessage);

    private void RunOnUiThread(Action action)
    {
        var dispatcher = _app?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        if (dispatcher.IsOnUIThread)
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
