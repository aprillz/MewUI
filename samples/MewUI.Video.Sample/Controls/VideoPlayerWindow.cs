using System.Diagnostics;
using System.Text;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Video.Sample.Decoding;
using Aprillz.MewUI.Video.Sample.Diagnostics;
using Aprillz.MewUI.Video.Sample.Playback;

namespace Aprillz.MewUI.Video.Sample.Controls;

/// <summary>
/// The playback window — owns the UI shell (path/open/checkboxes on top, seek slider /
/// play button at the bottom, video view + stats overlay in the middle) and the
/// <see cref="VideoPlayer"/> that drives it. Originally inlined in Program.cs; pulled out
/// so the program entry stays focused on bootstrapping (FFmpeg, backend registration,
/// process counter aggregation, log window).
/// </summary>
public sealed class VideoPlayerWindow : Window
{
    private TextBox _pathBox = null!;
    private TextBlock _statusText = null!;
    private TextBlock _backendText = null!;
    private Button _playPauseButton = null!;
    private Slider _seekSlider = null!;
    private VideoView _videoView = null!;
    private VideoPlayer _player = null!;
    private TextBlock _statsOverlayText = null!;
    private Border _statsOverlayBox = null!;
    private Canvas _statsOverlayCanvas = null!;
    private CheckBox _statsOverlayCheckBox = null!;
    private CheckBox _forceCpuReadbackCheckBox = null!;

    private readonly DispatcherTimer _uiTimer = new(TimeSpan.FromSeconds(1.0 / 60));
    private readonly Stopwatch _fpsStopwatch = new();
    private int _fpsFrames;
    private string _lastFpsText = "warming";

    private bool _suppressSeekSync;
    private bool _statsOverlayEnabled = true;
    private bool _isStatsOverlayDragging;
    private Point _statsOverlayDragOffset;
    private string? _lastRenderLoopStateLog;
    private string _lastSetStatsText = "";
    private string _cachedStatsOverlayText = "ffmpeg";
    private long _nextStatsOverlayUpdateTicks;

    private readonly ObservableValue<int> _uiPositionIntValue = new(0);
    private readonly ObservableValue<TimeSpan> _uiPositionValue = new(TimeSpan.Zero);
    private readonly ObservableValue<TimeSpan> _uiDurationValue = new(TimeSpan.Zero);
    private readonly ObservableValue<bool> _uiIsPlayingValue = new(false);

    private readonly string? _startupPath;

    /// <summary>The active <see cref="VideoPlayer"/>. Exposed so host code can subscribe to
    /// playback events or read the backing <see cref="VideoPlayback"/> directly.</summary>
    public VideoPlayer Player => _player;

    /// <summary>Latest process-level stats pushed by the host's counter aggregator. The
    /// window's per-second overlay rebuild reads these on each tick. Cross-thread writes are
    /// safe (reference-type field reads are atomic; the record value is immutable).</summary>
    public HostStats Stats { get; set; } = HostStats.Empty;

    /// <summary>
    /// Construct the playback window. <paramref name="startupPath"/> is auto-loaded on
    /// <c>OnLoaded</c> when non-empty.
    /// </summary>
    public VideoPlayerWindow(string? startupPath = null)
    {
        _startupPath = startupPath;
        WindowSize = WindowSize.Resizable(1280, 820);
        StartupLocation = WindowStartupLocation.CenterScreen;
        Padding = new Thickness(0);
        Title = "Aprillz.MewUI Video Sample";
        Content = BuildContent();

        Loaded += OnLoadedInternal;
        Closed += OnClosedInternal;
        FrameRendered += OnFrameRenderedInternal;
    }

    private FrameworkElement BuildContent() => new DockPanel()
        .Children(
            BuildTopBar()
                .DockTop(),

            BuildBottomBar()
                .DockBottom(),

            new Grid()
                .Children(
                    new VideoView()
                        .Ref(out _videoView),

                    new Canvas()
                        .Ref(out _statsOverlayCanvas)
                        .Children(
                            new Border()
                                .Ref(out _statsOverlayBox)
                                .Padding(10, 8)
                                .CornerRadius(6)
                                .Background(new Color(170, 0, 0, 0))
                                .OnMouseDown(BeginStatsOverlayDrag)
                                .OnMouseMove(DragStatsOverlay)
                                .OnMouseUp(EndStatsOverlayDrag)
                                .Child(
                                    new TextBlock()
                                        .Ref(out _statsOverlayText)
                                        .Text("ffmpeg")
                                        .FontFamily("Consolas")
                                        .FontSize(14)
                                        .Foreground(Color.White)
                                        .TextWrapping(TextWrapping.Wrap)
                                        .Width(480)
                                )
                        )
                )
        );

    private FrameworkElement BuildTopBar() => new Border()
        .Padding(12, 10)
        .BorderThickness(1)
        .WithTheme((theme, border) =>
        {
            border.Background(theme.Palette.ContainerBackground);
            border.BorderBrush(theme.Palette.ControlBorder);
        })
        .Child(
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new DockPanel()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                                .Ref(out _backendText)
                                .DockRight(),

                            new CheckBox()
                                .Ref(out _statsOverlayCheckBox)
                                .Content("Stats")
                                .DockRight()
                                .CenterVertical(),

                            new CheckBox()
                                .Ref(out _forceCpuReadbackCheckBox)
                                .Content("Force CPU")
                                .DockRight()
                                .CenterVertical(),

                            new Button()
                                .Content("Open...")
                                .DockRight()
                                .OnClick(OpenVideoFile),

                            new TextBox()
                                .Ref(out _pathBox)
                                .Placeholder("Select a local video file...")
                                .StretchHorizontal()
                        ),

                    new TextBlock()
                        .Ref(out _statusText)
                        .Text("Ready")
                        .FontSize(11)
                        .TextWrapping(TextWrapping.Wrap)
                )
        );

    private FrameworkElement BuildBottomBar() => new Border()
        .Padding(12, 10)
        .BorderThickness(1)
        .WithTheme((theme, border) =>
        {
            border.Background(theme.Palette.ContainerBackground);
            border.BorderBrush(theme.Palette.ControlBorder);
        })
        .Child(
            new DockPanel()
                .Spacing(12)
                .Children(
                    new Button()
                        .Ref(out _playPauseButton)
                        .BindContent(_uiIsPlayingValue, x => x ? "Pause" : "Play")
                        .Width(96)
                        .OnClick(TogglePlayback),

                    new StackPanel()
                        .Horizontal()
                        .Spacing(4)
                        .DockRight()
                        .CenterVertical()
                        .MinWidth(150)
                        .Children(
                            new TextBlock()
                                .BindText(_uiPositionIntValue, x => TimeSpan.FromSeconds(x).ToString(@"hh\:mm\:ss")),
                            new TextBlock()
                                .BindText(_uiDurationValue, x => x.ToString(@"\/\ hh\:mm\:ss"))
                        ),

                    new Slider()
                        .Ref(out _seekSlider)
                        .Minimum(0)
                        .Maximum(1)
                        .Value(0)
                        .SmallChange(0.1)
                        .StretchHorizontal()
                        .OnMouseDown(_ => _player?.BeginSeekDrag())
                        .OnMouseUp(_ => _player?.EndSeekDrag())
                        .OnValueChanged(OnSeekChanged)
                )
        );

    private void OnLoadedInternal()
    {
        SampleLog.Write("VideoPlayerWindow loaded.");
        _player = new VideoPlayer(this, _videoView);
        _player.StatusChanged += text => _statusText.Text = text;
        _player.LoadingStateChanged += isLoading =>
        {
            if (isLoading)
            {
                _playPauseButton.Content("Loading...");
            }
            else if (_player.Playback is null)
            {
                _playPauseButton.Content("Play");
            }
        };
        _player.PlaybackReplaced += OnPlaybackReplaced;

        UpdateBackendText();
        SetStatsOverlayPosition(12, 12);
        _uiTimer.Tick += UpdatePlaybackUi;
        _uiTimer.Start();

        _statsOverlayCheckBox.IsChecked = _statsOverlayEnabled;
        _statsOverlayCheckBox.CheckedChanged += OnStatsOverlayToggled;

        _forceCpuReadbackCheckBox.IsChecked = false;
        _forceCpuReadbackCheckBox.CheckedChanged += OnForceCpuReadbackToggled;

        if (!string.IsNullOrWhiteSpace(_startupPath))
        {
            _pathBox.Text = _startupPath;
            _ = _player.LoadAsync(_startupPath);
        }
        else
        {
            _statusText.Text = "Open a local video file to start playback.";
            SampleLog.Write("No startup path was provided.");
        }
    }

    private void OnClosedInternal()
    {
        SampleLog.Write("VideoPlayerWindow closing.");
        _uiTimer.Dispose();
        _player?.Dispose();
    }

    private void OnFrameRenderedInternal()
    {
        if (!_fpsStopwatch.IsRunning)
        {
            _fpsStopwatch.Restart();
            _fpsFrames = 0;
            return;
        }

        _fpsFrames++;
        UpdateFpsCounter();
    }

    private void OpenVideoFile()
    {
        SampleLog.Write("Opening file dialog.");
        var file = FileDialog.OpenFile(new OpenFileDialogOptions
        {
            Owner = Handle,
            Filter = "Video Files (*.mp4;*.mkv;*.avi;*.mov;*.webm)|*.mp4;*.mkv;*.avi;*.mov;*.webm|All Files (*.*)|*.*"
        });

        if (string.IsNullOrWhiteSpace(file))
        {
            SampleLog.Write("File dialog canceled.");
            _statusText.Text = "Open canceled.";
            return;
        }

        SampleLog.Write($"Selected file: {file}");
        _pathBox.Text = file;
        _ = _player.LoadAsync(file);
    }

    private void TogglePlayback()
    {
        if (_player.Playback is null && !_player.IsLoading)
        {
            SampleLog.Write("TogglePlayback requested with no playback. Falling back to LoadAsync.");
            _ = _player.LoadAsync(_pathBox.Text);
            return;
        }

        _player.TogglePlayPause();
    }

    private void OnPlaybackReplaced()
    {
        ResetFpsStats();

        if (_player.Playback is null)
        {
            _suppressSeekSync = true;
            _seekSlider.Maximum = 1;
            _seekSlider.Value = 0;
            _suppressSeekSync = false;
            _playPauseButton.Content("Play");
            _statsOverlayText.Text = "ffmpeg";
            _cachedStatsOverlayText = "ffmpeg";
            _nextStatsOverlayUpdateTicks = 0;
            return;
        }

        _suppressSeekSync = true;
        _seekSlider.Maximum = Math.Max(1, _player.Playback.Duration.TotalSeconds);
        _seekSlider.Value = 0;
        _suppressSeekSync = false;
    }

    private void OnSeekChanged(double value)
    {
        if (_suppressSeekSync || _player?.Playback is null)
        {
            return;
        }

        _player.OnSeekValueChanged(value);
    }

    private void BeginStatsOverlayDrag(MouseEventArgs e)
    {
        if (e.Button != MouseButton.Left)
        {
            return;
        }

        double currentLeft = Canvas.GetLeft(_statsOverlayBox);
        double currentTop = Canvas.GetTop(_statsOverlayBox);
        if (double.IsNaN(currentLeft))
        {
            currentLeft = 12;
        }

        if (double.IsNaN(currentTop))
        {
            currentTop = 12;
        }

        Point pointer = e.GetPosition(_statsOverlayCanvas);
        _statsOverlayDragOffset = new Point(pointer.X - currentLeft, pointer.Y - currentTop);
        _isStatsOverlayDragging = true;
        CaptureMouse(_statsOverlayBox);
        e.Handled = true;
    }

    private void DragStatsOverlay(MouseEventArgs e)
    {
        if (!_isStatsOverlayDragging)
        {
            return;
        }

        Point pointer = e.GetPosition(_statsOverlayCanvas);
        SetStatsOverlayPosition(pointer.X - _statsOverlayDragOffset.X, pointer.Y - _statsOverlayDragOffset.Y);
        e.Handled = true;
    }

    private void EndStatsOverlayDrag(MouseEventArgs e)
    {
        if (!_isStatsOverlayDragging || e.Button != MouseButton.Left)
        {
            return;
        }

        _isStatsOverlayDragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void SetStatsOverlayPosition(double left, double top)
    {
        double maxLeft = Math.Max(0, _statsOverlayCanvas.Bounds.Width - _statsOverlayBox.Bounds.Width);
        double maxTop = Math.Max(0, _statsOverlayCanvas.Bounds.Height - _statsOverlayBox.Bounds.Height);
        Canvas.SetLeft(_statsOverlayBox, Math.Clamp(left, 0, maxLeft));
        Canvas.SetTop(_statsOverlayBox, Math.Clamp(top, 0, maxTop));
    }

    private void OnStatsOverlayToggled(bool? isChecked)
    {
        _statsOverlayEnabled = isChecked == true;
        if (_statsOverlayCanvas is not null)
        {
            // Hiding the canvas removes the overlay from layout/render entirely. Combined with
            // stopping the timer below, redraws caused by stats text mutation drop to zero.
            _statsOverlayCanvas.IsVisible = _statsOverlayEnabled;
        }

        // Reset diagnostic counters at the toggle boundary so the user can directly compare
        // "stats on" vs "stats off" fps / VideoView redraw rates from the next sampling
        // window onwards.
        _videoView?.ResetOnRenderCallCount();
        _fpsFrames = 0;
        if (_fpsStopwatch.IsRunning)
        {
            _fpsStopwatch.Restart();
        }
    }

    private void OnForceCpuReadbackToggled(bool? isChecked)
    {
        _player?.SetForceCpuReadback(isChecked == true);
    }

    private void UpdatePlaybackUi()
    {
        if (_player is null)
        {
            return;
        }

        var playback = _player.Playback;
        if (playback is null)
        {
            _uiIsPlayingValue.Value = false;
            _uiDurationValue.Value = TimeSpan.Zero;
            _uiPositionValue.Value = TimeSpan.Zero;
            _uiPositionIntValue.Value = 0;
            return;
        }

        long nowTicks = Stopwatch.GetTimestamp();

        if (_statsOverlayEnabled && nowTicks >= _nextStatsOverlayUpdateTicks)
        {
            _cachedStatsOverlayText = BuildStatsOverlayText(playback);
            _nextStatsOverlayUpdateTicks = nowTicks + Stopwatch.Frequency;
        }

        if (_statsOverlayEnabled)
        {
            if (!string.Equals(_cachedStatsOverlayText, _lastSetStatsText, StringComparison.Ordinal))
            {
                _lastSetStatsText = _cachedStatsOverlayText;
            }
            _statsOverlayText.Text = _cachedStatsOverlayText;
        }

        var position = playback.Position;
        var duration = playback.Duration;

        _uiIsPlayingValue.Value = playback.IsPlaying;
        _uiPositionValue.Value = playback.Position;
        _uiDurationValue.Value = playback.Duration;
        _uiPositionIntValue.Value = (int)_uiPositionValue.Value.TotalSeconds;

        if (!_player.IsSeekDragActive)
        {
            _suppressSeekSync = true;
            _seekSlider.Maximum = Math.Max(1, duration.TotalSeconds);
            _seekSlider.Value = Math.Clamp(position.TotalSeconds, 0, _seekSlider.Maximum);
            _suppressSeekSync = false;
        }

        UpdateRenderLoopLog();

        if (playback.IsEnded)
        {
            _statusText.Text = $"Finished: {Path.GetFileName(playback.SourcePath)}";
        }
    }

    private void UpdateRenderLoopLog()
    {
        if (!Application.IsRunning)
        {
            return;
        }

        string renderLoopState = GetRenderLoopStatsText();
        if (!string.Equals(_lastRenderLoopStateLog, renderLoopState, StringComparison.Ordinal))
        {
            _lastRenderLoopStateLog = renderLoopState;
            SampleLog.Write($"Render loop: {renderLoopState}");
        }
    }

    private void UpdateBackendText()
    {
        _backendText.Text = $"Backend: {GraphicsFactory.Backend}";
        SampleLog.Write(_backendText.Text);
    }

    private string BuildStatsOverlayText(VideoPlayback playback)
    {
        var stats = Stats;

        var builder = new StringBuilder();
        builder.Append(playback.DecoderStatsOverlayText);
        builder.Append("\nfps: ").Append(_lastFpsText);
        builder.Append("\nrender loop: ").Append(GetRenderLoopStatsText());
        builder.Append("\npresent: ").Append(playback.PresentationTimingStatsText);
        builder.Append("\npresent path test: ").Append(_videoView.PresentationPathText);
        builder.Append("\nprocess");
        builder.Append("\ncpu: ").Append(stats.CpuUsageText);
        builder.Append("\nworking set: ").Append(FormatBytes(stats.WorkingSetBytes));
        builder.Append("\nprivate bytes: ").Append(FormatBytes(stats.PrivateBytes));
        builder.Append("\nmanaged heap: ").Append(FormatBytes(stats.ManagedHeapBytes));
        builder.Append("\ngpu: ").Append(stats.GpuUsageText);

        if (OperatingSystem.IsMacOS())
        {
            builder.Append("\n\ngpu");
            builder.Append("\nmetal alloc: ").Append(stats.MetalGpuBytes > 0 ? FormatBytes((long)stats.MetalGpuBytes) : "pending");
        }
        else if (OperatingSystem.IsWindows() && playback.D3D11Device != 0)
        {
            builder.Append("\n\ngpu");
            AppendGpuMemoryStats(builder, playback.D3D11Device, "local", D3D11Native.DXGI_MEMORY_SEGMENT_GROUP_LOCAL);
            AppendGpuMemoryStats(builder, playback.D3D11Device, "non-local", D3D11Native.DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL);
        }

        return builder.ToString();
    }

    private static void AppendGpuMemoryStats(StringBuilder builder, nint d3d11Device, string label, uint segmentGroup)
    {
        if (!D3D11Native.TryQueryVideoMemoryInfo(d3d11Device, segmentGroup, out var info))
        {
            builder.Append('\n').Append(label).Append(": unavailable");
            return;
        }

        builder.Append('\n').Append(label).Append(": ")
            .Append(FormatBytes((long)info.CurrentUsage))
            .Append(" / ")
            .Append(FormatBytes((long)info.Budget));
    }

    private static string GetRenderLoopStatsText()
    {
        if (!Application.IsRunning)
        {
            return "inactive";
        }

        var settings = Application.Current.RenderLoopSettings;
        string target = settings.TargetFps > 0 ? settings.TargetFps.ToString() : "uncapped";
        return $"mode={settings.Mode}, continuous={settings.IsContinuous}, vsync={(settings.VSyncEnabled ? "on" : "off")}, target={target}";
    }

    private void ResetFpsStats()
    {
        _fpsStopwatch.Reset();
        _fpsFrames = 0;
        _lastFpsText = "warming";
    }

    private void UpdateFpsCounter()
    {
        double elapsed = _fpsStopwatch.Elapsed.TotalSeconds;
        if (elapsed < 1.0)
        {
            return;
        }

        _lastFpsText = $"{(_fpsFrames <= 1 ? 0 : _fpsFrames) / elapsed:0.0}";
        _fpsFrames = 0;
        _fpsStopwatch.Restart();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.000} {units[unitIndex]}";
    }
}

/// <summary>
/// Process-level statistics snapshot produced by the host's counter aggregation loop and
/// pushed into <see cref="VideoPlayerWindow.Stats"/> for the overlay text rebuild. Kept
/// as an immutable record so cross-thread assignment is safe (reference write is atomic).
/// </summary>
public readonly record struct HostStats(
    string CpuUsageText,
    string GpuUsageText,
    long WorkingSetBytes,
    long PrivateBytes,
    long ManagedHeapBytes,
    ulong MetalGpuBytes)
{
    public static HostStats Empty { get; } = new(
        CpuUsageText: "warming",
        GpuUsageText: OperatingSystem.IsWindows() ? "warming" : "n/a",
        WorkingSetBytes: 0,
        PrivateBytes: 0,
        ManagedHeapBytes: 0,
        MetalGpuBytes: 0);
}
