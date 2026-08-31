namespace Aprillz.MewUI;

/// <summary>
/// Configures and runs an <see cref="Application"/> using an <see cref="AppOptions"/> instance.
/// </summary>
public sealed class ApplicationBuilder
{
    /// <summary>
    /// Gets or sets a factory used to create the main window when calling <see cref="Run()"/>.
    /// </summary>
    public Func<Window>? MainWindowFactory { get; set; }

    /// <summary>
    /// Gets the callback invoked with the command-line arguments on the UI thread after the dispatcher is
    /// installed and before the main window is shown. A configured callback without a main window factory
    /// starts without a main window.
    /// </summary>
    internal Action<string[]>? Startup { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationBuilder"/> class.
    /// </summary>
    /// <param name="options">Application options.</param>
    public ApplicationBuilder(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
    }

    /// <summary>
    /// Gets the options to be applied when running the application.
    /// </summary>
    public AppOptions Options { get; }

    /// <summary>
    /// Applies configured options and runs the application. When no <see cref="MainWindowFactory"/>
    /// is configured, a startup callback is required and the application runs without a main window.
    /// </summary>
    public void Run()
    {
        if (Application.IsRunning)
        {
            throw new InvalidOperationException("ApplicationBuilder cannot be used after Application is running.");
        }
        if (MainWindowFactory == null && Startup == null)
        {
            throw new InvalidOperationException(
                "Application startup is not configured. Use BuildMainWindow(...), OnStartup(...), or Run<TWindow>().");
        }

        // 1. Platform setup - establishes platform font and system theme detection.
        _ = Application.DefaultPlatformHost;
        // 2. Theme/options - user overrides applied on top of platform defaults.
        ApplyOptions();

        if (MainWindowFactory != null)
        {
            var mainWindow = MainWindowFactory();
            ArgumentNullException.ThrowIfNull(mainWindow);
            RunApplication(mainWindow);
        }
        else
        {
            Application.RunInternal(mainWindow: null, Startup, Options.ShutdownMode);
        }
    }

    /// <summary>
    /// Applies configured options and runs the application asynchronously.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Application.IsRunning)
        {
            throw new InvalidOperationException("ApplicationBuilder cannot be used after Application is running.");
        }
        if (MainWindowFactory == null && Startup == null)
        {
            throw new InvalidOperationException(
                "Application startup is not configured. Use BuildMainWindow(...), OnStartup(...), or Run<TWindow>().");
        }

        _ = Application.DefaultPlatformHost;
        ApplyOptions();

        if (MainWindowFactory != null)
        {
            var mainWindow = MainWindowFactory();
            ArgumentNullException.ThrowIfNull(mainWindow);
            return Application.RunInternalAsync(mainWindow, Startup, Options.ShutdownMode, cancellationToken);
        }

        return Application.RunInternalAsync(null, Startup, Options.ShutdownMode, cancellationToken);
    }

    /// <summary>
    /// Applies configured options and runs the application with the given main window.
    /// </summary>
    public void Run(Window mainWindow)
    {
        if (Application.IsRunning)
        {
            throw new InvalidOperationException("ApplicationBuilder cannot be used after Application is running.");
        }
        if (MainWindowFactory is not null)
        {
            throw new InvalidOperationException("Main window factory is already set. Use Run().");
        }

        ArgumentNullException.ThrowIfNull(mainWindow);

        _ = Application.DefaultPlatformHost;
        ApplyOptions();
        RunApplication(mainWindow);
    }

    /// <summary>
    /// Applies configured options and runs the application using a new instance of <typeparamref name="TWindow"/>.
    /// </summary>
    public void Run<TWindow>() where TWindow : Window, new()
    {
        if (MainWindowFactory is not null)
        {
            throw new InvalidOperationException("Main window factory is already set. Use Run().");
        }

        _ = Application.DefaultPlatformHost;
        ApplyOptions();
        RunApplication(new TWindow());
    }

    private void RunApplication(Window mainWindow)
        => Application.RunInternal(mainWindow, Startup, Options.ShutdownMode);

    private void ApplyOptions()
    {
        if (Application.IsRunning)
        {
            throw new InvalidOperationException("ApplicationBuilder cannot be used after Application is running.");
        }

        if (Options.Metrics != null)
        {
            ThemeManager.DefaultMetrics = Options.Metrics;
        }

        if (Options.LightSeed != null)
        {
            ThemeManager.DefaultLightSeed = Options.LightSeed;
        }

        if (Options.DarkSeed != null)
        {
            ThemeManager.DefaultDarkSeed = Options.DarkSeed;
        }

        if (Options.ThemeMode != null)
        {
            ThemeManager.Default = Options.ThemeMode.Value;
        }

        if (Options.AccentColor != null)
        {
            ThemeManager.DefaultAccentColor = Options.AccentColor.Value;
        }
        else if (Options.Accent != null)
        {
            ThemeManager.DefaultAccent = Options.Accent.Value;
        }
    }
}
