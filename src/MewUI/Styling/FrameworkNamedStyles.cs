namespace Aprillz.MewUI;

internal static class FrameworkNamedStyles
{
    private static readonly object _sync = new();
    private static readonly Dictionary<string, Func<Style>> _factories = new(StringComparer.Ordinal);

    internal static bool Register(string name, Func<Style> factory)
    {
        lock (_sync)
        {
            _factories.TryAdd(name, factory);
        }

        return true;
    }

    internal static bool TryGetFactory(string name, out Func<Style>? factory)
    {
        lock (_sync)
        {
            return _factories.TryGetValue(name, out factory);
        }
    }

    /// <summary>
    /// Materializes a framework style for trees with no application style sheet to hold it. The instance
    /// is shared, so controls wearing one name apply the same style.
    /// </summary>
    internal static Style? GetStyle(string name)
    {
        lock (_sync)
        {
            if (_styles.TryGetValue(name, out var style))
            {
                return style;
            }

            if (!_factories.TryGetValue(name, out var factory))
            {
                return null;
            }

            style = factory();
            _styles[name] = style;
            return style;
        }
    }

    private static readonly Dictionary<string, Style> _styles = new(StringComparer.Ordinal);
}
