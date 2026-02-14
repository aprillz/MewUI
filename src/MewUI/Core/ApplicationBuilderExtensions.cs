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
        if (!Application.IsRunning)
        {
            ThemeManager.DefaultAccent = accent;
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
    /// Sets theme metrics overrides.
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
    /// Configures the main window factory.
    /// </summary>
    public static ApplicationBuilder BuildMainWindow(this ApplicationBuilder builder, Func<Window> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        builder.MainWindowFactory = factory;
        return builder;
    }

    /// <summary>
    /// Throws a <see cref="PlatformNotSupportedException"/> with an optional custom message.
    /// </summary>
    public static ApplicationBuilder ThrowPlatformNotSupported(this ApplicationBuilder builder, string? message = null)
        => throw new PlatformNotSupportedException(message ?? "No platform/backend selected for this build.");
}
