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
    /// Applies configured options and runs the application using <see cref="MainWindowFactory"/>.
    /// </summary>
    public void Run()
    {
        if (Application.IsRunning)
        {
            throw new InvalidOperationException("ApplicationBuilder cannot be used after Application is running.");
        }
        if (MainWindowFactory == null)
        {
            throw new InvalidOperationException("Main window is not configured. Use UseMainWindow(...) or Run<TWindow>().");
        }

        // 1. Platform setup — establishes platform font and system theme detection.
        _ = Application.DefaultPlatformHost;
        // 2. Theme/options — user overrides applied on top of platform defaults.
        ApplyOptions();

        var mainWindow = MainWindowFactory();
        ArgumentNullException.ThrowIfNull(mainWindow);

        Application.Run(mainWindow);
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
        Application.Run(mainWindow);
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
        Application.Run(new TWindow());
    }

    private void ApplyOptions()
    {
        if (Application.IsRunning)
        {
            throw new InvalidOperationException("ApplicationBuilder cannot be used after Application is running.");
        }

        if (Options.Metrics != null)
        {
            var metrics = Options.Metrics;

            // If the user's metrics still has the compile-time default font ("Segoe UI")
            // but the platform provides a different system font (e.g. ".AppleSystemUIFont"),
            // merge the platform font so it isn't lost.
            var platformFont = ThemeMetrics.DefaultFontFamily;
            if (!string.IsNullOrEmpty(platformFont) &&
                metrics.FontFamily == ThemeMetrics.Default.FontFamily &&
                metrics.FontFamily != platformFont)
            {
                metrics = metrics with { FontFamily = platformFont };
            }

            ThemeManager.DefaultMetrics = metrics;
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

        if (Options.Accent != null)
        {
            ThemeManager.DefaultAccent = Options.Accent.Value;
        }
    }
}
