using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
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

VideoPlayerWindow playerWindow = null!;
Window? logWindow = null;
MultiLineTextBox? logTextBox = null;

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
GpuLoadSampler? gpuSampler = null;
double lastGpuPercent = 0;
CancellationTokenSource? counterAggregationCts = null;
Task? counterAggregationTask = null;
bool counterResetRequested = false;
HashSet<string> loggedCounterErrors = new();

// Snapshot mutated by the counter aggregation loop and pushed into the player window
// after each tick. Reads on the UI thread go through VideoPlayerWindow.Stats (atomic
// reference assignment) so we never need to lock on the publish path.
HostStats latestStats = HostStats.Empty;
object counterSnapshotGate = new();

SampleLog.LineAppended += AppendLogLine;

string? startupPath = Environment.GetCommandLineArgs()
    .Skip(1)
    .FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));

if (Debugger.IsAttached && string.IsNullOrWhiteSpace(startupPath))
{
    string[] debugCandidates = OperatingSystem.IsMacOS()
        ? ["/Users/al6uiz/Downloads/hevc_4k60P_main_dji_mavic3.mov"]
        : OperatingSystem.IsLinux()
        ? ["/mnt/e/Downloads/hevc_4k60P_main_dji_mavic3.mov"]
        : [@"E:\Downloads\hevc_4k60P_main_dji_mavic3.mov"];

    startupPath = debugCandidates.FirstOrDefault(File.Exists);
}

Application.DispatcherUnhandledException += e =>
{
    SampleLog.Write($"DispatcherUnhandledException: {e.Exception}");
    Console.WriteLine(e.Exception.ToString());
    e.Handled = true;
};

playerWindow = new VideoPlayerWindow(startupPath);
playerWindow.Loaded += () =>
{
    EnsureLogWindow();
    StartCounterAggregation();
};
playerWindow.Closed += () =>
{
    StopCounterAggregation();
    logWindow?.Close();
};

Application.Run(playerWindow);

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
            gpuSampler ??= new GpuLoadSampler();
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
                        // LibreHardwareMonitor's Computer auto-rediscovers adapters on the
                        // next Sample() — no explicit teardown / rebuild needed like the old
                        // PerformanceCounter path required.
                    }
                }

                currentProcess.Refresh();
                SampleCpuUsage();

                string gpuUsageText = SampleGpuUsageText();
                long nowTicks = Stopwatch.GetTimestamp();
                string cpuUsageText = nowTicks >= nextCpuPublishTicks
                    ? FormatCpuUsageSample()
                    : latestStats.CpuUsageText;

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
                    nint metalDevice = playerWindow?.Player?.Playback?.MetalDevice ?? 0;
                    metalGpuBytes = MacOsNative.GetMetalAllocatedSize(metalDevice);
                }

                var snapshot = new HostStats(
                    CpuUsageText: cpuUsageText,
                    GpuUsageText: gpuUsageText,
                    WorkingSetBytes: currentProcess.WorkingSet64,
                    PrivateBytes: privateBytes,
                    ManagedHeapBytes: GC.GetTotalMemory(forceFullCollection: false),
                    MetalGpuBytes: metalGpuBytes);

                latestStats = snapshot;
                if (playerWindow is not null)
                {
                    playerWindow.Stats = snapshot;
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
        gpuSampler?.Dispose();
        gpuSampler = null;
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
    lastGpuPercent = 0;
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

string SampleGpuUsageText()
{
    if (!OperatingSystem.IsWindows())
    {
        return "n/a";
    }

    gpuSampler ??= new GpuLoadSampler();
    if (!gpuSampler.IsAvailable)
    {
        return "unavailable";
    }

    var v = gpuSampler.Sample();
    if (!v.HasValue) return "n/a";
    lastGpuPercent = v.Value;
    return gpuSampler.IsPrimed ? $"{lastGpuPercent:0.0}%" : "warming";
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
        else if (args.Any(a => a is "--gdi"))
        {
            SampleLog.Write("Registering GDI backend.");
            GdiBackend.Register();
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
        logWindow.Show(playerWindow);
        return;
    }

    Window window = null!;
    MultiLineTextBox textBox = null!;
    window = new Window()
        .Resizable(720, 420)
        .OnBuild(x => x
            .Ref(out window)
            .Title("Aprillz.MewUI Video Sample Log")
            .Content(
                new Border()
                    .Padding(8)
                    .Child(
                        new MultiLineTextBox()
                            .Ref(out textBox)
                            .Wrap(true)
                            .FontFamily("Consolas")
                            .Text(SampleLog.Snapshot)
                    )
            )
        )
        .OnClosed(() =>
        {
            logWindow = null;
            logTextBox = null;
        });

    logWindow = window;
    logTextBox = textBox;
    logWindow.Show(playerWindow);
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
