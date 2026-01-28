using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI;

/// <summary>
/// Manages the application's effective <see cref="Theme"/> based on a theme mode (System/Light/Dark)
/// and optional accent overrides.
/// </summary>
public sealed class ThemeManager
{
    private static Theme? _defaultLightTheme;
    private static Theme? _defaultDarkTheme;

    internal static void ResetCachedDefaultThemes()
    {
        _defaultLightTheme = null;
        _defaultDarkTheme = null;
    }

    internal static ThemeVariant ResolveVariant(IPlatformHost platformHost, ThemeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(platformHost);

        if (variant != ThemeVariant.System)
        {
            return variant;
        }

        return platformHost.GetSystemThemeVariant();
    }

    internal static ThemeVariant ResolveVariantForStartup(ThemeVariant variant)
    {
        if (variant != ThemeVariant.System)
        {
            return variant;
        }

        // Application is not initialized yet, but we still want System to reflect the OS theme
        // so windows can be created with the correct initial colors.
        try
        {
            var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
            return host.GetSystemThemeVariant();
        }
        catch
        {
            return ThemeVariant.Light;
        }
    }

    public static ThemeSeed DefaultLightSeed
    {
        get;
        set
        {
            if (Application.IsRunning)
            {
                throw new InvalidOperationException("ThemeManager.DefaultLightSeed cannot be changed after Application is running.");
            }

            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(field, value))
            {
                return;
            }

            field = value;
            ResetCachedDefaultThemes();
        }
    } = ThemeSeed.DefaultLight;

    public static ThemeSeed DefaultDarkSeed
    {
        get;
        set
        {
            if (Application.IsRunning)
            {
                throw new InvalidOperationException("ThemeManager.DefaultDarkSeed cannot be changed after Application is running.");
            }

            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(field, value))
            {
                return;
            }

            field = value;
            ResetCachedDefaultThemes();
        }
    } = ThemeSeed.DefaultDark;

    public static ThemeMetrics DefaultMetrics
    {
        get;
        set
        {
            if (Application.IsRunning)
            {
                throw new InvalidOperationException("ThemeManager.DefaultMetrics cannot be changed after Application is running.");
            }

            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(field, value))
            {
                return;
            }

            field = value;
            ResetCachedDefaultThemes();
        }
    } = ThemeMetrics.Default;

    public static Accent DefaultAccent
    {
        get;
        set
        {
            if (Application.IsRunning)
            {
                throw new InvalidOperationException("ThemeManager.DefaultAccent cannot be changed after Application is running.");
            }

            if (field == value)
            {
                return;
            }

            field = value;
            ResetCachedDefaultThemes();
        }
    } = Accent.Blue;

    public static ThemeVariant Default
    {
        get;
        set
        {
            if (Application.IsRunning)
            {
                throw new InvalidOperationException("ThemeManager.Default cannot be changed after Application is running.");
            }

            field = value;
        }
    } = ThemeVariant.System;

    private readonly IPlatformHost _platformHost;
    private Theme? _cachedTheme;
    private Theme? _explicitTheme;
    private bool _explicitThemeSet;
    private Color? _accentOverride;
    private Color? _accentTextOverride;

    internal readonly struct ThemeChange
    {
        public Theme OldTheme { get; }
        public Theme NewTheme { get; }
        public bool Changed => !ReferenceEquals(OldTheme, NewTheme);

        public ThemeChange(Theme oldTheme, Theme newTheme)
        {
            OldTheme = oldTheme;
            NewTheme = newTheme;
        }
    }

    internal ThemeManager(IPlatformHost platformHost, ThemeVariant initialMode)
    {
        ArgumentNullException.ThrowIfNull(platformHost);
        _platformHost = platformHost;
        Mode = initialMode;
    }

    /// <summary>
    /// Gets the current theme mode (System/Light/Dark).
    /// </summary>
    public ThemeVariant Mode { get; private set; }

    /// <summary>
    /// Gets the current effective theme. This value is cached and recalculated only when inputs change.
    /// </summary>
    public Theme CurrentTheme
    {
        get
        {
            if (_explicitThemeSet)
            {
                return _explicitTheme!;
            }

            _cachedTheme ??= ResolveThemeFromMode();
            return _cachedTheme;
        }
    }

    internal ThemeChange SetCustomTheme(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var oldTheme = CurrentTheme;
        if (ReferenceEquals(oldTheme, theme))
        {
            return new ThemeChange(oldTheme, oldTheme);
        }

        _explicitTheme = theme;
        _explicitThemeSet = true;
        _cachedTheme = null;
        _accentOverride = null;
        _accentTextOverride = null;
        Mode = Palette.IsDarkBackground(theme.Palette.WindowBackground) ? ThemeVariant.Dark : ThemeVariant.Light;

        return new ThemeChange(oldTheme, theme);
    }

    internal ThemeChange SetTheme(ThemeVariant mode)
    {
        var oldTheme = CurrentTheme;

        if (Mode == mode && !_explicitThemeSet)
        {
            return new ThemeChange(oldTheme, oldTheme);
        }

        Mode = mode;
        _explicitThemeSet = false;
        _explicitTheme = null;
        _cachedTheme = ResolveThemeFromMode();

        return new ThemeChange(oldTheme, _cachedTheme);
    }

    internal ThemeChange SetAccent(Accent accent, Color? accentText)
    {
        var oldTheme = CurrentTheme;

        var baseTheme = ResolveBaseThemeForMode(Mode);
        _accentOverride = BuiltInAccent.GetAccentColor(accent, baseTheme.IsDark);
        _accentTextOverride = accentText;
        _explicitThemeSet = false;
        _explicitTheme = null;
        _cachedTheme = ResolveThemeFromMode();

        return new ThemeChange(oldTheme, _cachedTheme);
    }

    internal ThemeChange SetAccent(Color accent, Color? accentText)
    {
        var oldTheme = CurrentTheme;

        _accentOverride = accent;
        _accentTextOverride = accentText;
        _explicitThemeSet = false;
        _explicitTheme = null;
        _cachedTheme = ResolveThemeFromMode();

        return new ThemeChange(oldTheme, _cachedTheme);
    }

    internal ThemeChange ApplySystemThemeChanged()
    {
        var oldTheme = CurrentTheme;

        if (Mode != ThemeVariant.System || _explicitThemeSet)
        {
            return new ThemeChange(oldTheme, oldTheme);
        }

        var resolved = ResolveThemeFromMode();
        if (ReferenceEquals(_cachedTheme, resolved))
        {
            return new ThemeChange(oldTheme, oldTheme);
        }

        _cachedTheme = resolved;
        return new ThemeChange(oldTheme, resolved);
    }

    private Theme ResolveThemeFromMode()
    {
        var baseTheme = ResolveBaseThemeForMode(Mode);
        if (_accentOverride != null)
        {
            return baseTheme with
            {
                Palette = baseTheme.Palette.WithAccent(_accentOverride.Value, _accentTextOverride)
            };
        }

        return baseTheme;
    }

    private Theme ResolveBaseThemeForMode(ThemeVariant mode)
    {
        var resolvedVariant = ResolveVariant(_platformHost, mode);

        return GetDefaultTheme(resolvedVariant);
    }

    internal static Theme GetDefaultTheme(ThemeVariant variant)
    {
        if (variant == ThemeVariant.System)
        {
            throw new ArgumentException("ThemeVariant.System must be resolved before requesting a default Theme.", nameof(variant));
        }

        if (variant == ThemeVariant.Light)
        {
            return _defaultLightTheme ??= CreateDefaultTheme(isDark: false);
        }

        return _defaultDarkTheme ??= CreateDefaultTheme(isDark: true);
    }

    private static Theme CreateDefaultTheme(bool isDark)
    {
        var seed = isDark ? DefaultDarkSeed : DefaultLightSeed;
        var palette = new Palette(seed, DefaultAccent.GetColor(isDark));

        return new Theme
        {
            Name = isDark ? "Dark" : "Light",
            Palette = palette,
            Metrics = DefaultMetrics
        };
    }
}
