using System.Reflection;
using System.Text.RegularExpressions;

namespace Aprillz.MewUI.Gallery;

/// <summary>
/// Parses WPF-style XAML icon resource dictionaries and extracts PathGeometry data.
/// </summary>
static partial class IconResource
{
    public sealed record IconEntry(string Name, string PathData);

    private static string? _lastXaml;
    private static IconEntry[] _cached = [];

    /// <summary>
    /// Parses the icon dictionary the host supplied, or returns an empty set while it is still on
    /// its way. Results are cached per document so repeated calls during a page build are cheap.
    /// </summary>
    public static IconEntry[] GetAll(string? xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml))
        {
            return [];
        }

        if (string.Equals(_lastXaml, xaml, StringComparison.Ordinal))
        {
            return _cached;
        }

        var list = new List<IconEntry>();
        Load(xaml, list);
        _lastXaml = xaml;
        _cached = [.. list];
        return _cached;
    }

    private static void LoadFromResource(string resourceName, List<IconEntry> list)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return;

        using var reader = new StreamReader(stream);
        var xaml = reader.ReadToEnd();

        Load(xaml, list);
    }

    private static void Load(string xaml, List<IconEntry> list)
    {
        // Pattern 1: <PathGeometry x:Key="...">...content...</PathGeometry>
        foreach (Match m in ContentRegex().Matches(xaml))
        {
            var key = m.Groups[1].Value;
            var data = Normalize(m.Groups[2].Value);
            if (data.Length > 0)
                list.Add(new IconEntry(key, data));
        }

        // Pattern 2: <PathGeometry x:Key="..." ... Figures="..." />
        foreach (Match m in FiguresRegex().Matches(xaml))
        {
            var key = m.Groups[1].Value;
            var data = Normalize(m.Groups[2].Value);
            if (data.Length > 0 && !list.Exists(e => e.Name == key))
                list.Add(new IconEntry(key, data));
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    private static string Normalize(string data) =>
        WhitespaceRegex().Replace(data.Trim(), " ");

    // Matches: <PathGeometry x:Key="KEY">CONTENT</PathGeometry>
    // (?<!/) ensures the > is NOT preceded by / (excludes self-closing />)
    [GeneratedRegex(
        @"<PathGeometry\s+x:Key=""([^""]+)""[^>]*(?<!/)>\s*([\s\S]*?)\s*</PathGeometry>",
        RegexOptions.Compiled)]
    private static partial Regex ContentRegex();

    // Matches: <PathGeometry x:Key="KEY" ... Figures="DATA" />
    [GeneratedRegex(
        @"<PathGeometry\s+x:Key=""([^""]+)""[^>]*\sFigures=""([^""]+)""",
        RegexOptions.Compiled)]
    private static partial Regex FiguresRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
