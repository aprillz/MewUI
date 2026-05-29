using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Skia.Controls;
using Aprillz.MewUI.Skia.Interop;
using Aprillz.MewUI.Skia.Sample.Diagnostics;

using SkiaSharp;

#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]
#endif

Startup();

Window window = null!;
SkiaCanvasView canvas = null!;
CheckBox whiteBgCheckBox = null!;

ObservableValue<string> backendStatus = new("Backend: -");
ObservableValue<string> pathStatus = new("Path: -");
ObservableValue<bool> maxFpsEnabled = new(false);
ObservableValue<bool> whiteBgEnabled = new(false);

// Single multiline overlay observable. Five separate TextBlocks each invalidating the
// stack panel on every 1 Hz publish was producing a visible frame hitch — collapsing into
// one observable means one layout pass + one text raster + one MTLTexture upload per
// publish (the owner-keyed text cache reuses the same slot across renders).
ObservableValue<string> overlayText = new("FPS: - / CPU: - / GPU: -\nWS: - / PB: - / MH: -");

// Process counters (CPU/GPU/memory). Updated on a background thread; we marshal back to the
// UI dispatcher to flip the ObservableValue<> binding, otherwise the data-binding glue
// runs on the polling thread and the framework's invalidation path is not thread-safe.
ProcessStatistics processStats = new();
StatsSnapshot _latestStats = default;
string _latestFpsText = "-";

// Time-based animation: phase advances from a wall-clock stopwatch so the look stays
// consistent regardless of whether the renderer is VSync-capped (~60 Hz) or running
// uncapped under Max FPS.
Stopwatch animationClock = Stopwatch.StartNew();

// FPS sampling: each rendered frame bumps fpsFrames, fpsStopwatch tracks elapsed seconds.
// Once per second we divide and publish to fpsText.
Stopwatch fpsStopwatch = new();
int fpsFrames = 0;

// Cached reference of canvas.PathDescription so OnFrameRendered can skip the
// `$"Path: ..."` interpolation when the path hasn't changed. PathDescription returns
// interned string literals — reference comparison is enough.
string _lastPathDesc = "";

// SK object cache reused by PaintScene. Native-handle wrappers cost P/Invoke + native
// ref counting per allocation; recreate only when construction inputs (gradient extent,
// font size) actually change. Mutable Color / StrokeWidth / Size live on the instance.
SKPaint sBackgroundPaint = new() { IsAntialias = true };
SKPaint sCardStrokePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, Color = new SKColor(113, 145, 230, 90) };
SKPaint sWavePaint = new()
{
    IsAntialias = true,
    Style = SKPaintStyle.Stroke,
    StrokeCap = SKStrokeCap.Round,
    StrokeJoin = SKStrokeJoin.Round,
    Color = new SKColor(39, 92, 210, 220)
};
SKPaint sGlowPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(88, 133, 255, 70) };
SKPaint sDotPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(28, 73, 194, 255) };
SKPaint sAccentPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(248, 118, 74, 235) };
SKPaint sAccentTextPaint = new() { IsAntialias = true, Color = new SKColor(40, 56, 104, 240) };
SKPath sWavePath = new();
SKTypeface? sAccentTypeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
SKFont sAccentFont = new(sAccentTypeface, 20f);
SKShader? sBackgroundShader = null;
float sBackgroundShaderWidth = -1f;
float sBackgroundShaderHeight = -1f;

var root = new Window()
    .Resizable(1080, 760)
    .StartCenterScreen()
    .OnBuild(x => x
        .Ref(out window)
        .Title("Aprillz.MewUI Skia Sample")
        .Content(
            new DockPanel()
                .Margin(8)
                .Children(
                    TopBar()
                        .DockTop(),

                    new SkiaCanvasView()
                        .Ref(out canvas)
                )
        )
        .OnLoaded(() =>
        {
            string backendId = Application.Current.GraphicsFactory.Backend;
            backendStatus.Value = $"Backend: {backendId}";
            whiteBgCheckBox.IsVisible = backendId.Equals("Gdi", StringComparison.OrdinalIgnoreCase);
            canvas.PaintSurface += PaintScene;
            // Render loop runs continuously so the animation keeps ticking and Max FPS has an
            // effect. VSyncEnabled controls whether each frame waits for the display refresh.
            var settings = Application.Current.RenderLoopSettings;
            settings.TargetFps = 0;
            settings.VSyncEnabled = !maxFpsEnabled.Value;
            settings.SetContinuous(true);

            // Start process counter polling. The event fires on the polling thread; we hop
            // back to the UI dispatcher before mutating ObservableValue<> so the binding
            // invalidation runs on the right thread.
            processStats.StatsUpdated += OnStatsUpdated;
            processStats.Start();
        })
        .OnClosed(() =>
        {
            if (Application.IsRunning)
            {
                Application.Current.RenderLoopSettings.SetContinuous(false);
            }

            processStats.StatsUpdated -= OnStatsUpdated;
            processStats.Stop();
            processStats.Dispose();

            // Tear down cached SK native handles. Finalizers would eventually catch them,
            // but explicit Dispose avoids the cost of the queue scan at process exit and
            // surfaces use-after-free bugs immediately if anything still references them.
            sBackgroundPaint.Dispose();
            sCardStrokePaint.Dispose();
            sWavePaint.Dispose();
            sGlowPaint.Dispose();
            sDotPaint.Dispose();
            sAccentPaint.Dispose();
            sAccentTextPaint.Dispose();
            sWavePath.Dispose();
            sAccentFont.Dispose();
            sAccentTypeface?.Dispose();
            sBackgroundShader?.Dispose();
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
            UpdateFps();

            // Path description rarely changes (initial resolve + possible GPU→CPU fallback).
            // Reference-compare against the cached literal so we skip string interpolation
            // on the steady-state path. Without this guard, the `$"Path: …"` allocation runs
            // every rendered frame (~144/sec on ProMotion displays).
            string desc = canvas.PathDescription;
            if (desc != _lastPathDesc)
            {
                _lastPathDesc = desc;
                pathStatus.Value = "Path: " + desc;
            }
        })
    );

Application.Run(root);

void OnStatsUpdated(StatsSnapshot snapshot)
{
    var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
    if (dispatcher is null || dispatcher.IsOnUIThread)
    {
        ApplyStats(snapshot);
        return;
    }

    dispatcher.BeginInvoke(() => ApplyStats(snapshot));
}

void ApplyStats(StatsSnapshot snapshot)
{
    _latestStats = snapshot;
    PublishOverlay();
}

void PublishOverlay()
{
    string gpuText = _latestStats.GpuPercent is { } gpu ? $"{gpu:0.0}%" : "n/a";
    var text =
        $"FPS: {_latestFpsText} / " +
        $"CPU: {_latestStats.CpuPercent:0.0}% / " +
        $"GPU: {gpuText}\n" +
        $"WS:  {FormatBytes(_latestStats.WorkingSetBytes)} / " +
        $"PB:  {FormatBytes(_latestStats.PrivateBytes)} / " +
        $"MH:  {FormatBytes(_latestStats.ManagedHeapBytes)}";

    overlayText.Value = text;
}

static string FormatBytes(long bytes)
{
    if (bytes <= 0) return "0 B";
    string[] units = ["B", "KB", "MB", "GB", "TB"];
    double value = bytes;
    int unitIndex = 0;
    while (value >= 1024 && unitIndex < units.Length - 1)
    {
        value /= 1024;
        unitIndex++;
    }
    return $"{value:0.#} {units[unitIndex]}";
}

bool UpdateFps()
{
    double elapsed = fpsStopwatch.Elapsed.TotalSeconds;
    if (elapsed < 1.0)
    {
        return false;
    }

    double rate = (fpsFrames <= 1 ? 0 : fpsFrames) / elapsed;
    _latestFpsText = $"{rate:0.0}";
    fpsFrames = 0;
    fpsStopwatch.Restart();
    // Don't publish here — ApplyStats fires shortly after (same 1 Hz tick boundary) and will
    // pick up _latestFpsText. Keeps the overlay TextBlock to a single invalidation per second
    // instead of two (fps + stats).
    return true;
}

void OnWhiteBgToggled(bool isChecked)
{
    if (canvas is null) return;
    canvas.Background = isChecked ? new Color(255, 255, 255, 255) : null;
    canvas.InvalidateVisual();
}

void OnMaxFpsToggled(bool isChecked)
{
    if (!Application.IsRunning)
    {
        return;
    }

    var settings = Application.Current.RenderLoopSettings;
    settings.TargetFps = 0;
    settings.VSyncEnabled = !maxFpsEnabled.Value;
    settings.SetContinuous(true);

    // Reset FPS sampling so the first post-toggle reading reflects the new mode immediately
    // instead of averaging across the transition.
    fpsStopwatch.Restart();
    fpsFrames = 0;
    _latestFpsText = "-";
}

FrameworkElement TopBar() => new Border()
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
                new StackPanel()
                    .Vertical()
                    .Spacing(4)
                    .DockRight()
                    .Children(
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Right()
                            .Children(
                            new CheckBox()
                                .Content("Max FPS")
                                .Right()
                                .BindIsChecked(maxFpsEnabled)
                                .OnCheckedChanged(OnMaxFpsToggled),

                            new CheckBox()
                                .Ref(out whiteBgCheckBox)
                                .Content("White BG (opaque fast path)")
                                .Right()
                                .BindIsChecked(whiteBgEnabled)
                                .OnCheckedChanged(OnWhiteBgToggled)),

                        // One multiline TextBlock for all 6 stats — single layout pass + single
                        // text raster + single owner-cache texture update per publish (1 Hz).
                        // Width is fixed so digit-count changes don't cascade a measure pass
                        // up through the parent StackPanel / DockPanel.
                        new TextBlock()
                            .Right()
                            .BindText(overlayText)
                            .FontFamily("Consolas")
                            .FontSize(11)
                    ),

                new StackPanel()
                    .Vertical()
                    .Spacing(4)
                    .Children(
                        new TextBlock()
                            .Text("Aprillz.MewUI Skia Sample")
                            .FontSize(18)
                            .SemiBold(),
                        new TextBlock()
                            .BindText(backendStatus)
                            .FontSize(12),
                        new TextBlock()
                            .BindText(pathStatus)
                            .FontSize(11)
                    )
                    .DockLeft()
            )
    );

void PaintScene(SKCanvas canvas, SKImageInfo info)
{
    float width = info.Width;
    float height = info.Height;
    float minSide = MathF.Min(width, height);
    // Phase comes from wall-clock elapsed seconds × speed — frame-rate-independent.
    float t = (float)(animationClock.Elapsed.TotalSeconds * 2.1);

    // Card background — gradient shader depends on (width, height), so rebuild only on resize.
    if (sBackgroundShader is null || sBackgroundShaderWidth != width || sBackgroundShaderHeight != height)
    {
        sBackgroundShader?.Dispose();
        sBackgroundShader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            [new SKColor(246, 249, 255), new SKColor(228, 237, 255)],
            null,
            SKShaderTileMode.Clamp);
        sBackgroundPaint.Shader = sBackgroundShader;
        sBackgroundShaderWidth = width;
        sBackgroundShaderHeight = height;
    }
    sCardStrokePaint.StrokeWidth = MathF.Max(1.5f, minSide * 0.012f);
    var panelRect = new SKRect(0, 0, width, height);
    var innerRect = panelRect;
    innerRect.Inflate(-1.5f, -1.5f);
    canvas.DrawRoundRect(panelRect, 28, 28, sBackgroundPaint);
    canvas.DrawRoundRect(innerRect, 26, 26, sCardStrokePaint);

    // Animated wave — reset the shared path and refill points each frame.
    sWavePaint.StrokeWidth = MathF.Max(2f, minSide * 0.018f);
    sWavePath.Reset();

    float baseline = height * 0.70f;

    var multiply = 2.0f;
    var div = 56 * multiply;

    for (int i = 0; i <= div; i++)
    {
        float x = width * 0.08f + (width * 0.84f / div) * i;
        float amplitude = minSide * 0.10f;
        float y = baseline + MathF.Cos(t * 1.65f + i / multiply * 0.36f) * amplitude;

        if (i == 0)
        {
            sWavePath.MoveTo(x, y);
        }
        else
        {
            sWavePath.LineTo(x, y);
        }
    }
    canvas.DrawPath(sWavePath, sWavePaint);

    // Three orbiting dots
    var center = new SKPoint(width * 0.5f, height * 0.42f);
    float orbit = minSide * 0.19f;
    float radius = minSide * 0.09f;
    for (int i = 0; i < 3; i++)
    {
        float angle = t + i * 2.0943952f;
        float x = center.X + MathF.Cos(angle) * orbit;
        float y = center.Y + MathF.Sin(angle * 1.17f) * orbit * 0.6f;
        canvas.DrawCircle(x, y, radius * 1.35f, sGlowPaint);
        canvas.DrawCircle(x, y, radius, sDotPaint);
    }

    // Accent pill + label — font size depends on minSide, only update on change.
    float accentFontSize = MathF.Max(20f, minSide * 0.11f);
    if (sAccentFont.Size != accentFontSize)
    {
        sAccentFont.Size = accentFontSize;
    }

    float accentWidth = width * 0.18f;
    float accentHeight = minSide * 0.07f;
    float accentX = width * 0.11f + (MathF.Sin(t * 1.4f) * width * 0.05f);
    float accentY = height * 0.18f;
    canvas.DrawRoundRect(new SKRect(accentX, accentY, accentX + accentWidth, accentY + accentHeight), 14, 14, sAccentPaint);
    canvas.DrawText("SKSurface", width * 0.10f, height * 0.24f, SKTextAlign.Left, sAccentFont, sAccentTextPaint);
}

static void Startup()
{
    var args = Environment.GetCommandLineArgs();

    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();
        if (args.Any(a => a is "--vg"))
        {
            MewVGWin32Backend.Register();
            SkiaMewVGWin32Interop.Register();
        }
        else if (args.Any(a => a is "--gdigl"))
        {
            GdiBackend.Register();
            SkiaGdiInterop.RegisterGL();
        }
        else if (args.Any(a => a is "--gdi"))
        {
            GdiBackend.Register();
            SkiaGdiInterop.Register();
        }
        else
        {
            Direct2DBackend.Register();
            SkiaDirect2DInterop.Register();
        }
    }
    else if (OperatingSystem.IsMacOS())
    {
        MacOSPlatform.Register();
        MewVGMacOSBackend.Register();
        SkiaMewVGMacOSInterop.Register();
    }
    else if (OperatingSystem.IsLinux())
    {
        X11Platform.Register();
        MewVGX11Backend.Register();
        SkiaMewVGX11Interop.Register();
    }

    Application.DispatcherUnhandledException += e =>
    {
        Console.WriteLine(e.Exception.ToString());
        e.Handled = true;
    };
}
