using Aprillz.MewUI;

namespace Svg;

/// <summary>
/// Manages access to registered/private fonts for the MewUI rendering path.
/// </summary>
public sealed class SvgFontManager : IDisposable
{
    private static readonly string[][] DefaultLocalizedFamilyNames =
    [
        ["Meiryo", "メイリオ"],
        ["MS Gothic", "ＭＳ ゴシック"],
        ["MS Mincho", "ＭＳ 明朝"],
    ];

    public static List<string[]> LocalizedFamilyNames { get; } = [];
    public static List<string> PrivateFontPathList { get; } = [];
    public static List<byte[]> PrivateFontDataList { get; } = [];

    private readonly List<string[]> _localizedFamilyNames = [];
    private readonly HashSet<string> _registeredFamilies = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly List<FontResource> _privateFontResources = [];
    public SvgFontManager()
    {
        RegisterPrivateFontPaths();
        RegisterPrivateFontData();

        _localizedFamilyNames.AddRange(LocalizedFamilyNames);
        _localizedFamilyNames.AddRange(DefaultLocalizedFamilyNames);
    }

    public string? FindFont(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var familyNames = _localizedFamilyNames.Find(f => f.Contains(name, StringComparer.CurrentCultureIgnoreCase))
            ?? [name];

        foreach (var familyName in familyNames)
        {
            if (TryResolveFont(familyName, out var resolved))
            {
                return resolved;
            }
        }

        // Generic CSS family (serif/sans-serif/monospace) → platform-default name.
        var generic = ResolveGenericFamily(name.Trim());
        if (generic is not null)
        {
            return generic;
        }

        // Not in registered/private fonts and not a generic family — assume it's a
        // system-installed family (e.g. "Verdana", "Arial") and let the backend's
        // CreateFont resolve via the OS font enumeration. Without this, ValidateFontFamily
        // skips to the next entry in the font-family list and ultimately falls through
        // to "sans-serif" → Segoe UI even when the requested font is installed.
        return name.Trim();
    }

    public void Dispose()
    {
        foreach (var fontResource in _privateFontResources)
        {
            fontResource.Dispose();
        }
    }

    private void RegisterPrivateFontPaths()
    {
        foreach (var path in PrivateFontPathList)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                continue;
            }

            if (!File.Exists(fullPath))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(fullPath);
                var resource = FontResources.Register(stream, Path.GetExtension(fullPath), Path.GetFileNameWithoutExtension(fullPath));
                _privateFontResources.Add(resource);
                if (!string.IsNullOrWhiteSpace(resource.ParsedFamilyName))
                {
                    _registeredFamilies.Add(resource.ParsedFamilyName);
                }
            }
            catch
            {
            }
        }
    }

    private void RegisterPrivateFontData()
    {
        foreach (var data in PrivateFontDataList)
        {
            if (data is null || data.Length == 0)
            {
                continue;
            }

            using var stream = new MemoryStream(data, writable: false);
            var resource = FontResources.Register(stream, ".ttf");
            _privateFontResources.Add(resource);
            if (!string.IsNullOrWhiteSpace(resource.ParsedFamilyName))
            {
                _registeredFamilies.Add(resource.ParsedFamilyName);
            }
        }
    }

    private bool TryResolveFont(string familyName, out string? resolved)
    {
        resolved = null;
        familyName = familyName.Trim();
        if (familyName.Length == 0)
        {
            return false;
        }

        if (FontResources.LooksLikeFontFilePath(familyName))
        {
            try
            {
                using var stream = File.OpenRead(familyName);
                var resource = FontResources.Register(stream, Path.GetExtension(familyName), Path.GetFileNameWithoutExtension(familyName));
                _privateFontResources.Add(resource);
                if (!string.IsNullOrWhiteSpace(resource.ParsedFamilyName))
                {
                    _registeredFamilies.Add(resource.ParsedFamilyName);
                    resolved = resource.ParsedFamilyName;
                    return true;
                }
            }
            catch
            {
            }
        }

        if (_registeredFamilies.Contains(familyName))
        {
            resolved = familyName;
            return true;
        }

        return false;
    }

    private static string? ResolveGenericFamily(string familyName)
    {
        switch (familyName.ToLowerInvariant())
        {
            case "serif":
                return OperatingSystem.IsWindows() ? "Times New Roman"
                    : OperatingSystem.IsMacOS() ? "Times"
                    : "serif";
            case "sans-serif":
            case "sans":
                return OperatingSystem.IsWindows() ? "Segoe UI"
                    : OperatingSystem.IsMacOS() ? ".AppleSystemUIFont"
                    : "sans-serif";
            case "monospace":
                return OperatingSystem.IsWindows() ? "Consolas"
                    : OperatingSystem.IsMacOS() ? "Menlo"
                    : "monospace";
            default:
                return null;
        }
    }
}
