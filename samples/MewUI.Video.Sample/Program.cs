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

Startup();

VideoPlayerWindow playerWindow = null!;
Window? logWindow = null;
MultiLineTextBox? logTextBox = null;

ProcessStatistics processStats = new();

SampleLog.LineAppended += AppendLogLine;

string? startupPath = Environment.GetCommandLineArgs()
    .Skip(1)
    .FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));

Application.DispatcherUnhandledException += e =>
{
    SampleLog.Write($"DispatcherUnhandledException: {e.Exception}");
    Console.WriteLine(e.Exception.ToString());
    e.Handled = true;
};

playerWindow = new VideoPlayerWindow(startupPath);
processStats.GetMetalDevice = () => playerWindow.Player?.Playback?.MetalDevice ?? 0;
processStats.StatsUpdated += snapshot => playerWindow.Stats = snapshot;
playerWindow.Loaded += () =>
{
    EnsureLogWindow();
    processStats.Start();
};
playerWindow.Closed += () =>
{
    processStats.Stop();
    processStats.Dispose();
    logWindow?.Close();
};

Application.Run(playerWindow);

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

    // Pick the first candidate that actually contains FFmpeg shared libs - checking
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
    // module). Versioned suffixes vary across distros and Homebrew formula bumps -
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
            // Permission / IO error on a candidate - skip silently and try the next.
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
        // packaged ones - apt's libavcodec is often a major version behind.
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
