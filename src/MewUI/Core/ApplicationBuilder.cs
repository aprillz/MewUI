namespace Aprillz.MewUI;

using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

public sealed class ApplicationBuilder
{
    private Func<Window>? _mainWindowFactory;

    public ApplicationBuilder(AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
    }

    public AppOptions Options { get; }

    public ApplicationBuilder UseWin32()
    {
        Options.Platform = PlatformHostKind.Win32;
        return this;
    }

    public ApplicationBuilder UseX11()
    {
        Options.Platform = PlatformHostKind.X11;
        return this;
    }

    public ApplicationBuilder UseDirect2D()
    {
        Options.GraphicsBackend = GraphicsBackend.Direct2D;
        return this;
    }

    public ApplicationBuilder UseGdi()
    {
        Options.GraphicsBackend = GraphicsBackend.Gdi;
        return this;
    }

    public ApplicationBuilder UseOpenGL()
    {
        Options.GraphicsBackend = GraphicsBackend.OpenGL;
        return this;
    }

    public ApplicationBuilder UseTheme(ThemeVariant themeMode)
    {
        Options.ThemeMode = themeMode;
        return this;
    }

    public ApplicationBuilder UseAccent(Accent accent)
    {
        Options.Accent = accent;
        return this;
    }

    public ApplicationBuilder UseSeed(ThemeSeed lightSeed, ThemeSeed darkSeed)
    {
        ArgumentNullException.ThrowIfNull(lightSeed);
        ArgumentNullException.ThrowIfNull(darkSeed);

        Options.LightSeed = lightSeed;
        Options.DarkSeed = darkSeed;
        return this;
    }

    public ApplicationBuilder UseMetrics(ThemeMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        Options.Metrics = metrics;
        return this;
    }

    public ApplicationBuilder UseMainWindow(Func<Window> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _mainWindowFactory = factory;
        return this;
    }

    public void Run()
    {
        if (_mainWindowFactory == null)
        {
            throw new InvalidOperationException("Main window is not configured. Use UseMainWindow(...) or Run<TWindow>().");
        }

        Run(_mainWindowFactory());
    }

    public void Run(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        ApplyOptions();
        Application.Run(mainWindow);
    }

    public void Run<TWindow>() where TWindow : Window, new()
    {
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

        if (Options.Accent != null)
        {
            ThemeManager.DefaultAccent = Options.Accent.Value;
        }
    }
}

