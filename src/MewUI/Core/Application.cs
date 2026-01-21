using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Linux.X11;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.OpenGL;

namespace Aprillz.MewUI;

/// <summary>
/// Represents the main application entry point and message loop.
/// </summary>
public sealed class Application
{
    private static Application? _current;
    private static readonly object _syncLock = new();

    private static GraphicsBackend _defaultGraphicsBackend = OperatingSystem.IsWindows()
        ? GraphicsBackend.Direct2D
        : GraphicsBackend.OpenGL;

    private static IGraphicsFactory? _defaultGraphicsFactoryOverride;
    private static IPlatformHost _defaultPlatformHost = CreateDefaultPlatformHost();
    private static Exception? _pendingFatalException;
    private IUiDispatcher? _dispatcher;
    private readonly List<Window> _windows = new();

    /// <summary>
    /// Raised when an exception escapes from the platform message loop or window procedure.
    /// Set <see cref="UiUnhandledExceptionEventArgs.Handled"/> to true to continue.
    /// </summary>
    public static event EventHandler<UiUnhandledExceptionEventArgs>? UiUnhandledException;

    /// <summary>
    /// Gets the current application instance.
    /// </summary>
    public static Application Current => _current ?? throw new InvalidOperationException("Application not initialized. Call Application.Run() first.");

    /// <summary>
    /// Gets whether an application instance is running.
    /// </summary>
    public static bool IsRunning => _current != null;

    public IPlatformHost PlatformHost { get; }

    internal static event Action<IUiDispatcher?>? DispatcherChanged;

    public IUiDispatcher? Dispatcher
    {
        get => _dispatcher;
        internal set
        {
            if (_dispatcher == value)
            {
                return;
            }

            _dispatcher = value;
            DispatcherChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Gets currently tracked windows for this application instance.
    /// </summary>
    public IReadOnlyList<Window> AllWindows => _windows;

    /// <summary>
    /// Gets the selected graphics backend used by windows/controls.
    /// </summary>
    public static GraphicsBackend DefaultGraphicsBackend
    {
        get => _defaultGraphicsBackend;
        set
        {
            _defaultGraphicsBackend = value;
            _defaultGraphicsFactoryOverride = null;
        }
    }

    /// <summary>
    /// Gets or sets the default graphics factory used by windows/controls.
    /// Prefer <see cref="DefaultGraphicsBackend"/> for built-in backends.
    /// </summary>
    public static IGraphicsFactory DefaultGraphicsFactory
    {
        get => _defaultGraphicsFactoryOverride ?? GetFactoryForBackend(_defaultGraphicsBackend);
        set
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            // Keep existing code working, but prefer enum configuration.
            if (ReferenceEquals(value, Direct2DGraphicsFactory.Instance))
            {
                DefaultGraphicsBackend = GraphicsBackend.Direct2D;
                return;
            }

            if (ReferenceEquals(value, GdiGraphicsFactory.Instance))
            {
                DefaultGraphicsBackend = GraphicsBackend.Gdi;
                return;
            }

            _defaultGraphicsFactoryOverride = value;
        }
    }

    public static IPlatformHost DefaultPlatformHost
    {
        get => _defaultPlatformHost ??= CreateDefaultPlatformHost();
        set => _defaultPlatformHost = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the graphics factory used by windows/controls for this application instance.
    /// </summary>
    public IGraphicsFactory GraphicsFactory
    {
        get => DefaultGraphicsFactory;
        set => DefaultGraphicsFactory = value;
    }

    /// <summary>
    /// Runs the application with the specified main window.
    /// </summary>
    public static void Run(Window mainWindow)
    {
        if (_current != null)
        {
            throw new InvalidOperationException("Application is already running.");
        }

        lock (_syncLock)
        {
            if (_current != null)
            {
                throw new InvalidOperationException("Application is already running.");
            }

            var app = new Application(DefaultPlatformHost);
            _current = app;
            _pendingFatalException = null;
            app.RegisterWindow(mainWindow);
            app.RunCore(mainWindow);
        }
    }

    private Application(IPlatformHost platformHost) => PlatformHost = platformHost;

    internal void RegisterWindow(Window window)
    {
        if (_windows.Contains(window))
        {
            return;
        }

        _windows.Add(window);
    }

    internal void UnregisterWindow(Window window)
    {
        _windows.Remove(window);
    }

    private void RunCore(Window mainWindow)
    {
        PlatformHost.Run(this, mainWindow);
        _current = null;

        var fatal = Interlocked.Exchange(ref _pendingFatalException, null);
        if (fatal != null)
        {
            throw new InvalidOperationException("Unhandled exception in UI loop.", fatal);
        }
    }

    /// <summary>
    /// Quits the application.
    /// </summary>
    public static void Quit()
    {
        if (_current == null)
        {
            return;
        }

        _current.PlatformHost.Quit(_current);
    }

    /// <summary>
    /// Dispatches pending messages in the message queue.
    /// </summary>
    public static void DoEvents()
    {
        if (_current == null)
        {
            return;
        }

        _current.PlatformHost.DoEvents();
    }

    private static IGraphicsFactory GetFactoryForBackend(GraphicsBackend backend) => backend switch
    {
        GraphicsBackend.OpenGL => OpenGLGraphicsFactory.Instance,
        GraphicsBackend.Direct2D => OperatingSystem.IsWindows()
            ? Direct2DGraphicsFactory.Instance
            : throw new PlatformNotSupportedException("Direct2D backend is Windows-only. Use OpenGL on Linux."),
        GraphicsBackend.Gdi => OperatingSystem.IsWindows()
            ? GdiGraphicsFactory.Instance
            : throw new PlatformNotSupportedException("GDI backend is Windows-only. Use OpenGL on Linux."),
        _ => OperatingSystem.IsWindows() ? Direct2DGraphicsFactory.Instance : OpenGLGraphicsFactory.Instance,
    };

    internal static bool TryHandleUiException(Exception ex)
    {
        try
        {
            var args = new UiUnhandledExceptionEventArgs(ex);
            UiUnhandledException?.Invoke(null, args);
            return args.Handled;
        }
        catch
        {
            // If the handler itself throws, treat as unhandled.
            return false;
        }
    }

    internal static void NotifyFatalUiException(Exception ex)
        => Interlocked.CompareExchange(ref _pendingFatalException, ex, null);

    private static IPlatformHost CreateDefaultPlatformHost()
    {
        if (OperatingSystem.IsWindows())
        {
            return new Win32PlatformHost();
        }

        if (OperatingSystem.IsLinux())
        {
            return new X11PlatformHost();
        }

        throw new PlatformNotSupportedException("MewUI currently supports Windows and (experimental) Linux hosts only.");
    }
}