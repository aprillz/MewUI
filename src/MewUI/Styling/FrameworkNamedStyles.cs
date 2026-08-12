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
}
