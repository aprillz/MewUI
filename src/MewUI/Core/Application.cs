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

    // Surface-kind handshake: the platform host produces one native surface family and a backend
    // consumes a specific one. Both are recorded at registration so a mismatch fails immediately
    // with a clear error rather than at the first render's surface downcast.
    private static PlatformSurfaceKind? _platformSurfaceKind;
    private static PlatformSurfaceKind? _backendSurfaceKind;
    private static string? _platformSurfaceOrigin;
    private static string? _backendSurfaceOrigin;

    private Exception? _pendingFatalException;

    // Run-scoped state (window registry, main-window identity) and its ordered teardown. Non-null only
    // for the duration of a Run; created at run start, disposed at run end.
    private ApplicationRuntime? _runtime;
    private Action<string[]>? _startup;

    /// <summary>
    /// Determines when the run loop ends automatically as windows close. Scoped to this run; configure it
    /// before the run through <see cref="AppOptions.ShutdownMode"/>, or assign it from the startup
    /// callback. Defaults to <see cref="MewUI.ShutdownMode.OnLastWindowClose"/>.
    /// </summary>
    public ShutdownMode ShutdownMode { get; set; } = ShutdownMode.OnLastWindowClose;

    /// <summary>
    /// The window whose close ends the run under <see cref="MewUI.ShutdownMode.OnMainWindowClose"/>.
    /// Set by the window-based <see cref="Run(Window)"/> overloads; assign it to promote a window opened
    /// later, which is the only way a run started without a main window can use that mode. Null clears
    /// the identity, leaving that mode with nothing to trigger on.
    /// </summary>
    public Window? MainWindow
    {
        get => _runtime?.MainWindow;
        set
        {
            if (_runtime != null)
            {
                _runtime.MainWindow = value;
            }
        }
    }
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

    private StyleSheet _styleSheet = CreateDefaultStyleSheet();

    /// <summary>
    /// Gets or sets the application-level style sheet. Named styles defined here are available to all
    /// controls as a fallback when no closer StyleSheet is found in the context chain. Replace the
    /// instance with a fully configured sheet to change application styles after live lookup begins.
    /// </summary>
    public StyleSheet StyleSheet
    {
        get => _styleSheet;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_styleSheet, value))
            {
                return;
            }

            _styleSheet = value;
            var windows = _runtime?.SnapshotWindows() ?? Array.Empty<Window>();
            for (int i = 0; i < windows.Length; i++)
            {
                windows[i].RefreshStyles(scope: null, animate: true);
            }
        }
    }

    private static StyleSheet CreateDefaultStyleSheet()
        => new() { UsesFrameworkNamedStyles = true };

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

    /// <summary>
    /// The services the running platform offers: clipboard, message boxes, file dialogs and the
    /// shell's file icons. The host behind them stays internal.
    /// </summary>
    public PlatformServices PlatformServices => _platformServices ??= new PlatformServices(PlatformHost);

    private PlatformServices? _platformServices;

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
    public IReadOnlyList<Window> AllWindows => _runtime?.Windows ?? (IReadOnlyList<Window>)Array.Empty<Window>();

    private CommandScope? _commands;
    private InputMap? _inputMap;

    /// <summary>
    /// Gets the application-level command scope, the last stage of command routing.
    /// </summary>
    public CommandScope Commands => _commands ??= new CommandScope();

    /// <summary>
    /// Gets the application-level input map, pre-populated with the standard edit gestures.
    /// </summary>
    public InputMap InputMap => _inputMap ??= CreateDefaultInputMap();

    internal static CommandScope? CurrentCommandScopeOrNull => _current?._commands;

    internal static InputMap? CurrentInputMapOrNull => _current != null ? _current.InputMap : null;

    internal Window[] SnapshotWindows() => _runtime?.SnapshotWindows() ?? Array.Empty<Window>();

    private static InputMap CreateDefaultInputMap()
    {
        // Redo has no single cross-platform gesture; Primary+Y covers Windows/Linux convention and
        // Primary+Shift+Z covers the macOS convention, both routed to the same command.
        var map = new InputMap();
        map.Map(StandardCommands.Cut, new KeyGesture(Key.X, ModifierKeys.Primary));
        map.Map(StandardCommands.Copy, new KeyGesture(Key.C, ModifierKeys.Primary));
        map.Map(StandardCommands.Paste, new KeyGesture(Key.V, ModifierKeys.Primary));
        map.Map(StandardCommands.SelectAll, new KeyGesture(Key.A, ModifierKeys.Primary));
        map.Map(StandardCommands.Undo, new KeyGesture(Key.Z, ModifierKeys.Primary));
        map.Map(StandardCommands.Redo,
            new KeyGesture(Key.Y, ModifierKeys.Primary),
            new KeyGesture(Key.Z, ModifierKeys.Primary | ModifierKeys.Shift));
        return map;
    }

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
    /// before <see cref="Run(Window)"/> uses this as a fallback. The setter is internal - backends register the factory.
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

    // Tooling assemblies decorate the platform host through this seam, registered from their own
    // composition entry point before the first host access. Registration can precede the platform
    // Register() call in Main; the wrap is applied when the host is finally resolved.
    internal static Func<IPlatformHost, IPlatformHost>? PlatformHostInterceptor { get; set; }

    internal static IPlatformHost DefaultPlatformHost
    {
        get
        {
            if (_defaultPlatformHost == null)
            {
                var host = ResolvePlatformHost();
#if DEBUG
                host = MaybeTracePlatformHost(host);
#endif
                var interceptor = PlatformHostInterceptor;
                if (interceptor != null)
                {
                    host = interceptor(host);
                }
                _defaultPlatformHost = host;
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
    /// Runs the application with the specified main window. One UI runtime per process: a second
    /// concurrent call is rejected. Running again after a previous run returns (normally or by
    /// exception) is supported - the finally block below restores process state for it.
    /// </summary>
    public static void Run(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        RunInternal(mainWindow, startup: null, shutdownMode: null);
    }

    /// <summary>
    /// Runs the application with the specified main window and invokes <paramref name="startup"/>
    /// on the UI thread after the dispatcher is installed and before the window is shown.
    /// </summary>
    public static void Run(Window mainWindow, Action startup)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(startup);
        RunInternal(mainWindow, _ => startup(), shutdownMode: null);
    }

    /// <summary>
    /// Runs the application with the specified main window and invokes <paramref name="startup"/> with the
    /// command-line arguments on the UI thread after the dispatcher is installed and before the window is shown.
    /// </summary>
    public static void Run(Window mainWindow, Action<string[]> startup)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(startup);
        RunInternal(mainWindow, startup, shutdownMode: null);
    }

    /// <summary>
    /// Runs the application without a main window and invokes <paramref name="startup"/> on the UI
    /// thread after the dispatcher is installed and before the platform message loop begins.
    /// </summary>
    public static void Run(Action startup)
    {
        ArgumentNullException.ThrowIfNull(startup);
        RunInternal(mainWindow: null, _ => startup(), shutdownMode: null);
    }

    /// <summary>
    /// Runs the application without a main window and invokes <paramref name="startup"/> with the
    /// command-line arguments on the UI thread after the dispatcher is installed and before the platform
    /// message loop begins.
    /// </summary>
    public static void Run(Action<string[]> startup)
    {
        ArgumentNullException.ThrowIfNull(startup);
        RunInternal(mainWindow: null, startup, shutdownMode: null);
    }

    internal static void RunInternal(Window? mainWindow, Action<string[]>? startup, ShutdownMode? shutdownMode)
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
                // Each run starts from a clean exit code so a previous run's Shutdown value cannot be
                // reported by this one.
                Environment.ExitCode = 0;
                var host = DefaultPlatformHost;
                app = new Application(host);
                _current = app;
                app._runtime = new ApplicationRuntime();
                app._startup = startup;
                if (shutdownMode != null)
                {
                    app.ShutdownMode = shutdownMode.Value;
                }
                _ = app.Theme;
                if (mainWindow != null)
                {
                    app._runtime.MainWindow = mainWindow;
                    app.RegisterWindow(mainWindow);
                }
                app.RunCore(mainWindow);
            }
            finally
            {
                try
                {
                    if (app != null)
                    {
                        app._startup = null;
                        // Ordered teardown of run-scoped state (drag reset then registry clear).
                        app._runtime?.Dispose();
                        app._runtime = null;
                        if (app.Dispatcher != null)
                        {
                            app.Dispatcher = null;
                        }
                        else
                        {
                            // A host may fail before installing a dispatcher. Pre-run timers still
                            // need a deterministic runtime-end notification to release their static
                            // DispatcherChanged subscription.
                            DispatcherChanged?.Invoke(null);
                        }
                    }
                    else
                    {
                        // Default host/font initialization can fail before the Application object
                        // exists. This still terminates the attempted runtime for pre-run waiters.
                        DispatcherChanged?.Invoke(null);
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

    internal void InvalidateStyleCachesForHotReload()
    {
        _styleSheet.InvalidateLazyCache();

        var windows = _runtime?.SnapshotWindows() ?? Array.Empty<Window>();
        for (int i = 0; i < windows.Length; i++)
        {
            windows[i].InvalidateStyleSheetLazyCaches();
        }
    }

    internal void RefreshStylesAfterHotReload()
    {
        var windows = _runtime?.SnapshotWindows() ?? Array.Empty<Window>();
        for (int i = 0; i < windows.Length; i++)
        {
            windows[i].RefreshStyles(scope: null, animate: false);
        }
    }

    private void ApplyThemeChange(Theme oldTheme, Theme newTheme)
    {
        var windows = _runtime?.SnapshotWindows() ?? Array.Empty<Window>();
        foreach (var window in windows)
        {
            window.BroadcastThemeChanged(oldTheme, newTheme);
        }

        ThemeChanged?.Invoke(oldTheme, newTheme);
    }

    internal void RegisterWindow(Window window) => _runtime?.Register(window);

    // The shutdown decision is owned by ApplicationRuntime (policy-driven, one place) rather than each
    // platform host; hosts only maintain their own hwnd registry for routing.
    internal void UnregisterWindow(Window window) => _runtime?.Unregister(window, ShutdownMode);

    // Pure decision so the policy is unit-testable in isolation.
    internal static bool ShouldShutdownAfterClose(ShutdownMode mode, bool wasMainWindow, int remainingWindows)
        => mode switch
        {
            ShutdownMode.OnExplicitShutdown => false,
            ShutdownMode.OnMainWindowClose => wasMainWindow,
            _ => remainingWindows == 0,
        };

    internal void OnHostLoopStarting(Window? mainWindow)
    {
        var startup = Interlocked.Exchange(ref _startup, null);
        startup?.Invoke(GetCommandLineArguments());
        mainWindow?.Show();
    }

    // The framework supplies the arguments rather than the caller, so startup logic assembled outside the
    // entry point still receives them. Matches what a Main(string[] args) sees: no executable path.
    private static string[] GetCommandLineArguments()
    {
        var arguments = Environment.GetCommandLineArgs();
        return arguments.Length > 1 ? arguments[1..] : [];
    }

    private void RunCore(Window? mainWindow)
    {
        PlatformHost.Run(this, mainWindow);

        var fatal = Interlocked.Exchange(ref _pendingFatalException, null);
        if (fatal != null)
        {
            throw new InvalidOperationException("Unhandled exception in UI loop.", fatal);
        }
    }

    /// <summary>
    /// Ends the run loop with exit code 0. Does nothing when no run is in progress.
    /// </summary>
    // Separate from the exit-code overload rather than an optional parameter, so the method group still
    // converts to Action for command and event handlers.
    public static void Shutdown() => Shutdown(0);

    /// <summary>
    /// Ends the run loop and sets the process exit code. Does nothing when no run is in progress.
    /// </summary>
    /// <param name="exitCode">Value assigned to <see cref="Environment.ExitCode"/>.</param>
    public static void Shutdown(int exitCode)
    {
        // Assigned before the exit request so the code survives a fatal exception rethrown from Run, and
        // outside the running check so a request that raced the end of the loop still reports its code.
        Environment.ExitCode = exitCode;

        // Read once: a run ending on another thread would otherwise null the field between the two uses.
        var app = _current;
        app?.PlatformHost.Quit(app);
    }

    /// <summary>
    /// Dispatches pending messages in the message queue.
    /// </summary>
    [Obsolete("DoEvents will be removed. Await asynchronous work or use the dispatcher; for synchronous modal UI use Window.ShowDialog.")]
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
    /// <paramref name="requiredSurface"/> is the native surface family the backend needs, checked against
    /// the registered platform host.
    /// </summary>
    internal static void RegisterGraphicsFactory(Func<IGraphicsFactory> factory, PlatformSurfaceKind requiredSurface, string origin)
    {
        ArgumentNullException.ThrowIfNull(factory);
        EnsureNotRunning("graphics backend");

        var existing = Interlocked.CompareExchange(ref _graphicsFactoryProvider, factory, null);
        if (existing != null && existing != factory)
        {
            throw new InvalidOperationException("A graphics backend is already registered. Register only one per process.");
        }

        _backendSurfaceKind = requiredSurface;
        _backendSurfaceOrigin = origin;
        VerifySurfaceKindMatch();
    }

    /// <summary>
    /// Registers the platform host. Platform packages call this once at startup; only one is allowed per process.
    /// <paramref name="surface"/> is the native surface family the host produces, checked against the
    /// registered graphics backend. <paramref name="systemFontFamily"/> is the platform's system UI font,
    /// taken here rather than from the host instance so themes resolve fonts before the host is created.
    /// </summary>
    internal static void RegisterPlatformHost(Func<IPlatformHost> factory, PlatformSurfaceKind surface, string origin,
        string systemFontFamily)
    {
        ArgumentNullException.ThrowIfNull(factory);
        EnsureNotRunning("platform host");

        var existing = Interlocked.CompareExchange(ref _platformHostProvider, factory, null);
        if (existing != null && existing != factory)
        {
            throw new InvalidOperationException("A platform host is already registered. Register only one per process.");
        }

        ThemeMetrics.PlatformFontFamily = systemFontFamily;
        _platformSurfaceKind = surface;
        _platformSurfaceOrigin = origin;
        VerifySurfaceKindMatch();
    }

    // Fails a mismatched platform/backend pair as soon as both are registered (order-independent),
    // rather than deferring to the first render where the backend downcasts the platform surface.
    private static void VerifySurfaceKindMatch()
        => ValidateSurfaceKinds(_platformSurfaceKind, _backendSurfaceKind, _platformSurfaceOrigin, _backendSurfaceOrigin);

    // Pure check (no static state) so the compatibility rule can be tested in isolation.
    internal static void ValidateSurfaceKinds(
        PlatformSurfaceKind? platformSurface, PlatformSurfaceKind? backendSurface,
        string? platformOrigin, string? backendOrigin)
    {
        if (platformSurface is not PlatformSurfaceKind platform ||
            backendSurface is not PlatformSurfaceKind backend ||
            platform == backend)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Incompatible platform and graphics backend: the {backendOrigin} backend needs a " +
            $"{backend} window surface but the {platformOrigin} platform host produces a {platform} surface. " +
            "Register a matching platform/backend pair (e.g. Win32 + Direct2D, X11 + MewVG.X11).");
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

    internal static void RouteLifecycleException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        var app = _current;
        if (app == null)
        {
            DiagLog.Write($"[lifecycle] {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (app.TryHandleDispatcherException(ex))
        {
            return;
        }

        app.NotifyFatalDispatcherException(ex);
        try
        {
            app.PlatformHost.Quit(app);
        }
        catch (Exception quitException)
        {
            // The original lifecycle exception remains the fatal error. Shutdown is best-effort
            // here because this path is commonly entered from an OS callback boundary.
            DiagLog.Write($"[lifecycle] Quit failed: {quitException.GetType().Name}: {quitException.Message}");
        }
    }

    private static void ApplyPlatformFontDefaults(IPlatformHost host)
    {
        // Normally already set by RegisterPlatformHost; repeated here for hosts swapped by an interceptor.
        ThemeMetrics.PlatformFontFamily = host.DefaultFontFamily;

        Rendering.FontFallback.ApplyPlatformDefaults(host.DefaultFontFallbacks);
    }

#if DEBUG
    private static IPlatformHost MaybeTracePlatformHost(IPlatformHost host)
    {
        if (!DiagLog.Enabled)
        {
            return host;
        }

        return host is TracingPlatformHost ? host : new TracingPlatformHost(host);
    }
#endif
}
