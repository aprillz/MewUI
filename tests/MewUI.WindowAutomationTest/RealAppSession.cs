using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Hosts one real application loop for the whole test assembly against the actual platform: Win32 with
/// Direct2D, X11 with MewVG, or macOS with MewVG. Tests exercise real native windows, real platform input
/// traffic, and real per-monitor DPI. Test bodies are marshaled onto the loop's thread with
/// <see cref="RunAsync"/>; awaits inside them resume on it via the platform's synchronization context.
/// </summary>
[TestClass]
public static class RealAppSession
{
    private static Thread? _uiThread;
    private static IDispatcher? _dispatcher;
    private static Window? _keeperWindow;
    private static Exception? _startupFailure;
    private static readonly ManualResetEventSlim _loopReady = new();

    /// <summary>Set by the macOS entry point, whose main thread runs the loop in place of a dedicated thread.</summary>
    internal static bool HostsLoopOnMainThread { get; set; }

    [AssemblyInitialize]
    public static void StartApplication(TestContext _)
    {
        if (OperatingSystem.IsMacOS())
        {
            // AppKit runs only on the process main thread, which only the runner executable's own entry point hands over.
            if (!HostsLoopOnMainThread)
            {
                _startupFailure = new PlatformNotSupportedException(
                    "On macOS the suite runs from the runner executable (-p:UseVSTest=false with an osx runtime identifier), whose entry point hosts the loop on the main thread.");
                return;
            }
        }
        else if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            _uiThread = new Thread(RunLoop);
            if (OperatingSystem.IsWindows())
            {
                _uiThread.SetApartmentState(ApartmentState.STA);
            }

            _uiThread.IsBackground = true;
            _uiThread.Start();
        }
        else
        {
            return;
        }

        if (!_loopReady.Wait(TimeSpan.FromSeconds(15)))
        {
            _startupFailure = new TimeoutException("The application loop did not come up.");
        }
    }

    [AssemblyCleanup]
    public static void StopApplication()
    {
        // Shutting down AppKit terminates the process before the test platform reports; the macOS entry point
        // ends the process with the platform's exit code instead.
        if (_dispatcher is null || HostsLoopOnMainThread)
        {
            return;
        }

        _dispatcher.BeginInvoke(static () => Application.Shutdown());
        _uiThread?.Join(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Runs the body on the application's UI thread and completes when it does. A body that
    /// throws (including asserts) surfaces its exception to the awaiting test.
    /// </summary>
    public static Task RunAsync(Func<Task> body)
    {
        if (_startupFailure is not null)
        {
            throw new InvalidOperationException("The application loop failed to start.", _startupFailure);
        }

        if (_dispatcher is null)
        {
            throw new InvalidOperationException("The application loop is not running.");
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.BeginInvoke(() => Execute(body, completion));
        return completion.Task;
    }

    public static bool IsAvailable => _dispatcher is not null && _startupFailure is null;

    /// <summary>Registers the platform and runs the application loop on the calling thread until it shuts down.</summary>
    internal static void RunLoop()
    {
        try
        {
            RegisterPlatform();
            if (OperatingSystem.IsWindows())
            {
                Application.Run(() =>
                {
                    // Parked far off-screen so the loop outlives per-test windows regardless of
                    // the application's shutdown mode.
                    _keeperWindow = new Window
                    {
                        Title = "WindowAutomationTest keeper",
                        StartupLocation = WindowStartupLocation.Manual,
                        ShowInTaskbar = false,
                    };
                    _keeperWindow.MoveTo(-32000, -32000);
                    _keeperWindow.Show();

                    OnLoopStarted();
                });
            }
            else
            {
                // Explicit shutdown keeps the loop alive across per-test windows without a keeper window of its own.
                Application.Create()
                    .WithShutdownMode(ShutdownMode.OnExplicitShutdown)
                    .OnStartup(OnLoopStarted)
                    .Run();
            }
        }
        catch (Exception failure)
        {
            _startupFailure = failure;
            _loopReady.Set();
        }
    }

    private static void RegisterPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            Win32Platform.Register();
            // Direct2D unless the runner asks for the GL backend, which is the one whose
            // window-level clip and pixel-format choices the clip oracle exercises.
            if (string.Equals(Environment.GetEnvironmentVariable("MEWUI_AUTOMATION_BACKEND"), "MewVG", StringComparison.OrdinalIgnoreCase))
            {
                MewVGWin32Backend.Register();
            }
            else
            {
                Direct2DBackend.Register();
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacOSPlatform.Register();
            MewVGMacOSBackend.Register();
        }
        else
        {
            X11Platform.Register();
            MewVGX11Backend.Register();
        }
    }

    private static void OnLoopStarted()
    {
        _dispatcher = Application.Current.Dispatcher;
        _loopReady.Set();
    }

    private static async void Execute(Func<Task> body, TaskCompletionSource completion)
    {
        try
        {
            await body();
            completion.TrySetResult();
        }
        catch (Exception failure)
        {
            completion.TrySetException(failure);
        }
    }
}
