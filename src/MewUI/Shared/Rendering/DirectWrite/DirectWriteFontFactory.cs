using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.DirectWrite;

/// <summary>
/// Owns the DirectWrite factory and the font family resolution shared by every backend that
/// creates <see cref="DirectWriteFont"/>. Holds no rendering state, so a backend can use it
/// without taking on Direct2D.
/// </summary>
internal sealed unsafe class DirectWriteFontFactory : IDisposable
{
    private nint _factory;
    private bool _disposed;

    // Cache: family name to a DirectWrite custom font collection built from a registered file.
    private readonly Dictionary<string, nint> _privateFontCollections = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The shared <c>IDWriteFactory</c>, created on first access. Never 0 once returned.</summary>
    internal nint Factory
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_factory == 0)
            {
                int hr = Native.DirectWrite.DWrite.DWriteCreateFactory(
                    DWRITE_FACTORY_TYPE.SHARED, Native.DirectWrite.DWrite.IID_IDWriteFactory, out _factory);
                if (hr < 0 || _factory == 0)
                {
                    throw new InvalidOperationException($"DWriteCreateFactory failed: 0x{hr:X8}");
                }
            }

            return _factory;
        }
    }

    internal DirectWriteFont CreateFont(string family, double size, FontWeight weight,
        bool italic, bool underline, bool strikethrough, uint dpi = 96, bool gridFitMetrics = false)
    {
        family = SelectFamilyCandidate(ValidateFamilyName(family));
        var (resolvedFamily, fontCollection) = ResolveWithCollection(family);
        return new DirectWriteFont(resolvedFamily, size, weight, italic, underline, strikethrough,
            Factory, fontCollection, dpi, gridFitMetrics);
    }

    /// <summary>Picks the first installed family from a comma-separated list; single names pass through.</summary>
    internal string SelectFamilyCandidate(string family)
    {
        if (!FontFamilyList.IsList(family))
        {
            return family;
        }

        string[] candidates = FontFamilyList.Split(family);
        foreach (string candidate in candidates)
        {
            if (FontRegistry.Resolve(candidate) != null || IsSystemFamilyInstalled(candidate))
            {
                return candidate;
            }
        }
        return candidates.Length > 0 ? candidates[0] : family;
    }

    internal static string ValidateFamilyName(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            throw new ArgumentException("Font family must be provided by the caller.", nameof(family));
        }

        return family.Trim();
    }

    /// <summary>Forces DirectWrite to re-read the system font set, picking up newly installed families.</summary>
    internal void RefreshSystemFontCollection()
    {
        int hr = DWriteVTable.GetSystemFontCollection((IDWriteFactory*)Factory, out var collection, checkForUpdates: true);
        if (hr >= 0 && collection != 0)
        {
            ComHelpers.Release(collection);
        }
    }

    internal (string Family, nint FontCollection) ResolveWithCollection(string familyOrPath)
    {
        familyOrPath = ValidateFamilyName(familyOrPath);
        var resolved = FontRegistry.Resolve(familyOrPath);
        if (resolved != null)
        {
            // The GDI text path may still be the one that draws this font, and its family lookup
            // only sees files that were registered with GDI.
            if (OperatingSystem.IsWindows())
            {
                Win32Fonts.EnsurePrivateFontFamily(resolved.Value.FilePath);
            }

            var fontCollection = GetOrCreatePrivateCollection(resolved.Value.FamilyName, resolved.Value.FilePath);
            return (resolved.Value.FamilyName, fontCollection);
        }

        if (OperatingSystem.IsWindows() && FontResources.LooksLikeFontFilePath(familyOrPath))
        {
            var path = Path.GetFullPath(familyOrPath);
            Win32Fonts.EnsurePrivateFontFamily(path);
            var family = FontResources.TryGetParsedFamilyName(path, out var parsed) && !string.IsNullOrWhiteSpace(parsed)
                ? parsed
                : Path.GetFileNameWithoutExtension(path);
            var fontCollection = GetOrCreatePrivateCollection(family, path);
            return (family, fontCollection);
        }

        return (familyOrPath, 0);
    }

    private bool IsSystemFamilyInstalled(string family)
    {
        if (DWriteVTable.GetSystemFontCollection((IDWriteFactory*)Factory, out nint collection, false) < 0 || collection == 0)
        {
            return false;
        }

        try
        {
            return DWriteVTable.FindFamilyName(collection, family, out _, out int exists) >= 0 && exists != 0;
        }
        finally
        {
            ComHelpers.Release(collection);
        }
    }

    private nint GetOrCreatePrivateCollection(string familyName, string filePath)
    {
        if (_privateFontCollections.TryGetValue(familyName, out var cached))
        {
            return cached;
        }

        var collection = DWritePrivateFontCollection.CreateCollection((IDWriteFactory*)Factory, [filePath]);
        if (collection != 0)
        {
            _privateFontCollections[familyName] = collection;
        }

        return collection;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var collection in _privateFontCollections.Values)
        {
            ComHelpers.Release(collection);
        }
        _privateFontCollections.Clear();

        ComHelpers.Release(_factory);
        _factory = 0;
    }
}
