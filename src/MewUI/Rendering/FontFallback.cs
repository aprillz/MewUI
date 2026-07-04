using System.Collections.Concurrent;
using System.Globalization;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Global fallback font chain. When a glyph is missing in the primary font,
/// fonts in this chain are tried in order.
/// </summary>
public static class FontFallback
{
    private static readonly object _lock = new();
    private static volatile string[] _chain = [];
    private static int _version;
    private static string? _locale;

    /// <summary>
    /// Adds a font family to the end of the fallback chain.
    /// </summary>
    public static void AddFallback(string fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        lock (_lock)
        {
            var chain = _chain;
            var next = new string[chain.Length + 1];
            chain.CopyTo(next, 0);
            next[chain.Length] = fontFamily;
            _chain = next;
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Adds multiple font families to the end of the fallback chain.
    /// </summary>
    public static void AddFallbacks(params string[] fontFamilies)
    {
        ArgumentNullException.ThrowIfNull(fontFamilies);
        if (fontFamilies.Length == 0) return;
        lock (_lock)
        {
            var chain = _chain;
            var next = new string[chain.Length + fontFamilies.Length];
            chain.CopyTo(next, 0);
            fontFamilies.CopyTo(next, chain.Length);
            _chain = next;
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Inserts a font family at the specified index in the fallback chain.
    /// </summary>
    public static void InsertFallback(int index, string fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        lock (_lock)
        {
            var chain = _chain;
            if ((uint)index > (uint)chain.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            var next = new string[chain.Length + 1];
            Array.Copy(chain, 0, next, 0, index);
            next[index] = fontFamily;
            Array.Copy(chain, index, next, index + 1, chain.Length - index);
            _chain = next;
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Removes a font family from the fallback chain.
    /// </summary>
    public static bool RemoveFallback(string fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        lock (_lock)
        {
            var chain = _chain;
            int idx = Array.FindIndex(chain, f => string.Equals(f, fontFamily, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;
            var next = new string[chain.Length - 1];
            Array.Copy(chain, 0, next, 0, idx);
            Array.Copy(chain, idx + 1, next, idx, chain.Length - idx - 1);
            _chain = next;
            Interlocked.Increment(ref _version);
            return true;
        }
    }

    /// <summary>
    /// Removes all entries from the fallback chain.
    /// </summary>
    public static void ClearFallbacks()
    {
        lock (_lock)
        {
            _chain = [];
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Atomically replaces the entire fallback chain.
    /// Preferred over multiple Add/Remove calls to avoid intermediate cache invalidations.
    /// </summary>
    public static void SetFallbacks(IEnumerable<string> fontFamilies)
    {
        ArgumentNullException.ThrowIfNull(fontFamilies);
        lock (_lock)
        {
            _chain = fontFamilies.ToArray();
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Gets the current fallback chain as a read-only snapshot.
    /// </summary>
    public static IReadOnlyList<string> FallbackChain => _chain;

    /// <summary>
    /// Locale hint for font fallback (affects CJK script priority in DirectWrite).
    /// <c>null</c> = system default (<see cref="CultureInfo.CurrentUICulture"/>).
    /// </summary>
    public static string? Locale
    {
        get => _locale;
        set => _locale = value;
    }

    internal static string ResolvedLocale
        => _locale ?? CultureInfo.CurrentUICulture.Name;

    internal static int Version => Volatile.Read(ref _version);

    /// <summary>
    /// Returns the current chain as an array snapshot (internal fast path).
    /// </summary>
    internal static string[] GetChainSnapshot() => _chain;

    /// <summary>
    /// Applies the platform's default fallback chain if no user chain is configured.
    /// Called from <c>Application.ApplyPlatformFontDefaults</c>, mirroring
    /// how <c>IPlatformHost.DefaultFontFamily</c> flows into <c>ThemeMetrics</c>.
    /// </summary>
    internal static void ApplyPlatformDefaults(IReadOnlyList<string> defaults)
    {
        if (_chain.Length > 0) return; // User already configured
        if (defaults == null || defaults.Count == 0) return;
        SetFallbacks(defaults);
    }

    /// <summary>
    /// Returns CJK font families ordered by <paramref name="locale"/> priority.
    /// All four variants (KR, JP, SC, TC) are always included - the locale only
    /// determines which appears first.
    /// </summary>
    public static string[] OrderCjkByLocale(string locale, string kr, string jp, string sc, string tc)
    {
        return locale switch
        {
            var l when l.StartsWith("ko", StringComparison.OrdinalIgnoreCase) => [kr, jp, sc, tc],
            var l when l.StartsWith("ja", StringComparison.OrdinalIgnoreCase) => [jp, kr, sc, tc],
            var l when l.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
                     || l.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase) => [tc, sc, jp, kr],
            var l when l.StartsWith("zh", StringComparison.OrdinalIgnoreCase) => [sc, tc, jp, kr],
            _ => [jp, kr, sc, tc], // neutral default
        };
    }
}
