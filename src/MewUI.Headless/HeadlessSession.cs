using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Headless;

/// <summary>
/// Runs an <see cref="Application"/> on a dedicated UI thread without a display server; one session
/// exists per process at a time and disposing it ends the run.
/// </summary>
public sealed class HeadlessSession : IDisposable
{
    private const string HOST_ORIGIN = "Headless";
    private const int SHUTDOWN_TIMEOUT_MS = 30_000;

    private static readonly object _gate = new();
    private static readonly Func<IPlatformHost> _hostFactory = ResolveHost;
    private static HeadlessSession? _current;

    private readonly HeadlessPlatformHost _host;
    private readonly IGraphicsFactory _backendFactory;
    private readonly MeasureOnlyGraphicsFactory? _measureOnlyFactory;
    private readonly bool _previousPreferNativePopups;
    private readonly bool _previousRendersWithoutDisplay;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly Thread _thread;
    private readonly object _pendingGate = new();
    private readonly Dictionary<long, Action<Exception>> _pendingFailures = new();
    private long _nextPendingId;
    private Exception? _runFailure;
    private bool _disposed;

    private HeadlessSession(HeadlessOptions options, IGraphicsFactory backendFactory, MeasureOnlyGraphicsFactory? measureOnlyFactory,
        bool previousPreferNativePopups, bool previousRendersWithoutDisplay)
    {
        Options = options;
        _backendFactory = backendFactory;
        _measureOnlyFactory = measureOnlyFactory;
        _previousPreferNativePopups = previousPreferNativePopups;
        _previousRendersWithoutDisplay = previousRendersWithoutDisplay;
        Clipboard = new HeadlessClipboard();
        MessageBoxes = new HeadlessMessageBoxes();
        FileDialogs = new HeadlessFileDialogs();
        _host = new HeadlessPlatformHost(options, new HeadlessTimeSource(options.Clock == HeadlessClock.Virtual),
            options.DefaultFontFamily ?? DefaultSystemFontFamily(), Clipboard, MessageBoxes, FileDialogs);
        _thread = new Thread(RunApplication)
        {
            Name = "MewUI headless UI thread",
            IsBackground = true,
        };
    }

    /// <summary>Gets the running session, or null when none is running.</summary>
    public static HeadlessSession? Current => Volatile.Read(ref _current);

    /// <summary>Gets the options the session was started with.</summary>
    public HeadlessOptions Options { get; }

    /// <summary>Gets the in-memory clipboard the running application reads and writes.</summary>
    public HeadlessClipboard Clipboard { get; }

    /// <summary>Gets the scripted answers for native message boxes.</summary>
    public HeadlessMessageBoxes MessageBoxes { get; }

    /// <summary>Gets the scripted answers for native file dialogs.</summary>
    public HeadlessFileDialogs FileDialogs { get; }

    /// <summary>Gets whether the caller is on the session's UI thread.</summary>
    public bool IsOnUIThread => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    /// <summary>
    /// Starts a session with <paramref name="options"/> and returns once the application runs; throws
    /// <see cref="InvalidOperationException"/> when a session is already running or no graphics backend is
    /// registered, and <see cref="PlatformNotSupportedException"/> when offscreen rendering is asked for where
    /// it is not available.
    /// </summary>
    public static HeadlessSession Start(HeadlessOptions? options = null)
    {
        options ??= new HeadlessOptions();
        if (options.Dpi == 0)
        {
            throw new ArgumentException("Dpi must be greater than zero.", nameof(options));
        }

        lock (_gate)
        {
            if (_current != null)
            {
                throw new InvalidOperationException("A headless session is already running; dispose it before starting another.");
            }

            if (Application.IsRunning)
            {
                throw new InvalidOperationException("An application is already running in this process.");
            }

            var surfaceKind = Application.RegisteredBackendSurfaceKind
                ?? throw new InvalidOperationException("No graphics backend is registered. Call the backend's Register method (GdiBackend.Register(), Direct2DBackend.Register(), MewVGX11Backend.Register(), MewVGMacOSBackend.Register()) before starting a headless session.");
            var backendFactory = Application.DefaultGraphicsFactory;

            MeasureOnlyGraphicsFactory? measureOnlyFactory = null;
            if (options.Rendering == HeadlessRendering.MeasureOnly)
            {
                measureOnlyFactory = new MeasureOnlyGraphicsFactory(backendFactory);
            }

            var session = new HeadlessSession(options, backendFactory, measureOnlyFactory, PopupManager.PreferNativePopups,
                Application.RendersWithoutDisplay);
            // Repeating the registration with the same delegate is accepted; it refreshes the font family the themes use.
            Application.RegisterPlatformHost(_hostFactory, surfaceKind, HOST_ORIGIN, session._host.DefaultFontFamily);
            _current = session;
            try
            {
                session.StartThread();
            }
            catch
            {
                session.RestoreProcessState();
                _current = null;
                throw;
            }

            return session;
        }
    }

    private static IPlatformHost ResolveHost()
        => Current?._host ?? throw new InvalidOperationException("No headless session is running.");

    private static string DefaultSystemFontFamily()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Segoe UI";
        }

        if (OperatingSystem.IsMacOS())
        {
            return ".AppleSystemUIFont";
        }

        return "sans-serif";
    }

    private void StartThread()
    {
        if (_measureOnlyFactory != null)
        {
            Application.DefaultGraphicsFactory = _measureOnlyFactory;
        }

        // Native popup windows cannot exist without a display; popups open inside the owner's surface.
        PopupManager.PreferNativePopups = false;
        // A GPU backend that renders offscreen through a display connection brings its own device instead.
        Application.RendersWithoutDisplay = Options.Rendering == HeadlessRendering.Offscreen;

        _thread.Start();
        _started.Wait();
        if (_runFailure != null)
        {
            _thread.Join();
            if (_runFailure is PlatformNotSupportedException unsupported)
            {
                throw new PlatformNotSupportedException(unsupported.Message, unsupported);
            }

            throw new InvalidOperationException("The headless application failed to start.", _runFailure);
        }
    }

    private void RunApplication()
    {
        try
        {
            Application.Create()
                .WithShutdownMode(ShutdownMode.OnExplicitShutdown)
                .OnStartup(() =>
                {
                    ProbeOffscreenRendering();
                    _started.Set();
                })
                .Run();
        }
        catch (Exception ex)
        {
            _runFailure = ex;
        }
        finally
        {
            _started.Set();
            FailPendingWork();
        }
    }

    // A backend that cannot draw offscreen here (no EGL on Linux, say) fails the start with its own
    // message rather than the first frame inside the loop.
    private void ProbeOffscreenRendering()
    {
        if (Options.Rendering == HeadlessRendering.Offscreen)
        {
            _backendFactory.AcquireBackgroundRenderScope().Dispose();
        }
    }

    // Work posted before the loop ended would otherwise wait forever for a result.
    private void FailPendingWork()
    {
        Action<Exception>[] pending;
        lock (_pendingGate)
        {
            pending = _pendingFailures.Values.ToArray();
            _pendingFailures.Clear();
        }

        var failure = _runFailure != null
            ? new InvalidOperationException("The headless application loop ended with an unhandled exception.", _runFailure)
            : new InvalidOperationException("The headless application loop has ended.");
        for (int index = 0; index < pending.Length; index++)
        {
            pending[index](failure);
        }
    }

    private void RestoreProcessState()
    {
        PopupManager.PreferNativePopups = _previousPreferNativePopups;
        Application.RendersWithoutDisplay = _previousRendersWithoutDisplay;
        if (_measureOnlyFactory != null && !Application.IsRunning)
        {
            Application.DefaultGraphicsFactory = _backendFactory;
        }
        _host.Dispose();
        _started.Dispose();
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread and returns when it completed; its exception is rethrown on the caller.</summary>
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Run(() =>
        {
            action();
            return true;
        });
    }

    /// <summary>Runs <paramref name="function"/> on the UI thread and returns its result; its exception is rethrown on the caller.</summary>
    public T Run<T>(Func<T> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (IsOnUIThread)
        {
            return function();
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                completion.TrySetResult(function());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, failure => completion.TrySetException(failure));

        return completion.Task.GetAwaiter().GetResult();
    }

    /// <summary>Runs <paramref name="function"/> on the UI thread; every await inside it resumes on that thread.</summary>
    public Task RunAsync(Func<Task> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return RunAsync(async () =>
        {
            await function().ConfigureAwait(true);
            return true;
        });
    }

    /// <summary>Runs <paramref name="function"/> on the UI thread and returns its result; every await inside it resumes on that thread.</summary>
    public Task<T> RunAsync<T>(Func<Task<T>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async () =>
        {
            try
            {
                completion.TrySetResult(await function().ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, failure => completion.TrySetException(failure));

        return completion.Task;
    }

    /// <summary>Drains queued dispatcher work and due timers, then lays out and draws every window that asked for it.</summary>
    public void RunJobs() => Run(_host.RunJobs);

    /// <summary>
    /// Moves the virtual clock forward by <paramref name="duration"/>, running the timers and animation
    /// frames that fall due; throws <see cref="InvalidOperationException"/> on a <see cref="HeadlessClock.RealTime"/> session.
    /// </summary>
    public void AdvanceTime(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "The duration must not be negative.");
        }

        if (!_host.Clock.IsVirtual)
        {
            throw new InvalidOperationException("AdvanceTime needs a session started with HeadlessClock.Virtual.");
        }

        Run(() => _host.AdvanceTime(duration));
    }

    private void Post(Action action, Action<Exception> fail)
    {
        ThrowIfUnusable();
        var dispatcher = _host.Dispatcher ?? throw new InvalidOperationException("The headless session is not running.");

        long id;
        lock (_pendingGate)
        {
            id = ++_nextPendingId;
            _pendingFailures[id] = fail;
        }

        dispatcher.BeginInvoke(() =>
        {
            try
            {
                action();
            }
            finally
            {
                lock (_pendingGate)
                {
                    _pendingFailures.Remove(id);
                }
            }
        });

        // The loop can end between the liveness check and the post; the failure sweep then ran without this entry.
        if (!_thread.IsAlive)
        {
            FailPendingWork();
        }
    }

    private void ThrowIfUnusable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runFailure != null)
        {
            throw new InvalidOperationException("The headless application loop ended with an unhandled exception.", _runFailure);
        }

        if (!_thread.IsAlive)
        {
            throw new InvalidOperationException("The headless application loop has ended.");
        }
    }

    /// <summary>Closes every window, ends the application run and releases the UI thread.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (_thread.IsAlive && _host.Dispatcher is HeadlessDispatcher dispatcher)
                {
                    dispatcher.BeginInvoke(CloseWindowsAndShutdown);
                    if (!_thread.Join(SHUTDOWN_TIMEOUT_MS))
                    {
                        throw new TimeoutException("The headless UI thread did not stop after the application was asked to shut down.");
                    }
                }
            }
            finally
            {
                _current = null;
                RestoreProcessState();
            }
        }
    }

    private static void CloseWindowsAndShutdown()
    {
        if (Application.IsRunning)
        {
            var windows = Application.Current.AllWindows.ToArray();
            for (int index = windows.Length - 1; index >= 0; index--)
            {
                windows[index].Close();
            }
        }

        Application.Shutdown();
    }
}
