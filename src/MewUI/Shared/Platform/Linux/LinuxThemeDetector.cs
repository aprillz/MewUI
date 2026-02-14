using System.Diagnostics;
using System.Globalization;

namespace Aprillz.MewUI.Platform.Linux;

internal static class LinuxThemeDetector
{
    private const int GnomePollIntervalMs = 2000;
    private static long _lastGnomePollTick;
    private static ThemeVariant? _lastGnomeVariant;

    public static ThemeVariant DetectSystemThemeVariant()
    {
        // 1) Environment override (GTK_THEME=Adwaita:dark etc).
        var gtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
        if (!string.IsNullOrWhiteSpace(gtkTheme) && ContainsDarkKeyword(gtkTheme))
        {
            return ThemeVariant.Dark;
        }

        // 2) GNOME (Wayland): gsettings. This is the most reliable on GNOME 42+.
        // Key: org.gnome.desktop.interface color-scheme = 'default' | 'prefer-dark' | 'prefer-light'
        if (TryGetGnomeThemeVariant(out var gnomeVariant))
        {
            return gnomeVariant;
        }

        // 2) GTK settings.ini (common across GNOME/KDE/Xfce for GTK apps).
        // Prefer gtk-4.0 then gtk-3.0 (newer DEs tend to write gtk-4.0).
        foreach (var path in EnumerateGtkSettingsPaths())
        {
            if (TryReadGtkPreferDarkTheme(path, out bool preferDark))
            {
                return preferDark ? ThemeVariant.Dark : ThemeVariant.Light;
            }

            if (TryReadGtkThemeName(path, out var themeName) && !string.IsNullOrWhiteSpace(themeName) && ContainsDarkKeyword(themeName))
            {
                return ThemeVariant.Dark;
            }
        }

        // 3) KDE: kdeglobals ColorScheme (BreezeDark etc). Best-effort.
        foreach (var path in EnumerateKdeGlobalsPaths())
        {
            if (TryReadKdeColorScheme(path, out var scheme) && !string.IsNullOrWhiteSpace(scheme) && ContainsDarkKeyword(scheme))
            {
                return ThemeVariant.Dark;
            }
        }

        return ThemeVariant.Light;
    }

    private static bool TryGetGnomeThemeVariant(out ThemeVariant variant)
    {
        variant = ThemeVariant.Light;

        // Avoid spawning gsettings too often; X11PlatformHost polls frequently.
        long now = Environment.TickCount64;
        if (now - _lastGnomePollTick < GnomePollIntervalMs && _lastGnomeVariant.HasValue)
        {
            variant = _lastGnomeVariant.Value;
            return true;
        }

        _lastGnomePollTick = now;

        // If gsettings isn't available, bail.
        // (We intentionally don't pre-check PATH; just attempt and catch.)
        if (!TryRunGSettings("org.gnome.desktop.interface", "color-scheme", out var scheme))
        {
            _lastGnomeVariant = null;
            return false;
        }

        // GNOME: explicit system preference.
        // When color-scheme is 'default', GNOME may still be configured via legacy gtk-theme name (Tweaks, themes),
        // so we fall back to gtk-theme heuristics to better match user expectations.
        if (scheme.Contains("prefer-dark", StringComparison.OrdinalIgnoreCase))
        {
            variant = ThemeVariant.Dark;
            _lastGnomeVariant = variant;
            return true;
        }

        if (scheme.Contains("prefer-light", StringComparison.OrdinalIgnoreCase))
        {
            variant = ThemeVariant.Light;
            _lastGnomeVariant = variant;
            return true;
        }

        // default/unknown: use gtk-theme name as a heuristic (often contains "-dark").
        if (TryRunGSettings("org.gnome.desktop.interface", "gtk-theme", out var gtkThemeName) &&
            !string.IsNullOrWhiteSpace(gtkThemeName) &&
            ContainsDarkKeyword(gtkThemeName))
        {
            variant = ThemeVariant.Dark;
            _lastGnomeVariant = variant;
            return true;
        }

        variant = ThemeVariant.Light;
        _lastGnomeVariant = variant;
        return true;
    }

    private static bool TryRunGSettings(string schema, string key, out string value)
    {
        value = string.Empty;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gsettings",
                ArgumentList = { "get", schema, key },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null)
            {
                return false;
            }

            // Keep this tight; this can be called from polling.
            if (!p.WaitForExit(150))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            if (p.ExitCode != 0)
            {
                return false;
            }

            var output = p.StandardOutput.ReadToEnd();
            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            // Output is usually quoted, e.g. "'prefer-dark'" or "'Adwaita-dark'".
            value = output.Trim().Trim('\'', '"', '\r', '\n', ' ', '\t');
            return value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateGtkSettingsPaths()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            yield return Path.Combine(xdgConfigHome, "gtk-4.0", "settings.ini");
            yield return Path.Combine(xdgConfigHome, "gtk-3.0", "settings.ini");
        }

        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, ".config", "gtk-4.0", "settings.ini");
            yield return Path.Combine(home, ".config", "gtk-3.0", "settings.ini");
        }
    }

    private static IEnumerable<string> EnumerateKdeGlobalsPaths()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            yield return Path.Combine(xdgConfigHome, "kdeglobals");
        }

        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, ".config", "kdeglobals");
        }
    }

    private static bool TryReadGtkPreferDarkTheme(string path, out bool preferDark)
    {
        preferDark = false;
        if (!TryReadIniValue(path, "Settings", "gtk-application-prefer-dark-theme", out var value))
        {
            return false;
        }

        // Common values: 0/1, true/false.
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
        {
            preferDark = i != 0;
            return true;
        }

        if (bool.TryParse(value, out bool b))
        {
            preferDark = b;
            return true;
        }

        // Some configs might use "yes/no".
        if (string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
        {
            preferDark = true;
            return true;
        }

        if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
        {
            preferDark = false;
            return true;
        }

        return true; // key existed; unknown value => treat as Light
    }

    private static bool TryReadGtkThemeName(string path, out string? themeName)
        => TryReadIniValue(path, "Settings", "gtk-theme-name", out themeName);

    private static bool TryReadKdeColorScheme(string path, out string? scheme)
        => TryReadIniValue(path, "General", "ColorScheme", out scheme);

    private static bool TryReadIniValue(string path, string section, string key, out string? value)
    {
        value = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            string currentSection = string.Empty;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                {
                    continue;
                }

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }

                if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var k = line[..eq].Trim();
                if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = line[(eq + 1)..].Trim().Trim('"');
                return true;
            }
        }
        catch
        {
            // ignore and fall back
        }

        return false;
    }

    private static bool ContainsDarkKeyword(string value)
        => value.Contains("dark", StringComparison.OrdinalIgnoreCase) ||
           value.Contains(":dark", StringComparison.OrdinalIgnoreCase) ||
           value.EndsWith("-dark", StringComparison.OrdinalIgnoreCase);
}
