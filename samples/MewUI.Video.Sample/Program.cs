using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Video.Sample.Controls;
using Aprillz.MewUI.Video.Sample.Decoding;
using Aprillz.MewUI.Video.Sample.Diagnostics;
using Aprillz.MewUI.Video.Sample.Playback;

using FFmpeg.AutoGen;

using DynamicBindings = FFmpeg.AutoGen.Bindings.DynamicallyLoaded.DynamicallyLoadedBindings;

#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]
#endif

Startup();

Window window = null!;
Window logWindow = null!;
TextBox pathBox = null!;
MultiLineTextBox logTextBox = null!;
TextBlock statusText = null!;
TextBlock backendText = null!;
TextBlock statsOverlayText = null!;
Border statsOverlayBox = null!;
Canvas statsOverlayCanvas = null!;
CheckBox statsOverlayCheckBox = null!;
CheckBox forceCpuReadbackCheckBox = null!;
bool statsOverlayEnabled = true;
string lastSetStatsText = "";
// UI-side observables. UpdatePlaybackUi pushes current playback state into these on
// each tick; bindings on TextBlock.Text / Slider.Value / Button.Content fire only when
// ObservableValue.Set actually changes the value. Same-value pushes short-circuit, so
// a paused/idle playback drives no UI invalidation even though the timer still ticks.
ObservableValue<int> uiPositionIntValue = new(0);
ObservableValue<TimeSpan> uiPositionValue = new(TimeSpan.Zero);
ObservableValue<TimeSpan> uiDurationValue = new(TimeSpan.Zero);
ObservableValue<bool> uiIsPlayingValue = new(false);
Button playPauseButton = null!;
Slider seekSlider = null!;
VideoView videoView = null!;

VideoPlayback? playback = null;
DispatcherTimer uiTimer = new(TimeSpan.FromSeconds(1.0 / 60));
bool suppressSeekSync = false;
bool isSeekDragActive = false;
int loadRequestId = 0;
bool isLoading = false;
double pendingDragSeekSeconds = -1;
bool dragPreviewSeekInFlight = false;
bool isStatsOverlayDragging = false;
Point statsOverlayDragOffset = default;
Process currentProcess = Process.GetCurrentProcess();
TimeSpan lastCpuTime = currentProcess.TotalProcessorTime;
long lastCpuSampleTicks = Stopwatch.GetTimestamp();
double lastCpuPercent = 0;
double lastCpuAveragePercent = 0;
double lastCpuMinPercent = 0;
double lastCpuMaxPercent = 0;
double intervalCpuSeconds = 0;
double intervalElapsedSeconds = 0;
double intervalCpuMinPercent = double.PositiveInfinity;
double intervalCpuMaxPercent = 0;
List<PerformanceCounter> gpuEngineCounters = [];
bool gpuCountersPrimed = false;
double lastGpuPercent = 0;
Stopwatch fpsStopwatch = new();
int fpsFrames = 0;
object counterSnapshotGate = new();
CancellationTokenSource? counterAggregationCts = null;
Task? counterAggregationTask = null;
bool counterResetRequested = false;
HashSet<string> loggedCounterErrors = new();
string cachedCpuUsageText = "warming";
string cachedGpuUsageText = OperatingSystem.IsWindows() ? "warming" : "n/a";
long cachedWorkingSetBytes = 0;
long cachedPrivateBytes = 0;
long cachedManagedHeapBytes = 0;
ulong cachedMetalGpuBytes = 0;
string lastFpsText = "warming";
string cachedStatsOverlayText = "ffmpeg";
long nextStatsOverlayUpdateTicks = 0;
string? lastRenderLoopStateLog = null;

SampleLog.LineAppended += AppendLogLine;

string? startupPath = Environment.GetCommandLineArgs()
    .Skip(1)
    .FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));

if (Debugger.IsAttached && string.IsNullOrWhiteSpace(startupPath))
{
    string[] debugCandidates = OperatingSystem.IsMacOS()
        ? ["/Users/al6uiz/Desktop/Screen Recording 2026-03-10 at 5.25.50 PM.mov"]
        : [@"E:\Downloads\hevc_4k60P_main_dji_mavic3.mov"];

    startupPath = debugCandidates.FirstOrDefault(File.Exists);
}

Application.DispatcherUnhandledException += e =>
{
    SampleLog.Write($"DispatcherUnhandledException: {e.Exception}");
    Console.WriteLine(e.Exception.ToString());
    statusText?.Text($"Error: {e.Exception.Message}");
    e.Handled = true;
};

var root = new Window()
    .Resizable(1280, 820)
    .StartCenterScreen()
    .OnBuild(x => x
        .Ref(out window)
        .Padding(0)
        .Title("Aprillz.MewUI Video Sample")
        .Content(
            new DockPanel()
                .Children(
                    TopBar()
                        .DockTop(),

                    BottomBar()
                        .DockBottom(),

                    new Grid()
                        .Children(
                            new VideoView()
                                .Ref(out videoView),

                            new Canvas()
                                .Ref(out statsOverlayCanvas)
                                .Children(
                                    new Border()
                                        .Ref(out statsOverlayBox)
                                        .Padding(10, 8)
                                        .CornerRadius(6)
                                        .Background(new Color(170, 0, 0, 0))
                                        .OnMouseDown(BeginStatsOverlayDrag)
                                        .OnMouseMove(DragStatsOverlay)
                                        .OnMouseUp(EndStatsOverlayDrag)
                                        .Child(
                                            new TextBlock()
                                                .Ref(out statsOverlayText)
                                                .Text("ffmpeg")
                                                .FontFamily("Consolas")
                                                .FontSize(14)
                                                .Foreground(Color.White)
                                                .TextWrapping(TextWrapping.Wrap)
                                                .Width(480)
                                        )
                                )
                        )
                )
        )
        .OnLoaded(() =>
        {
            SampleLog.Write("Main window loaded.");
            EnsureLogWindow();
            UpdateBackendText();
            StartCounterAggregation();
            SetStatsOverlayPosition(12, 12);
            uiTimer.Tick += UpdatePlaybackUi;
            uiTimer.Start();

            statsOverlayCheckBox.IsChecked = statsOverlayEnabled;
            statsOverlayCheckBox.CheckedChanged += OnStatsOverlayToggled;

            forceCpuReadbackCheckBox.IsChecked = false;
            forceCpuReadbackCheckBox.CheckedChanged += OnForceCpuReadbackToggled;

            if (!string.IsNullOrWhiteSpace(startupPath))
            {
                pathBox.Text = startupPath;
                BeginLoadVideo(startupPath);
            }
            else
            {
                statusText.Text = "Open a local video file to start playback.";
                SampleLog.Write("No startup path was provided.");
            }
        })
        .OnClosed(() =>
        {
            SampleLog.Write("Main window closing.");
            StopCounterAggregation();
            uiTimer.Dispose();
            ReplacePlayback(null);
            logWindow?.Close();
        })
        .OnFrameRendered(() =>
        {
            if (!fpsStopwatch.IsRunning)
            {
                fpsStopwatch.Restart();
                fpsFrames = 0;
                return;
            }

            fpsFrames++;
            UpdateFpsCounter(ref fpsFrames);
        })
    );

Application.Run(root);

FrameworkElement TopBar() => new Border()
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
                            .Ref(out backendText)
                            .DockRight(),

                        new CheckBox()
                            .Ref(out statsOverlayCheckBox)
                            .Content("Stats")
                            .DockRight()
                            .CenterVertical(),

                        new CheckBox()
                            .Ref(out forceCpuReadbackCheckBox)
                            .Content("Force CPU")
                            .DockRight()
                            .CenterVertical(),

                        new Button()
                            .Content("Open...")
                            .DockRight()
                            .OnClick(OpenVideoFile),

                        new TextBox()
                            .Ref(out pathBox)
                            .Placeholder("Select a local video file...")
                            .StretchHorizontal()
                    ),

                new TextBlock()
                    .Ref(out statusText)
                    .Text("Ready")
                    .FontSize(11)
                    .TextWrapping(TextWrapping.Wrap)
            )
    );

FrameworkElement BottomBar() => new Border()
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
                    .Ref(out playPauseButton)
                    .BindContent(uiIsPlayingValue, x => x ? "Pause" : "Play")
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
                            .BindText(uiPositionIntValue, x => TimeSpan.FromSeconds(x).ToString(@"hh\:mm\:ss")),
                        new TextBlock()
                            .BindText(uiDurationValue, x => x.ToString(@"\/\ hh\:mm\:ss"))
                    ),
                new Slider()
                    .Ref(out seekSlider)
                    .Minimum(0)
                    .Maximum(1)
                    .Value(0)
                    .SmallChange(0.1)
                    .StretchHorizontal()
                    .OnMouseDown(_ => BeginSeekDrag())
                    .OnMouseUp(_ => EndSeekDrag())
                    .OnValueChanged(OnSeekChanged)
            )
    );

void OpenVideoFile()
{
    SampleLog.Write("Opening file dialog.");
    var file = FileDialog.OpenFile(new OpenFileDialogOptions
    {
        Owner = window.Handle,
        Filter = "Video Files (*.mp4;*.mkv;*.avi;*.mov;*.webm)|*.mp4;*.mkv;*.avi;*.mov;*.webm|All Files (*.*)|*.*"
    });

    if (string.IsNullOrWhiteSpace(file))
    {
        SampleLog.Write("File dialog canceled.");
        statusText.Text = "Open canceled.";
        return;
    }

    SampleLog.Write($"Selected file: {file}");
    pathBox.Text = file;
    BeginLoadVideo(file);
}

void BeginLoadVideo(string? path)
{
    _ = LoadVideoAsync(path, Interlocked.Increment(ref loadRequestId));
}

async Task LoadVideoAsync(string? path, int requestId)
{
    SampleLog.Write($"LoadVideo called. path={path ?? "<null>"}, requestId={requestId}");
    IBusyIndicator? busyIndicator = null;

    if (string.IsNullOrWhiteSpace(path))
    {
        SampleLog.Write("LoadVideo aborted: path is empty.");
        statusText.Text = "Path is empty.";
        return;
    }

    if (!File.Exists(path))
    {
        SampleLog.Write("LoadVideo aborted: file does not exist.");
        statusText.Text = $"File not found: {path}";
        return;
    }

    isLoading = true;
    playPauseButton.Content("Loading...");
    statusText.Text = $"Loading: {Path.GetFileName(path)}";
    busyIndicator = window.CreateBusyIndicator($"Loading {Path.GetFileName(path)}...");

    try
    {
        SampleLog.Write("Creating VideoPlayback on background thread.");
        nint preferredD3D11Device = window.GraphicsFactory is Direct2DGraphicsFactory d2dFactory
            ? d2dFactory.NativeD3D11Device
            : 0;
        var newPlayback = await Task.Run(() => new VideoPlayback(path, preferredD3D11Device));

        if (requestId != Volatile.Read(ref loadRequestId))
        {
            SampleLog.Write($"Discarding stale load result. requestId={requestId}");
            newPlayback.Dispose();
            return;
        }

        ReplacePlayback(newPlayback);
        SampleLog.Write($"Playback created. size={newPlayback.PixelWidth}x{newPlayback.PixelHeight}, duration={newPlayback.Duration}");
        newPlayback.Play();
        SampleLog.Write("Playback.Play invoked.");
        videoView.InvalidateVisual();
        UpdatePlaybackRenderLoopMode();

        suppressSeekSync = true;
        seekSlider.Maximum = Math.Max(1, newPlayback.Duration.TotalSeconds);
        seekSlider.Value = 0;
        suppressSeekSync = false;

        playPauseButton.Content("Pause");
        statusText.Text = $"Loaded: {Path.GetFileName(path)} ({newPlayback.PixelWidth}x{newPlayback.PixelHeight})";
        SampleLog.Write("LoadVideo completed successfully.");
        UpdatePlaybackUi();
    }
    catch (DllNotFoundException ex)
    {
        SampleLog.Write($"DllNotFoundException: {ex}");
        statusText.Text = "FFmpeg native DLLs were not found. See samples/MewUI.Video.Sample/README.md.";
        Console.WriteLine(ex.ToString());
    }
    catch (Exception ex)
    {
        SampleLog.Write($"LoadVideo failed: {ex}");
        statusText.Text = $"Failed to load video: {ex.Message}";
    }
    finally
    {
        busyIndicator?.Dispose();

        if (requestId == Volatile.Read(ref loadRequestId))
        {
            isLoading = false;
            if (playback is null)
            {
                playPauseButton.Content("Play");
            }
        }
    }
}

void ReplacePlayback(VideoPlayback? newPlayback)
{
    ResetDragPreviewState();
    SampleLog.Write($"ReplacePlayback: {(newPlayback is null ? "clear" : Path.GetFileName(newPlayback.SourcePath))}");
    var oldPlayback = playback;
    if (oldPlayback is not null)
    {
        oldPlayback.FrameReady -= OnPlaybackFrameReadyForDragPreview;
    }

    playback = newPlayback;
    videoView.Playback = newPlayback;
    if (newPlayback is not null)
    {
        newPlayback.FrameReady += OnPlaybackFrameReadyForDragPreview;
    }

    ResetCpuStats();
    ResetGpuStats();
    ResetFpsStats();

    oldPlayback?.Dispose();
    UpdatePlaybackRenderLoopMode();

    if (newPlayback is null)
    {
        suppressSeekSync = true;
        seekSlider.Maximum = 1;
        seekSlider.Value = 0;
        suppressSeekSync = false;
        playPauseButton.Content("Play");
        statsOverlayText.Text = "ffmpeg";
        cachedStatsOverlayText = "ffmpeg";
        nextStatsOverlayUpdateTicks = 0;
    }
}


void TogglePlayback()
{
    if (playback is null)
    {
        if (isLoading)
        {
            SampleLog.Write("TogglePlayback ignored while load is in progress.");
            statusText.Text = "Loading video...";
            return;
        }

        SampleLog.Write("TogglePlayback requested with no playback. Falling back to LoadVideo.");
        BeginLoadVideo(pathBox.Text);
        return;
    }

    if (playback.IsPlaying)
    {
        SampleLog.Write("Playback paused by user.");
        playback.Pause();
        playPauseButton.Content("Play");
        statusText.Text = "Paused.";
        UpdatePlaybackRenderLoopMode();
    }
    else
    {
        ResetDragPreviewState();
        SampleLog.Write("Playback resumed by user.");
        playback.Play();
        playPauseButton.Content("Pause");
        statusText.Text = $"Playing: {Path.GetFileName(playback.SourcePath)}";
        videoView.InvalidateVisual();
        UpdatePlaybackRenderLoopMode();
    }
}



void OnSeekChanged(double value)
{
    if (suppressSeekSync || playback is null)
    {
        return;
    }

    if (isSeekDragActive && !playback.IsPlaying)
    {
        pendingDragSeekSeconds = value;
        TryStartDragPreviewSeek(playback);
        statusText.Text = $"Preview: {FormatTime(TimeSpan.FromSeconds(value))}";
        return;
    }

    ResetDragPreviewState();
    playback.Seek(TimeSpan.FromSeconds(value));
    videoView.InvalidateVisual();

    if (!isSeekDragActive)
    {
        statusText.Text = $"Seek: {FormatTime(TimeSpan.FromSeconds(value))}";
    }
}

void BeginSeekDrag()
{
    isSeekDragActive = true;
}

void EndSeekDrag()
{
    isSeekDragActive = false;

    if (playback is null || playback.IsPlaying || pendingDragSeekSeconds < 0)
    {
        return;
    }

    double targetSeconds = pendingDragSeekSeconds;
    ResetDragPreviewState();
    playback.Seek(TimeSpan.FromSeconds(targetSeconds));
    videoView.InvalidateVisual();
    statusText.Text = $"Seek: {FormatTime(TimeSpan.FromSeconds(targetSeconds))}";
}

void TryStartDragPreviewSeek(VideoPlayback targetPlayback)
{
    if (dragPreviewSeekInFlight || !isSeekDragActive || targetPlayback.IsPlaying || pendingDragSeekSeconds < 0)
    {
        return;
    }

    double targetSeconds = pendingDragSeekSeconds;
    pendingDragSeekSeconds = -1;
    dragPreviewSeekInFlight = true;
    targetPlayback.Seek(TimeSpan.FromSeconds(targetSeconds));
}

void ResetDragPreviewState()
{
    dragPreviewSeekInFlight = false;
    pendingDragSeekSeconds = -1;
}

void BeginStatsOverlayDrag(MouseEventArgs e)
{
    if (e.Button != MouseButton.Left)
    {
        return;
    }

    double currentLeft = Canvas.GetLeft(statsOverlayBox);
    double currentTop = Canvas.GetTop(statsOverlayBox);
    if (double.IsNaN(currentLeft))
    {
        currentLeft = 12;
    }

    if (double.IsNaN(currentTop))
    {
        currentTop = 12;
    }

    Point pointer = e.GetPosition(statsOverlayCanvas);
    statsOverlayDragOffset = new Point(pointer.X - currentLeft, pointer.Y - currentTop);
    isStatsOverlayDragging = true;

    if (window is not null)
    {
        window.CaptureMouse(statsOverlayBox);
    }

    e.Handled = true;
}

void DragStatsOverlay(MouseEventArgs e)
{
    if (!isStatsOverlayDragging)
    {
        return;
    }

    Point pointer = e.GetPosition(statsOverlayCanvas);
    SetStatsOverlayPosition(pointer.X - statsOverlayDragOffset.X, pointer.Y - statsOverlayDragOffset.Y);
    e.Handled = true;
}

void EndStatsOverlayDrag(MouseEventArgs e)
{
    if (!isStatsOverlayDragging || e.Button != MouseButton.Left)
    {
        return;
    }

    isStatsOverlayDragging = false;
    if (window is not null)
    {
        window.ReleaseMouseCapture();
    }

    e.Handled = true;
}

void SetStatsOverlayPosition(double left, double top)
{
    double maxLeft = Math.Max(0, statsOverlayCanvas.Bounds.Width - statsOverlayBox.Bounds.Width);
    double maxTop = Math.Max(0, statsOverlayCanvas.Bounds.Height - statsOverlayBox.Bounds.Height);
    Canvas.SetLeft(statsOverlayBox, Math.Clamp(left, 0, maxLeft));
    Canvas.SetTop(statsOverlayBox, Math.Clamp(top, 0, maxTop));
}

void OnPlaybackFrameReadyForDragPreview()
{
    var dispatcher = Application.Current.Dispatcher;
    if (dispatcher is null || dispatcher.IsOnUIThread)
    {
        CompleteDragPreviewSeek();
        return;
    }

    dispatcher.BeginInvoke(CompleteDragPreviewSeek);
}

void CompleteDragPreviewSeek()
{
    dragPreviewSeekInFlight = false;

    if (playback is null || playback.IsPlaying || !isSeekDragActive)
    {
        return;
    }

    TryStartDragPreviewSeek(playback);
}

void OnStatsOverlayToggled(bool? isChecked)
{
    statsOverlayEnabled = isChecked == true;
    if (statsOverlayCanvas is not null)
    {
        // Hiding the canvas removes the overlay from layout/render entirely. Combined with
        // stopping the timer below, redraws caused by stats text mutation drop to zero.
        statsOverlayCanvas.IsVisible = statsOverlayEnabled;
    }

    // Stop polling the live counters when nobody is reading them. Restart only when
    // checked back on AND a playback is loaded — there's nothing to poll otherwise.
    if (statsOverlayEnabled && playback is not null)
    {
        if (!uiTimer.IsEnabled) uiTimer.Start();
    }
    else
    {
        if (uiTimer.IsEnabled) uiTimer.Stop();
    }

    // Reset all diagnostic counters at the toggle boundary so the user can directly
    // compare "stats on" vs "stats off" fps / VideoView redraw rates from the next
    // sampling window onwards.
    videoView?.ResetOnRenderCallCount();
    fpsFrames = 0;
    if (fpsStopwatch.IsRunning)
    {
        fpsStopwatch.Restart();
    }

}

void OnForceCpuReadbackToggled(bool? isChecked)
{
    bool enabled = isChecked == true;
    if (playback is not null)
    {
        playback.ForceCpuReadback = enabled;
    }
    SampleLog.Write($"ForceCpuReadback toggled: {enabled}");
}


void UpdatePlaybackUi()
{
    if (playback is null)
    {
        uiIsPlayingValue.Value = false;
        uiDurationValue.Value = TimeSpan.Zero;
        uiPositionValue.Value = TimeSpan.Zero;
        uiPositionIntValue.Value = 0;

        return;
    }

    long nowTicks = Stopwatch.GetTimestamp();

    if (nowTicks >= nextStatsOverlayUpdateTicks)
    {
        cachedStatsOverlayText = BuildStatsOverlayText(playback);
        nextStatsOverlayUpdateTicks = nowTicks + Stopwatch.Frequency;
    }

    if (statsOverlayEnabled)
    {
        if (!string.Equals(cachedStatsOverlayText, lastSetStatsText, StringComparison.Ordinal))
        {
            lastSetStatsText = cachedStatsOverlayText;
        }
        statsOverlayText.Text = cachedStatsOverlayText;
    }

    var position = playback.Position;
    var duration = playback.Duration;


    uiIsPlayingValue.Value = playback.IsPlaying;

    uiPositionValue.Value = playback.Position;
    uiDurationValue.Value = playback.Duration;

    uiPositionIntValue.Value = (int)uiPositionValue.Value.TotalSeconds;

    if (!isSeekDragActive)
    {
        suppressSeekSync = true;
        seekSlider.Maximum = Math.Max(1, duration.TotalSeconds);
        seekSlider.Value = Math.Clamp(position.TotalSeconds, 0, seekSlider.Maximum);
        suppressSeekSync = false;
    }

    UpdatePlaybackRenderLoopMode();

    if (playback.IsEnded)
    {
        statusText.Text = $"Finished: {Path.GetFileName(playback.SourcePath)}";
    }
}

void UpdatePlaybackRenderLoopMode()
{
    if (!Application.IsRunning)
    {
        return;
    }

    Application.Current.RenderLoopSettings.SetContinuous(playback?.IsPlaying == true);

    string renderLoopState = GetRenderLoopStatsText();
    if (!string.Equals(lastRenderLoopStateLog, renderLoopState, StringComparison.Ordinal))
    {
        lastRenderLoopStateLog = renderLoopState;
        SampleLog.Write($"Render loop: {renderLoopState}");
    }
}

void UpdateBackendText()
{
    backendText.Text = $"Backend: {window.GraphicsFactory.Backend}";
    SampleLog.Write(backendText.Text);
}

string BuildStatsOverlayText(VideoPlayback playback)
{
    string cpuUsageText;
    string gpuUsageText;
    long workingSetBytes;
    long privateBytes;
    long managedHeapBytes;
    ulong metalGpuBytes;
    lock (counterSnapshotGate)
    {
        cpuUsageText = cachedCpuUsageText;
        gpuUsageText = cachedGpuUsageText;
        workingSetBytes = cachedWorkingSetBytes;
        privateBytes = cachedPrivateBytes;
        managedHeapBytes = cachedManagedHeapBytes;
        metalGpuBytes = cachedMetalGpuBytes;
    }

    var builder = new StringBuilder();
    builder.Append(playback.DecoderStatsOverlayText);
    builder.Append("\nfps: ").Append(lastFpsText);
    builder.Append("\nrender loop: ").Append(GetRenderLoopStatsText());
    builder.Append("\npresent: ").Append(playback.PresentationTimingStatsText);
    builder.Append("\npresent path test: ").Append(videoView.PresentationPathText);
    builder.Append("\nprocess");
    builder.Append("\ncpu: ").Append(cpuUsageText);
    builder.Append("\nworking set: ").Append(FormatBytes(workingSetBytes));
    builder.Append("\nprivate bytes: ").Append(FormatBytes(privateBytes));
    builder.Append("\nmanaged heap: ").Append(FormatBytes(managedHeapBytes));
    builder.Append("\ngpu: ").Append(gpuUsageText);

    if (OperatingSystem.IsMacOS())
    {
        builder.Append("\n\ngpu");
        builder.Append("\nmetal alloc: ").Append(metalGpuBytes > 0 ? FormatBytes((long)metalGpuBytes) : "pending");
    }
    else if (OperatingSystem.IsWindows() && playback.D3D11Device != 0)
    {
        builder.Append("\n\ngpu");
        AppendGpuMemoryStats(builder, playback.D3D11Device, "local", D3D11Native.DXGI_MEMORY_SEGMENT_GROUP_LOCAL);
        AppendGpuMemoryStats(builder, playback.D3D11Device, "non-local", D3D11Native.DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL);
    }

    return builder.ToString();
}

string GetRenderLoopStatsText()
{
    if (!Application.IsRunning)
    {
        return "inactive";
    }

    var settings = Application.Current.RenderLoopSettings;
    string target = settings.TargetFps > 0 ? settings.TargetFps.ToString() : "uncapped";
    return $"mode={settings.Mode}, continuous={settings.IsContinuous}, vsync={(settings.VSyncEnabled ? "on" : "off")}, target={target}";
}

void AppendGpuMemoryStats(StringBuilder builder, nint d3d11Device, string label, uint segmentGroup)
{
    if (!D3D11Native.TryQueryVideoMemoryInfo(d3d11Device, segmentGroup, out var info))
    {
        builder.Append("\n").Append(label).Append(": unavailable");
        return;
    }

    builder.Append("\n").Append(label).Append(": ")
        .Append(FormatBytes((long)info.CurrentUsage))
        .Append(" / ")
        .Append(FormatBytes((long)info.Budget));
}

string FormatCpuUsageSample()
{
    if (intervalElapsedSeconds > 0)
    {
        double intervalPercent = intervalCpuSeconds / (intervalElapsedSeconds * Environment.ProcessorCount) * 100.0;
        lastCpuAveragePercent = Math.Clamp(intervalPercent, 0, 999);
        lastCpuMinPercent = double.IsPositiveInfinity(intervalCpuMinPercent) ? lastCpuAveragePercent : intervalCpuMinPercent;
        lastCpuMaxPercent = intervalCpuMaxPercent;
    }

    intervalCpuSeconds = 0;
    intervalElapsedSeconds = 0;
    intervalCpuMinPercent = double.PositiveInfinity;
    intervalCpuMaxPercent = 0;
    return $"{lastCpuAveragePercent:0.0}% (1s avg, min {lastCpuMinPercent:0.0}, max {lastCpuMaxPercent:0.0})";
}

void SampleCpuUsage()
{
    currentProcess.Refresh();
    TimeSpan cpuTime = currentProcess.TotalProcessorTime;
    long nowTicks = Stopwatch.GetTimestamp();
    double elapsedSeconds = (nowTicks - lastCpuSampleTicks) / (double)Stopwatch.Frequency;
    if (elapsedSeconds <= 0)
    {
        return;
    }

    double cpuSeconds = (cpuTime - lastCpuTime).TotalSeconds;
    lastCpuPercent = cpuSeconds / (elapsedSeconds * Environment.ProcessorCount) * 100.0;
    lastCpuPercent = Math.Clamp(lastCpuPercent, 0, 999);
    intervalCpuSeconds += Math.Max(0, cpuSeconds);
    intervalElapsedSeconds += elapsedSeconds;
    intervalCpuMinPercent = Math.Min(intervalCpuMinPercent, lastCpuPercent);
    intervalCpuMaxPercent = Math.Max(intervalCpuMaxPercent, lastCpuPercent);

    lastCpuTime = cpuTime;
    lastCpuSampleTicks = nowTicks;
}

void ResetCpuStats()
{
    lock (counterSnapshotGate)
    {
        counterResetRequested = true;
        cachedCpuUsageText = "warming";
        cachedWorkingSetBytes = 0;
        cachedPrivateBytes = 0;
        cachedManagedHeapBytes = 0;
        cachedMetalGpuBytes = 0;
    }
}

void ResetGpuStats()
{
    lock (counterSnapshotGate)
    {
        counterResetRequested = true;
        cachedGpuUsageText = OperatingSystem.IsWindows() ? "warming" : "n/a";
    }
}

void StartCounterAggregation()
{
    if (counterAggregationCts is not null)
    {
        return;
    }

    ResetCounterSamplingState();
    counterAggregationCts = new CancellationTokenSource();
    counterAggregationTask = Task.Run(() => RunCounterAggregationLoopAsync(counterAggregationCts.Token));
}

void StopCounterAggregation()
{
    var cts = counterAggregationCts;
    counterAggregationCts = null;

    if (cts is null)
    {
        return;
    }

    cts.Cancel();
    cts.Dispose();
}

async Task RunCounterAggregationLoopAsync(CancellationToken cancellationToken)
{
    try
    {
        ResetCounterSamplingState();
        if (OperatingSystem.IsWindows())
        {
            RebuildGpuCounters();
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        long nextCpuPublishTicks = Stopwatch.GetTimestamp() + Stopwatch.Frequency;

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                if (TryConsumeCounterResetRequest())
                {
                    ResetCounterSamplingState();
                    if (OperatingSystem.IsWindows())
                    {
                        RebuildGpuCounters();
                    }
                }

                currentProcess.Refresh();
                SampleCpuUsage();

                string gpuUsageText = SampleGpuUsageText();
                long nowTicks = Stopwatch.GetTimestamp();
                string cpuUsageText = nowTicks >= nextCpuPublishTicks
                    ? FormatCpuUsageSample()
                    : GetCachedCpuUsageText();

                if (nowTicks >= nextCpuPublishTicks)
                {
                    nextCpuPublishTicks = nowTicks + Stopwatch.Frequency;
                }

                long privateBytes;
                if (OperatingSystem.IsMacOS() && MacOsNative.TryGetPhysFootprint(out ulong physFootprint))
                {
                    privateBytes = (long)physFootprint;
                }
                else
                {
                    privateBytes = currentProcess.PrivateMemorySize64;
                }

                ulong metalGpuBytes = 0;
                if (OperatingSystem.IsMacOS())
                {
                    // Reference reads are atomic; safe cross-thread access.
                    nint metalDevice = playback?.MetalDevice ?? 0;
                    metalGpuBytes = MacOsNative.GetMetalAllocatedSize(metalDevice);
                }

                lock (counterSnapshotGate)
                {
                    cachedCpuUsageText = cpuUsageText;
                    cachedGpuUsageText = gpuUsageText;
                    cachedWorkingSetBytes = currentProcess.WorkingSet64;
                    cachedPrivateBytes = privateBytes;
                    cachedManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
                    cachedMetalGpuBytes = metalGpuBytes;
                }
            }
            catch (Exception perTickEx)
            {
                // A single bad sample shouldn't kill the whole loop. Log once per unique
                // message so a recurring failure doesn't spam the log.
                string key = $"{perTickEx.GetType().Name}: {perTickEx.Message}";
                if (loggedCounterErrors.Add(key))
                {
                    SampleLog.Write($"Counter aggregation tick failed (will retry): {key}\n{perTickEx.StackTrace}");
                }
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        SampleLog.Write($"Counter aggregation loop terminated by exception: {ex}");
    }
    finally
    {
        DisposeGpuCounters();
    }
}


bool TryConsumeCounterResetRequest()
{
    lock (counterSnapshotGate)
    {
        bool requested = counterResetRequested;
        counterResetRequested = false;
        return requested;
    }
}

string GetCachedCpuUsageText()
{
    lock (counterSnapshotGate)
    {
        return cachedCpuUsageText;
    }
}

void ResetCounterSamplingState()
{
    currentProcess.Refresh();
    lastCpuTime = currentProcess.TotalProcessorTime;
    lastCpuSampleTicks = Stopwatch.GetTimestamp();
    lastCpuPercent = 0;
    lastCpuAveragePercent = 0;
    lastCpuMinPercent = 0;
    lastCpuMaxPercent = 0;
    intervalCpuSeconds = 0;
    intervalElapsedSeconds = 0;
    intervalCpuMinPercent = double.PositiveInfinity;
    intervalCpuMaxPercent = 0;
    gpuCountersPrimed = false;
    lastGpuPercent = 0;
}

string SampleGpuUsageText()
{
    if (!OperatingSystem.IsWindows())
    {
        return "n/a";
    }

    if (gpuEngineCounters.Count == 0)
    {
        RebuildGpuCounters();
        if (gpuEngineCounters.Count == 0)
        {
            return "unavailable";
        }
    }

    double sample = 0;
    bool rebuildNeeded = false;
    foreach (var counter in gpuEngineCounters)
    {
        try
        {
            sample += counter.NextValue();
        }
        catch
        {
            rebuildNeeded = true;
            break;
        }
    }

    if (rebuildNeeded)
    {
        RebuildGpuCounters();
        return gpuCountersPrimed ? $"{lastGpuPercent:0.0}%" : "warming";
    }

    lastGpuPercent = Math.Clamp(sample, 0, 100);
    if (!gpuCountersPrimed)
    {
        gpuCountersPrimed = true;
        return "warming";
    }

    return $"{lastGpuPercent:0.0}%";
}

void ResetFpsStats()
{
    fpsStopwatch.Reset();
    fpsFrames = 0;
    lastFpsText = "warming";
}

bool UpdateFpsCounter(ref int frameCount)
{
    double elapsed = fpsStopwatch.Elapsed.TotalSeconds;
    if (elapsed < 1.0)
    {
        return false;
    }

    lastFpsText = $"{(frameCount <= 1 ? 0 : frameCount) / elapsed:0.0}";
    frameCount = 0;
    fpsStopwatch.Restart();
    return true;
}

[SupportedOSPlatform("windows")]
void RebuildGpuCounters()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    DisposeGpuCounters();

    try
    {
        var category = new PerformanceCounterCategory("GPU Engine");
        string pidToken = $"pid_{currentProcess.Id}_";
        foreach (var instanceName in category.GetInstanceNames())
        {
            if (!instanceName.Contains(pidToken, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                gpuEngineCounters.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", instanceName, readOnly: true));
            }
            catch
            {
            }
        }
    }
    catch
    {
    }

    foreach (var counter in gpuEngineCounters)
    {
        try
        {
            _ = counter.NextValue();
        }
        catch
        {
        }
    }

    gpuCountersPrimed = false;
}

void DisposeGpuCounters()
{
    if (!OperatingSystem.IsWindows())
    {
        return;
    }

    foreach (var counter in gpuEngineCounters)
    {
        counter.Dispose();
    }

    gpuEngineCounters.Clear();
}

static string FormatBytes(long bytes)
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

static string FormatTime(TimeSpan time)
{
    if (time < TimeSpan.Zero)
    {
        time = TimeSpan.Zero;
    }

    return time.ToString(@"hh\:mm\:ss");
}

static void Startup()
{
    SampleLog.Write("Startup begin.");
    InitializeFFmpegBindings();

    var args = Environment.GetCommandLineArgs();
    SampleLog.Write($"Args: {string.Join(" ", args)}");

    if (OperatingSystem.IsWindows())
    {
        SampleLog.Write("Registering Win32 platform.");
        Win32Platform.Register();

        if (args.Any(a => a is "--vg"))
        {
            SampleLog.Write("Registering MewVG Win32 backend.");
            MewVGWin32Backend.Register();
        }
        else
        {
            SampleLog.Write("Registering Direct2D backend.");
            Direct2DBackend.Register();
        }
    }
    else if (OperatingSystem.IsMacOS())
    {
        SampleLog.Write("Registering macOS platform/backend.");
        MacOSPlatform.Register();
        MewVGMacOSBackend.Register();
    }
    else
    {
        SampleLog.Write("Registering X11 platform/backend.");
        X11Platform.Register();
        MewVGX11Backend.Register();
    }

    SampleLog.Write("Startup completed.");
}

static void InitializeFFmpegBindings()
{
    SampleLog.Write("InitializeFFmpegBindings begin.");
    string[] candidates = BuildFFmpegSearchPaths();

    // Pick the first candidate that actually contains FFmpeg shared libs — checking
    // Directory.Exists alone short-circuits on AppContext.BaseDirectory (always exists)
    // and prevents fall-through to Homebrew / system paths.
    string? libraryPath = candidates.FirstOrDefault(ContainsFFmpegLibs);
    if (!string.IsNullOrWhiteSpace(libraryPath))
    {
        SampleLog.Write($"FFmpeg library path selected: {libraryPath}");
        ffmpeg.RootPath = libraryPath;
        DynamicBindings.LibrariesPath = libraryPath;
    }
    else
    {
        SampleLog.Write($"No FFmpeg library directory contains avformat. Searched: [{string.Join(", ", candidates)}]. Falling back to default loader behavior.");
    }

    DynamicBindings.Initialize();
    SampleLog.Write("Dynamic FFmpeg bindings initialized.");
}

static bool ContainsFFmpegLibs(string directory)
{
    if (!Directory.Exists(directory))
    {
        return false;
    }

    // Match the platform's library naming for libavformat (the most identifying FFmpeg
    // module). Versioned suffixes vary across distros and Homebrew formula bumps —
    // wildcard matching catches all of them.
    string[] patterns = OperatingSystem.IsWindows()
        ? ["avformat*.dll"]
        : OperatingSystem.IsMacOS()
            ? ["libavformat*.dylib"]
            : ["libavformat.so*"];

    foreach (var pattern in patterns)
    {
        try
        {
            if (Directory.EnumerateFiles(directory, pattern).Any())
            {
                return true;
            }
        }
        catch
        {
            // Permission / IO error on a candidate — skip silently and try the next.
        }
    }

    return false;
}

static string[] BuildFFmpegSearchPaths()
{
    // App-local locations (cross-platform): the user can drop the FFmpeg shared
    // libs next to the app and the loader picks them up before system paths.
    var paths = new List<string>
    {
        Path.Combine(AppContext.BaseDirectory, "ffmpeg-native"),
        AppContext.BaseDirectory,
    };

    if (OperatingSystem.IsWindows())
    {
        paths.Insert(0, Path.Combine(AppContext.BaseDirectory, "FFmpeg", "bin", "x64"));
        paths.Insert(1, Path.Combine(AppContext.BaseDirectory, "win-x64"));
        paths.Insert(2, Path.Combine(AppContext.BaseDirectory, "ffmpeg-native", "win-x64"));
    }
    else if (OperatingSystem.IsMacOS())
    {
        // Homebrew default install paths. Apple Silicon: /opt/homebrew, Intel: /usr/local.
        paths.Add("/opt/homebrew/lib");
        paths.Add("/usr/local/lib");
    }
    else if (OperatingSystem.IsLinux())
    {
        // FFMPEG_HOME / LD_LIBRARY_PATH overrides come first so a user-supplied
        // build (e.g. /opt/ffmpeg-8.0.1) wins over the distro-packaged libs.
        string? ffmpegHome = Environment.GetEnvironmentVariable("FFMPEG_HOME");
        if (!string.IsNullOrEmpty(ffmpegHome))
        {
            paths.Insert(0, Path.Combine(ffmpegHome, "lib"));
            paths.Insert(1, Path.Combine(ffmpegHome, "lib64"));
        }

        string? ldLibraryPath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        if (!string.IsNullOrEmpty(ldLibraryPath))
        {
            foreach (var entry in ldLibraryPath.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                paths.Add(entry);
            }
        }

        // Common standalone-install prefixes (statically-linked FFmpeg drops, e.g.
        // /opt/ffmpeg-8.0.1, BtbN's master builds, custom /usr/local prefixes).
        // These come before the distro paths so newer custom builds shadow older
        // packaged ones — apt's libavcodec is often a major version behind.
        foreach (var prefix in new[] { "/opt/ffmpeg-8.0.1", "/opt/ffmpeg", "/usr/local" })
        {
            paths.Add(Path.Combine(prefix, "lib"));
            paths.Add(Path.Combine(prefix, "lib64"));
        }

        // Standard Linux package install paths (apt: libavcodec*, etc.).
        paths.Add("/usr/lib/x86_64-linux-gnu");
        paths.Add("/usr/lib/aarch64-linux-gnu");
        paths.Add("/usr/lib64");
        paths.Add("/usr/lib");
    }

    return [.. paths];
}

void EnsureLogWindow()
{
    if (logWindow is not null)
    {
        logWindow.Show(window);
        return;
    }

    logWindow = new Window()
        .Resizable(720, 420)
        .OnBuild(x => x
            .Ref(out logWindow)
            .Title("Aprillz.MewUI Video Sample Log")
            .Content(
                new Border()
                    .Padding(8)
                    .Child(
                        new MultiLineTextBox()
                            .Ref(out logTextBox)
                            .Wrap(true)
                            .FontFamily("Consolas")
                            .Text(SampleLog.Snapshot)
                    )
            )
        )
        .OnClosed(() =>
        {
            logWindow = null!;
            logTextBox = null!;
        });

    logWindow.Show(window);
    SampleLog.Write("Log window opened.");
}

void AppendLogLine(string line)
{
    Console.WriteLine(line);

    var dispatcher = Application.Current.Dispatcher;
    if (dispatcher is null)
    {
        return;
    }

    dispatcher.BeginInvoke(() =>
    {
        if (logTextBox is null)
        {
            return;
        }

        logTextBox.AppendText(
            string.IsNullOrEmpty(logTextBox.Text)
                ? line
                : Environment.NewLine + line,
            scrollToCaret: true);
    });
}
