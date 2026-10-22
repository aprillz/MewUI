namespace Aprillz.MewUI;

/// <summary>
/// Fluent configuration extensions for <see cref="ApplicationBuilder"/>.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Sets the default theme mode.
    /// </summary>
    public static ApplicationBuilder UseTheme(this ApplicationBuilder builder, ThemeVariant themeMode)
    {
        builder.Options.ThemeMode = themeMode;
        if (!Application.IsRunning)
        {
            ThemeManager.Default = themeMode;
        }
        return builder;
    }

    /// <summary>
    /// Sets the default accent.
    /// </summary>
    public static ApplicationBuilder UseAccent(this ApplicationBuilder builder, Accent accent)
    {
        builder.Options.Accent = accent;
        builder.Options.AccentColor = null;
        if (!Application.IsRunning)
        {
            ThemeManager.DefaultAccentColor = null;
            ThemeManager.DefaultAccent = accent;
        }
        return builder;
    }

    /// <summary>
    /// Sets the default accent to a custom color.
    /// </summary>
    public static ApplicationBuilder UseAccent(this ApplicationBuilder builder, Color accentColor)
    {
        builder.Options.AccentColor = accentColor;
        if (!Application.IsRunning)
        {
            ThemeManager.DefaultAccentColor = accentColor;
        }
        return builder;
    }

    /// <summary>
    /// Sets the seed palettes used to generate light/dark themes.
    /// </summary>
    public static ApplicationBuilder UseSeed(this ApplicationBuilder builder, ThemeSeed lightSeed, ThemeSeed darkSeed)
    {
        ArgumentNullException.ThrowIfNull(lightSeed);
        ArgumentNullException.ThrowIfNull(darkSeed);

        builder.Options.LightSeed = lightSeed;
        builder.Options.DarkSeed = darkSeed;
        if (!Application.IsRunning)
        {
            ThemeManager.DefaultLightSeed = lightSeed;
            ThemeManager.DefaultDarkSeed = darkSeed;
        }
        return builder;
    }

    /// <summary>
    /// Sets the theme metric overrides.
    /// </summary>
    public static ApplicationBuilder UseMetrics(this ApplicationBuilder builder, ThemeMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        builder.Options.Metrics = metrics;
        if (!Application.IsRunning)
        {
            ThemeManager.DefaultMetrics = metrics;
        }
        return builder;
    }

    /// <summary>
    /// Sets the main window factory.
    /// </summary>
    public static ApplicationBuilder BuildMainWindow(this ApplicationBuilder builder, Func<Window> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        builder.MainWindowFactory = factory;
        return builder;
    }

    /// <summary>
    /// Sets the callback invoked on the UI thread after the dispatcher is installed and before the main
    /// window is shown.
    /// </summary>
    /// <remarks>
    /// Calling this method again replaces the previously configured callback. When no main window
    /// factory is configured, <see cref="ApplicationBuilder.Run()"/> starts the application without a
    /// main window.
    /// </remarks>
    public static ApplicationBuilder OnStartup(this ApplicationBuilder builder, Action startup)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(startup);

        builder.Startup = _ => startup();
        return builder;
    }

    /// <summary>
    /// Sets the callback invoked with the command-line arguments on the UI thread after the dispatcher
    /// is installed and before the main window is shown.
    /// </summary>
    /// <remarks>
    /// Calling this method again replaces the previously configured callback. When no main window
    /// factory is configured, <see cref="ApplicationBuilder.Run()"/> starts the application without a
    /// main window.
    /// </remarks>
    public static ApplicationBuilder OnStartup(this ApplicationBuilder builder, Action<string[]> startup)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(startup);

        builder.Startup = startup;
        return builder;
    }

    /// <summary>
    /// Sets when the run loop ends automatically as windows close.
    /// </summary>
    public static ApplicationBuilder WithShutdownMode(this ApplicationBuilder builder, ShutdownMode shutdownMode)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Options.ShutdownMode = shutdownMode;
        return builder;
    }

    /// <summary>
    /// Throws a <see cref="PlatformNotSupportedException"/> with an optional custom message.
    /// </summary>
    public static ApplicationBuilder ThrowPlatformNotSupported(this ApplicationBuilder builder, string? message = null)
        => throw new PlatformNotSupportedException(message ?? "No platform/backend selected for this build.");
}
