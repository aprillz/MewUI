using System.Collections.Concurrent;

namespace Aprillz.MewUI.Resources;

/// <summary>
/// Global registry mapping (familyName) → cached file path.
/// Populated by <see cref="FontResources.Register"/> and queried by each rendering backend's CreateFont.
/// </summary>
internal static class FontRegistry
{
    internal readonly record struct ResolvedFont(string FamilyName, string FilePath);

    // Key: family name (case-insensitive)
    // Value: file path to the cached font file
    private static readonly ConcurrentDictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a font file path for a given family name.
    /// </summary>
    internal static void Register(string familyName, string filePath)
    {
        if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(filePath))
            return;

        _map[familyName] = filePath;
    }

    /// <summary>
    /// Resolves a family name to a registered font file path.
    /// Returns null if no registered font matches.
    /// </summary>
    internal static ResolvedFont? Resolve(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return null;

        if (_map.TryGetValue(family, out var filePath))
            return new ResolvedFont(family, filePath);

        return null;
    }
}
