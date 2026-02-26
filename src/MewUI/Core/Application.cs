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
    private static IGraphicsFactory? _defaultGraphicsFactoryOverride;
    private static string? _defaultGraphicsFactoryId;
    private static readonly Dictionary<string, Func<IGraphicsFactory>> _graphicsFactoriesById = new(StringComparer.OrdinalIgnoreCase);
    private static string? _defaultPlatformHostId;
    private static readonly Dictionary<string, Func<IPlatformHost>> _platformHostsById = new(StringComparer.OrdinalIgnoreCase);
    private static IPlatformHost? _defaultPlatformHostOverride;

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

    /// <summary>
    /// Gets the currently active theme.
    /// </summary>
    public Theme Theme => _themeManager.CurrentTheme;

    /// <summary>
    /// Gets the render loop settings controlling frame scheduling.
    /// </summary>
    public RenderLoopSettings RenderLoopSettings => _renderLoopSettings;

    /// <summary>
    /// Raised when the theme changes.
    /// </summary>
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

    /// <summary>
    /// Gets the active platform host responsible for windowing and input.
    /// </summary>
    public IPlatformHost PlatformHost { get; }

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
    public static GraphicsBackend SelectedGraphicsBackend
    {
        get
        {
            try
            {
                return DefaultGraphicsFactory.Backend;
            }
            catch
            {
                return GraphicsBackend.Custom;
            }
        }
    }

    /// <summary>
    /// Gets or sets the default graphics factory used by windows/controls.
    /// In trim/AOT-friendly setups, backend packages register factories via <see cref="RegisterGraphicsFactory"/>.
    /// </summary>
    public static IGraphicsFactory DefaultGraphicsFactory
    {
        get => _defaultGraphicsFactoryOverride ?? CreateDefaultGraphicsFactory();
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _defaultGraphicsFactoryOverride = value;
        }
    }

    /// <summary>
    /// Sets the default graphics factory by id. The id must have been registered with <see cref="RegisterGraphicsFactory"/>.
    /// </summary>
    public static void SetDefaultGraphicsFactory(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_current != null)
        {
            throw new InvalidOperationException("Cannot change graphics backend while the application is running.");
        }

        lock (_syncLock)
        {
            if (!_graphicsFactoriesById.ContainsKey(id))
            {
                throw new InvalidOperationException($"Graphics factory '{id}' is not registered.");
            }

            _defaultGraphicsFactoryId = id;
            _defaultGraphicsFactoryOverride = null;
        }
    }

    /// <summary>
    /// Sets the default platform host by id. The id must have been registered with <see cref="RegisterPlatformHost"/>.
    /// </summary>
    public static void SetDefaultPlatformHost(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_current != null)
        {
            throw new InvalidOperationException("Cannot change platform host while the application is running.");
        }

        lock (_syncLock)
        {
            if (!_platformHostsById.ContainsKey(id))
            {
                throw new InvalidOperationException($"Platform host '{id}' is not registered.");
            }

            _defaultPlatformHostId = id;
            _defaultPlatformHostOverride = null; // reset lazy cache
        }
    }

    public static IPlatformHost DefaultPlatformHost
    {
        get => _defaultPlatformHostOverride ??= CreateDefaultPlatformHost();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _defaultPlatformHostOverride = value;
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

    private static IGraphicsFactory CreateDefaultGraphicsFactory()
    {
        if (_defaultGraphicsFactoryOverride != null)
        {
            return _defaultGraphicsFactoryOverride;
        }

        if (_defaultGraphicsFactoryId != null)
        {
            return CreateRegisteredGraphicsFactory(_defaultGraphicsFactoryId);
        }

        if (TryGetSingleRegisteredGraphicsFactory(out var singleFactory))
        {
            return singleFactory;
        }

        throw new InvalidOperationException(
            "No graphics backend selected. Register a backend package (e.g. Aprillz.MewUI.Backend.OpenGL / Direct2D / Gdi) " +
            "and call Application.SetDefaultGraphicsFactory(...) if multiple backends are referenced.");
    }

    /// <summary>
    /// Registers a graphics factory by id. Backend packages should call this at startup.
    /// </summary>
    public static void RegisterGraphicsFactory(string id, Func<IGraphicsFactory> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);

        if (_current != null)
        {
            throw new InvalidOperationException("Cannot register a graphics backend while the application is running.");
        }

        lock (_syncLock)
        {
            // Project policy: a process should register exactly one graphics backend. This keeps the core trim-friendly.
            // Tools/tests can still choose a backend by registering only one in their entry point.
            if (_graphicsFactoriesById.Count > 0 && !_graphicsFactoriesById.ContainsKey(id))
            {
                var existingIds = string.Join(", ", _graphicsFactoriesById.Keys.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase));
                throw new InvalidOperationException($"A graphics backend is already registered ({existingIds}). Register only one backend per process.");
            }

            if (_graphicsFactoriesById.TryGetValue(id, out var existing) && existing != factory)
            {
                throw new InvalidOperationException($"Graphics factory '{id}' is already registered.");
            }

            _graphicsFactoriesById[id] = factory;
        }
    }

    /// <summary>
    /// Registers a platform host by id. Platform packages should call this at startup.
    /// </summary>
    public static void RegisterPlatformHost(string id, Func<IPlatformHost> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);

        if (_current != null)
        {
            throw new InvalidOperationException("Cannot register a platform host while the application is running.");
        }

        lock (_syncLock)
        {
            // Project policy: a process should register exactly one platform host.
            if (_platformHostsById.Count > 0 && !_platformHostsById.ContainsKey(id))
            {
                var existingIds = string.Join(", ", _platformHostsById.Keys.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase));
                throw new InvalidOperationException($"A platform host is already registered ({existingIds}). Register only one platform host per process.");
            }

            if (_platformHostsById.TryGetValue(id, out var existing) && existing != factory)
            {
                throw new InvalidOperationException($"Platform host '{id}' is already registered.");
            }

            _platformHostsById[id] = factory;
        }
    }

    private static bool TryGetSingleRegisteredGraphicsFactory(out IGraphicsFactory factory)
    {
        lock (_syncLock)
        {
            if (_graphicsFactoriesById.Count == 1)
            {
                var single = _graphicsFactoriesById.Values.First();
                factory = single();
                return true;
            }

            factory = null!;
            return false;
        }
    }

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
        if (_defaultPlatformHostId != null)
        {
            return MaybeTracePlatformHost(CreateRegisteredPlatformHost(_defaultPlatformHostId));
        }

        lock (_syncLock)
        {
            if (_platformHostsById.Count == 1)
            {
                return MaybeTracePlatformHost(_platformHostsById.Values.First()());
            }

            if (_platformHostsById.Count == 0)
            {
                throw new InvalidOperationException(
                    "No platform host registered. Add a platform package such as Aprillz.MewUI.Platform.Win32 or Aprillz.MewUI.Platform.X11.");
            }

            var ids = string.Join(", ", _platformHostsById.Keys.OrderBy(static s => s, StringComparer.OrdinalIgnoreCase));
            throw new InvalidOperationException(
                $"Multiple platform hosts are registered ({ids}). Call Application.SetDefaultPlatformHost(\"...\") or assign Application.DefaultPlatformHost explicitly.");
        }
    }

    private static IPlatformHost CreateRegisteredPlatformHost(string id)
    {
        lock (_syncLock)
        {
            if (!_platformHostsById.TryGetValue(id, out var factory))
            {
                throw new InvalidOperationException($"No platform host registered for '{id}'.");
            }

            return factory();
        }
    }

    private static IPlatformHost MaybeTracePlatformHost(IPlatformHost host)
    {
        if (!DiagLog.Enabled)
        {
            return host;
        }

        return host is TracingPlatformHost ? host : new TracingPlatformHost(host);
    }

    private static IGraphicsFactory CreateRegisteredGraphicsFactory(string id)
    {
        lock (_syncLock)
        {
            if (!_graphicsFactoriesById.TryGetValue(id, out var factory))
            {
                throw new InvalidOperationException($"No graphics factory registered for '{id}'.");
            }

            return factory();
        }
    }
}
