using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Represents the main application entry point and message loop.
/// </summary>
public sealed class Application
{
    private static Application? _current;
    private static readonly object _syncLock = new();

    private static Func<IGraphicsFactory>? _graphicsFactoryProvider;
    private static IGraphicsFactory? _defaultGraphicsFactory;
    private static Func<IPlatformHost>? _platformHostProvider;
    private static IPlatformHost? _defaultPlatformHost;

    private Exception? _pendingFatalException;

    private readonly List<Window> _windows = new();
    private readonly ThemeManager _themeManager;
    private readonly RenderLoopSettings _renderLoopSettings = new();
    private IGraphicsFactory? _graphicsFactory;

    /// <summary>
    /// Raised when an exception escapes from the UI dispatcher work queue.
    /// Set <see cref="DispatcherUnhandledExceptionEventArgs.Handled"/> to true to continue.
    /// </summary>
    public static event Action<DispatcherUnhandledExceptionEventArgs>? DispatcherUnhandledException;

    /// <summary>
    /// Gets the current application instance.
    /// </summary>
    public static Application Current => _current ?? throw new InvalidOperationException("Application not initialized. Call Application.Run() first.");

    /// <summary>
    /// Gets the currently active theme.
    /// </summary>
    public Theme Theme => _themeManager.CurrentTheme;

    /// <summary>
    /// Gets the application-level style sheet. Named styles defined here are available to all controls
    /// as a fallback when no closer StyleSheet is found in the visual tree.
    /// </summary>
    public StyleSheet StyleSheet { get; } = CreateDefaultStyleSheet();

    private static StyleSheet CreateDefaultStyleSheet()
    {
        var sheet = new StyleSheet();
        BuiltInStyles.Register(sheet);
        return sheet;
    }

    /// <summary>
    /// Gets the render loop settings controlling frame scheduling.
    /// </summary>
    public RenderLoopSettings RenderLoopSettings => _renderLoopSettings;

    /// <summary>
    /// Raised when the theme changes.
    /// </summary>
    public event Action<Theme, Theme>? ThemeChanged;

    /// <summary>
    /// Raised when the theme mode changes.
    /// </summary>
    public event Action? ThemeModeChanged;

    public ThemeVariant ThemeMode => _themeManager.Mode;

    public void SetTheme(ThemeVariant mode)
    {
        var lastMode = _themeManager.Mode;

        var change = _themeManager.SetTheme(mode);
        if (change.Changed)
        {
            ApplyThemeChange(change.OldTheme, change.NewTheme);
        }

        if (lastMode != mode)
        {
            ThemeModeChanged?.Invoke();
        }
    }

    public void SetThemeMode(ThemeVariant mode)
    {
        var lastMode = _themeManager.Mode;

        var change = _themeManager.SetTheme(mode);
        if (change.Changed)
        {
            ApplyThemeChange(change.OldTheme, change.NewTheme);
        }

        if (lastMode != mode)
        {
            ThemeModeChanged?.Invoke();
        }
    }

    public void SetAccent(Accent accent, Color? accentText = null)
    {
        var change = _themeManager.SetAccent(accent, accentText);
        if (change.Changed)
        {
            ApplyThemeChange(change.OldTheme, change.NewTheme);
        }
    }

    public void SetAccent(Color accent, Color? accentText = null)
    {
        var change = _themeManager.SetAccent(accent, accentText);
        if (change.Changed)
        {
            ApplyThemeChange(change.OldTheme, change.NewTheme);
        }
    }

    /// <summary>
    /// Gets whether an application instance is running.
    /// </summary>
    public static bool IsRunning => _current != null;

    /// <summary>
    /// Gets the active platform host responsible for windowing and input.
    /// </summary>
    internal IPlatformHost PlatformHost { get; }

    internal static event Action<IDispatcher?>? DispatcherChanged;

    public IDispatcher? Dispatcher
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
    /// This is derived from <see cref="DefaultGraphicsFactory"/> and exists mainly for diagnostics.
    /// </summary>
    public static string SelectedGraphicsBackend
    {
        get
        {
            try
            {
                return DefaultGraphicsFactory.Backend;
            }
            catch
            {
                return "Unknown";
            }
        }
    }

    /// <summary>
    /// Gets or sets the default graphics factory used by windows/controls.
    /// In trim/AOT-friendly setups, backend packages register factories via <see cref="RegisterGraphicsFactory"/>.
    /// </summary>
    /// <summary>
    /// Gets the default graphics factory (the pre-<see cref="Current"/> reference). Rendering code that may run
    /// before <see cref="Run"/> uses this as a fallback. The setter is internal - backends register the factory.
    /// </summary>
    public static IGraphicsFactory DefaultGraphicsFactory
    {
        // Application owns the single process-wide factory: the provider is invoked once and cached (no
        // per-class singleton). It is process-scoped, so it is not cleared or disposed across runs.
        get => _defaultGraphicsFactory ??= (_graphicsFactoryProvider ?? throw new InvalidOperationException(
            "No graphics backend registered. Add a backend package (Aprillz.MewUI.Backend.Direct2D / Gdi / MewVG.*)."))();
        internal set
        {
            ArgumentNullException.ThrowIfNull(value);
            EnsureNotRunning("graphics backend");
            _defaultGraphicsFactory = value;
        }
    }

    internal static IPlatformHost DefaultPlatformHost
    {
        get
        {
            if (_defaultPlatformHost == null)
            {
                _defaultPlatformHost = MaybeTracePlatformHost(ResolvePlatformHost());
                ApplyPlatformFontDefaults(_defaultPlatformHost);
            }

            return _defaultPlatformHost;
        }
    }

    /// <summary>
    /// Gets the graphics factory bound to this running application instance (captured on first access).
    /// </summary>
    public IGraphicsFactory GraphicsFactory => _graphicsFactory ??= DefaultGraphicsFactory;

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

            Application? app = null;
            try
            {
                var host = DefaultPlatformHost;
                app = new Application(host);
                _current = app;
                _ = app.Theme;
                app.RegisterWindow(mainWindow);
                app.RunCore(mainWindow);
            }
            finally
            {
                try
                {
                    if (app != null)
                    {
                        app._windows.Clear();
                        app.Dispatcher = null;
                    }
                }
                finally
                {
                    _current = null;

                    // Platform hosts are run-scoped. Clear the process reference before disposing so a
                    // throwing Dispose cannot strand a stale host and prevent the next Application.Run.
                    var host = Interlocked.Exchange(ref _defaultPlatformHost, null);
                    host?.Dispose();
                }
            }
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
        {
            ApplyThemeChange(change.OldTheme, change.NewTheme);
        }
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

    private static IPlatformHost ResolvePlatformHost()
        => (_platformHostProvider
            ?? throw new InvalidOperationException(
                "No platform host registered. Add a platform package (Aprillz.MewUI.Platform.Win32 / X11 / MacOS)."))();

    private static void EnsureNotRunning(string what)
    {
        if (_current != null)
        {
            throw new InvalidOperationException($"Cannot change the {what} while the application is running.");
        }
    }

    /// <summary>
    /// Registers the graphics backend. Backend packages call this once at startup; only one is allowed per process.
    /// </summary>
    internal static void RegisterGraphicsFactory(Func<IGraphicsFactory> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        EnsureNotRunning("graphics backend");

        var existing = Interlocked.CompareExchange(ref _graphicsFactoryProvider, factory, null);
        if (existing != null && existing != factory)
        {
            throw new InvalidOperationException("A graphics backend is already registered. Register only one per process.");
        }
    }

    /// <summary>
    /// Registers the platform host. Platform packages call this once at startup; only one is allowed per process.
    /// </summary>
    internal static void RegisterPlatformHost(Func<IPlatformHost> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        EnsureNotRunning("platform host");

        var existing = Interlocked.CompareExchange(ref _platformHostProvider, factory, null);
        if (existing != null && existing != factory)
        {
            throw new InvalidOperationException("A platform host is already registered. Register only one per process.");
        }
    }

    internal bool TryHandleDispatcherException(Exception ex)
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

    internal void NotifyFatalDispatcherException(Exception ex)
        => Interlocked.CompareExchange(ref _pendingFatalException, ex, null);

    private static void ApplyPlatformFontDefaults(IPlatformHost host)
    {
        var fontFamily = host.DefaultFontFamily;
        if (string.IsNullOrEmpty(fontFamily))
        {
            return;
        }

        ThemeMetrics.DefaultFontFamily = fontFamily;

        var metrics = ThemeManager.DefaultMetrics;
        if (metrics.FontFamily != fontFamily)
        {
            ThemeManager.DefaultMetrics = metrics with { FontFamily = fontFamily };
        }

        // Apply platform default font fallback chain (same pattern as DefaultFontFamily).
        Rendering.FontFallback.ApplyPlatformDefaults(host.DefaultFontFallbacks);
    }

    private static IPlatformHost MaybeTracePlatformHost(IPlatformHost host)
    {
        if (!DiagLog.Enabled)
        {
            return host;
        }

        return host is TracingPlatformHost ? host : new TracingPlatformHost(host);
    }
}
