using System.Collections.Concurrent;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Resources;

using FC = Aprillz.MewUI.Native.Fontconfig.Fontconfig;

namespace Aprillz.MewUI.Rendering.FreeType;

/// <summary>
/// The fonts tried, in order, for what a font's own face lacks: the installed families listed after it in its
/// family list, those of the <see cref="FontFallback"/> chain, then the fonts fontconfig sorts after the primary one.
/// </summary>
internal sealed class FreeTypeFallbackSet
{
    private static ConcurrentDictionary<SetKey, FreeTypeFallbackSet> _sets = new();
    private static long _setsVersion = -1;

    private readonly SetKey _key;
    private readonly string[] _chainPaths;
    private readonly ConcurrentDictionary<uint, int> _entryByCodePoint = new();
    private readonly object _systemGate = new();
    private SystemEntry[]? _system;

    private FreeTypeFallbackSet(SetKey key, long version, string[] listed)
    {
        _key = key;
        Version = version;

        var chainPaths = new List<string>();
        foreach (string family in listed.Concat(FontFallback.GetChainSnapshot()))
        {
            // A missing family would bring fontconfig's stand-in, which the sorted list below already has.
            var path = LinuxFontResolver.ResolveInstalledFontPath(family, key.Weight, key.Italic);
            if (path != null && path != key.PrimaryPath && !chainPaths.Contains(path))
            {
                chainPaths.Add(path);
            }
        }

        _chainPaths = [.. chainPaths];
    }

    /// <summary>The fallback chain and registered fonts this set was built against.</summary>
    public long Version { get; }

    /// <summary>Whether nothing this set was built from has changed since.</summary>
    public bool IsCurrent => Version == CurrentVersion;

    private static long CurrentVersion => ((long)FontFallback.Version << 32) | (uint)FontRegistry.Version;

    /// <summary>
    /// The set for the font <paramref name="family"/> resolved to <paramref name="primaryPath"/>, with the families
    /// <paramref name="listed"/> after it in the requested family list.
    /// </summary>
    public static FreeTypeFallbackSet For(string family, string primaryPath, string[] listed, FontWeight weight, bool italic)
    {
        long version = CurrentVersion;
        var sets = _sets;
        if (Interlocked.Read(ref _setsVersion) != version)
        {
            sets = new ConcurrentDictionary<SetKey, FreeTypeFallbackSet>();
            _sets = sets;
            Interlocked.Exchange(ref _setsVersion, version);
        }

        return sets.GetOrAdd(
            new SetKey(family, primaryPath, string.Join(',', listed), weight, italic),
            static (key, argument) => new FreeTypeFallbackSet(key, argument.Version, argument.Listed),
            (Version: version, Listed: listed));
    }

    /// <summary>The face that draws <paramref name="codePoint"/>, or <see langword="null"/> when no installed font has it.</summary>
    public FreeTypeFaceCache.FaceEntry? FaceFor(uint codePoint, int pixelHeight)
    {
        if (!_entryByCodePoint.TryGetValue(codePoint, out int entry))
        {
            entry = FindEntry(codePoint, pixelHeight);
            _entryByCodePoint[codePoint] = entry;
        }

        return entry < 0 ? null : FreeTypeFaceCache.Instance.Get(PathOf(entry), pixelHeight, _key.Weight, _key.Italic);
    }

    private string PathOf(int entry) => entry < _chainPaths.Length ? _chainPaths[entry] : _system![entry - _chainPaths.Length].Path;

    private int FindEntry(uint codePoint, int pixelHeight)
    {
        for (int index = 0; index < _chainPaths.Length; index++)
        {
            if (FreeTypeFaceCache.Instance.Get(_chainPaths[index], pixelHeight, _key.Weight, _key.Italic).GetGlyphIndex(codePoint) != 0)
            {
                return index;
            }
        }

        var system = GetSystemEntries();
        for (int index = 0; index < system.Length; index++)
        {
            // The face confirms the character set: a collection's set may cover faces other than the one drawn.
            if (FC.FcCharSetHasChar(system[index].CharSet.DangerousGetHandle(), codePoint) &&
                FreeTypeFaceCache.Instance.Get(system[index].Path, pixelHeight, _key.Weight, _key.Italic).GetGlyphIndex(codePoint) != 0)
            {
                return _chainPaths.Length + index;
            }
        }

        return -1;
    }

    private SystemEntry[] GetSystemEntries()
    {
        var system = Volatile.Read(ref _system);
        if (system != null)
        {
            return system;
        }

        lock (_systemGate)
        {
            return _system ??= SortSystemFonts();
        }
    }

    /// <summary>Every installed font, in the order fontconfig prefers them for the primary family, with its character set.</summary>
    private SystemEntry[] SortSystemFonts()
    {
        var entries = new List<SystemEntry>();
        nint pattern = 0;
        nint fontSet = 0;
        try
        {
            FC.FcInit();
            pattern = FC.FcPatternCreate();
            if (pattern == 0)
            {
                return [];
            }

            FC.FcPatternAddString(pattern, FC.FC_FAMILY, _key.Family);
            FC.FcPatternAddInteger(pattern, FC.FC_WEIGHT, LinuxFontResolver.ToFcWeight(_key.Weight));
            FC.FcPatternAddInteger(pattern, FC.FC_SLANT, _key.Italic ? FC.FC_SLANT_ITALIC : FC.FC_SLANT_ROMAN);
            FC.FcConfigSubstitute(0, pattern, FC.FcMatchPattern);
            FC.FcDefaultSubstitute(pattern);

            fontSet = FC.FcFontSort(0, pattern, true, out _, out int result);
            if (fontSet == 0 || result != FC.FcResultMatch)
            {
                return [];
            }

            // FcFontSet: { int nfont; int sfont; FcPattern** fonts; }
            int count = Marshal.ReadInt32(fontSet, 0);
            nint fonts = Marshal.ReadIntPtr(fontSet, 2 * sizeof(int));
            var seen = new HashSet<string>(_chainPaths) { _key.PrimaryPath };
            for (int index = 0; index < count; index++)
            {
                nint font = Marshal.ReadIntPtr(fonts, index * nint.Size);
                if (font == 0 ||
                    FC.FcPatternGetString(font, FC.FC_FILE, 0, out nint filePointer) != FC.FcResultMatch ||
                    FC.FcPatternGetCharSet(font, FC.FC_CHARSET, 0, out nint charSet) != FC.FcResultMatch ||
                    filePointer == 0 || charSet == 0)
                {
                    continue;
                }

                string? path = Marshal.PtrToStringUTF8(filePointer);
                if (string.IsNullOrEmpty(path) || !seen.Add(path) || !File.Exists(path))
                {
                    continue;
                }

                entries.Add(new SystemEntry(path, new CharSetHandle(FC.FcCharSetCopy(charSet))));
            }
        }
        catch
        {
            // fontconfig missing or failing leaves the chain as the only fallback.
        }
        finally
        {
            if (fontSet != 0)
            {
                FC.FcFontSetDestroy(fontSet);
            }

            if (pattern != 0)
            {
                FC.FcPatternDestroy(pattern);
            }
        }

        return [.. entries];
    }

    private readonly record struct SetKey(string Family, string PrimaryPath, string Listed, FontWeight Weight, bool Italic);

    private readonly record struct SystemEntry(string Path, CharSetHandle CharSet);

    /// <summary>A reference to a fontconfig character set, released with the set that holds it.</summary>
    private sealed class CharSetHandle : SafeHandle
    {
        public CharSetHandle(nint charSet)
            : base(0, ownsHandle: true)
        {
            SetHandle(charSet);
        }

        public override bool IsInvalid => handle == 0;

        protected override bool ReleaseHandle()
        {
            FC.FcCharSetDestroy(handle);
            return true;
        }
    }
}
