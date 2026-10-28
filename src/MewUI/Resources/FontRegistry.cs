using System.Collections.Concurrent;

namespace Aprillz.MewUI.Resources;

/// <summary>
/// Global registry mapping a family name to the font files registered under it, one per weight and slant.
/// Populated by <see cref="FontResources.Register"/> and queried by each rendering backend's CreateFont.
/// </summary>
internal static class FontRegistry
{
    /// <summary>One registered font file and the weight and slant it declares.</summary>
    internal readonly record struct RegisteredFace(string FilePath, int Weight, bool IsItalic);

    /// <summary>A registered family and its faces, in registration order.</summary>
    internal readonly record struct ResolvedFont(string FamilyName, RegisteredFace[] Faces)
    {
        /// <summary>Every file of the family, for backends that build a collection and match faces themselves.</summary>
        public IEnumerable<string> FilePaths => Faces.Select(face => face.FilePath);

        /// <summary>The face that draws <paramref name="weight"/> and <paramref name="italic"/>, matched the way CSS matches a family's faces.</summary>
        public RegisteredFace SelectFace(FontWeight weight, bool italic) => FontRegistry.SelectFace(Faces, (int)weight, italic);
    }

    // Key: family name (case-insensitive). A changed family gets a new array, so a cache built from one can tell it is stale.
    private static readonly ConcurrentDictionary<string, RegisteredFace[]> _families = new(StringComparer.OrdinalIgnoreCase);

    private static int _version;

    /// <summary>Changes whenever a family is registered, so a cache of resolved font files knows to start over.</summary>
    internal static int Version => Volatile.Read(ref _version);

    /// <summary>
    /// Adds a font file to a family, reading its weight and slant from the file. Registering the same file again changes nothing.
    /// </summary>
    internal static void Register(string familyName, string filePath)
    {
        if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(filePath))
            return;

        OpenTypeFaceStyle.TryRead(filePath, out int weight, out bool italic);
        Register(familyName, new RegisteredFace(filePath, weight, italic));
    }

    /// <summary>Adds a face whose weight and slant are already known.</summary>
    internal static void Register(string familyName, RegisteredFace face)
    {
        bool changed = false;
        _families.AddOrUpdate(
            familyName,
            _ =>
            {
                changed = true;
                return [face];
            },
            (_, faces) =>
            {
                if (faces.Any(existing => string.Equals(existing.FilePath, face.FilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    changed = false;
                    return faces;
                }

                changed = true;
                return [.. faces, face];
            });

        if (changed)
        {
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>Removes a font file from every family that holds it, dropping families left empty.</summary>
    internal static void Unregister(string filePath)
    {
        bool changed = false;
        foreach (var (family, faces) in _families)
        {
            if (!faces.Any(face => string.Equals(face.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var remaining = faces.Where(face => !string.Equals(face.FilePath, filePath, StringComparison.OrdinalIgnoreCase)).ToArray();
            bool updated = remaining.Length == 0
                ? _families.TryRemove(new KeyValuePair<string, RegisteredFace[]>(family, faces))
                : _families.TryUpdate(family, remaining, faces);
            changed |= updated;
        }

        if (changed)
        {
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Resolves a family name to its registered faces.
    /// Returns null if no registered font matches.
    /// </summary>
    internal static ResolvedFont? Resolve(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
            return null;

        if (_families.TryGetValue(family, out var faces))
            return new ResolvedFont(family, faces);

        return null;
    }

    private static RegisteredFace SelectFace(RegisteredFace[] faces, int weight, bool italic)
    {
        // CSS Fonts 4, font matching: slant narrows the set first, then weight picks within it.
        var sameSlant = faces.Where(face => face.IsItalic == italic).ToArray();
        var candidates = sameSlant.Length > 0 ? sameSlant : faces;

        var exact = candidates.Where(face => face.Weight == weight).ToArray();
        if (exact.Length > 0)
        {
            return exact[0];
        }

        var lighter = candidates.Where(face => face.Weight < weight).OrderByDescending(face => face.Weight);
        var heavier = candidates.Where(face => face.Weight > weight).OrderBy(face => face.Weight);
        if (weight >= (int)FontWeight.Normal && weight <= (int)FontWeight.Medium)
        {
            var upToMedium = heavier.Where(face => face.Weight <= (int)FontWeight.Medium);
            var aboveMedium = heavier.Where(face => face.Weight > (int)FontWeight.Medium);
            return upToMedium.Concat(lighter).Concat(aboveMedium).First();
        }

        return weight < (int)FontWeight.Normal
            ? lighter.Concat(heavier).First()
            : heavier.Concat(lighter).First();
    }
}
