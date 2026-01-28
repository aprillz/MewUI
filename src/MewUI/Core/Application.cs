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
    private Exception? _pendingFatalException;

    private readonly List<Window> _windows = new();
    private readonly ThemeManager _themeManager;
    private readonly RenderLoopSettings _renderLoopSettings = new();

    /// <summary>
    /// Raised when an exception escapes from the UI dispatcher work queue.
    /// Set <see cref="DispatcherUnhandledExceptionEventArgs.Handled"/> to true to continue.
    /// </summary>
    public static event Action<DispatcherUnhandledExceptionEventArgs>? DispatcherUnhandledException;

    /// <summary>
    /// Gets the current application instance.
    /// </summary>
    public static Application Current => _current ?? throw new InvalidOperationException("Application not initialized. Call Application.Run() first.");

    public Theme Theme => _themeManager.CurrentTheme;

    public RenderLoopSettings RenderLoopSettings => _renderLoopSettings;

    public event Action<Theme, Theme>? ThemeChanged;

    internal ThemeVariant ThemeMode => _themeManager.Mode;

    public void SetTheme(ThemeVariant mode)
    {
        var change = _themeManager.SetTheme(mode);
        if (change.Changed)
        {
            ApplyThemeChange(change.OldTheme, change.NewTheme);
        }
    }

    public void SetThemeMode(ThemeVariant mode)
    {
        var change = _themeManager.SetTheme(mode);
        if (change.Changed)
            ApplyThemeChange(change.OldTheme, change.NewTheme);
    }

    public void SetAccent(Accent accent, Color? accentText = null)
    {
        var change = _themeManager.SetAccent(accent, accentText);
        if (change.Changed)
            ApplyThemeChange(change.OldTheme, change.NewTheme);
    }

    public void SetAccent(Color accent, Color? accentText = null)
    {
        var change = _themeManager.SetAccent(accent, accentText);
        if (change.Changed)
            ApplyThemeChange(change.OldTheme, change.NewTheme);
    }

    /// <summary>
    /// Gets whether an application instance is running.
    /// </summary>
    public static bool IsRunning => _current != null;

    public IPlatformHost PlatformHost { get; }

    internal static event Action<IUiDispatcher?>? DispatcherChanged;

    public IUiDispatcher? Dispatcher
    {
        get; internal set
        {
            if (field == value)
            {
                return;
            }

            field = value;
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
            if (value == Direct2DGraphicsFactory.Instance)
            {
                DefaultGraphicsBackend = GraphicsBackend.Direct2D;
                return;
            }

            if (value == GdiGraphicsFactory.Instance)
            {
                DefaultGraphicsBackend = GraphicsBackend.Gdi;
                return;
            }

            _defaultGraphicsFactoryOverride = value;
        }
    }

    public static IPlatformHost DefaultPlatformHost
    {
        get => field ??= CreateDefaultPlatformHost();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
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
            _ = app.Theme;
            app.RegisterWindow(mainWindow);
            app.RunCore(mainWindow);
        }
    }

    public static ApplicationBuilder Create() => new ApplicationBuilder(new AppOptions());

    private Application(IPlatformHost platformHost)
    {
        PlatformHost = platformHost;
        _themeManager = new ThemeManager(platformHost, ThemeManager.Default);
    }

    internal void NotifySystemThemeChanged()
    {
        var change = _themeManager.ApplySystemThemeChanged();
        if (change.Changed)
            ApplyThemeChange(change.OldTheme, change.NewTheme);
    }

    private void ApplyThemeChange(Theme oldTheme, Theme newTheme)
    {
        foreach (var window in AllWindows)
        {
            window.BroadcastThemeChanged(oldTheme, newTheme);
        }

        ThemeChanged?.Invoke(oldTheme, newTheme);
    }

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

    public bool TryHandleDispatcherException(Exception ex)
    {
        try
        {
            var args = new DispatcherUnhandledExceptionEventArgs(ex);
            DispatcherUnhandledException?.Invoke(args);
            return args.Handled;
        }
        catch
        {
            // If the handler itself throws, treat as unhandled.
            return false;
        }
    }

    public void NotifyFatalDispatcherException(Exception ex)
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