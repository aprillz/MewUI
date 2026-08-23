using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Hosts one real application loop for the whole test assembly: a dedicated STA thread runs
/// <see cref="Application.Run(Action)"/> against the actual Win32 platform and Direct2D backend,
/// so tests exercise real HWNDs, real WM_* traffic, and real per-monitor DPI. Test bodies are
/// marshaled onto that thread with <see cref="RunAsync"/>; awaits inside them resume on it via
/// the platform's synchronization context.
/// </summary>
[TestClass]
public static class RealAppSession
{
    private static Thread? _uiThread;
    private static IDispatcher? _dispatcher;
    private static Window? _keeperWindow;
    private static Exception? _startupFailure;

    [AssemblyInitialize]
    public static void StartApplication(TestContext _)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var ready = new ManualResetEventSlim();

        _uiThread = new Thread(() =>
        {
            try
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

                    _dispatcher = Application.Current.Dispatcher;
                    ready.Set();
                });
            }
            catch (Exception failure)
            {
                _startupFailure = failure;
                ready.Set();
            }
        });
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.IsBackground = true;
        _uiThread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(15)))
        {
            _startupFailure = new TimeoutException("The application loop did not come up.");
        }
    }

    [AssemblyCleanup]
    public static void StopApplication()
    {
        if (_dispatcher is null)
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
