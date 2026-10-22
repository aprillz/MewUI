using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Counts of one Starlight pass; unresolved links point above the api directory and were left as written.</summary>
public sealed record StarlightSummary(int Pages, int FrontMatterAdded, int LinksRewritten, int GuideLines, int UnresolvedLinks);

/// <summary>Turns xmldocmd pages into Starlight content: frontmatter with title and slug (the H1 that opens a page is dropped because Starlight renders the title), site-absolute links, and a "Guide:" line on type pages; re-running changes nothing.</summary>
public static class ApiStarlightWriter
{
    private const string PAGE_EXTENSION = ".md";
    private const string INDEX_STEM = "index";
    private const string INDEX_MEMBER_SEGMENT = "index-member";
    private const string API_SEGMENT = "api";
    private const string FRONT_MATTER_FENCE = "---";
    private const string TITLE_KEY = "title:";
    private const string SLUG_KEY = "slug:";
    private const string HEADING_PREFIX = "# ";
    private const string CODE_FENCE = "```";
    private const string GUIDE_PREFIX = "Guide: ";
    private const string GENERATED_MARKER = "<!-- DO NOT EDIT";
    private const string VERSION_FILE = "version.txt";
    private const string VERSION_PREFIX = "Generated from ";
    private const int TYPE_PAGE_DEPTH = 3;

    // Link text may hold one level of brackets (array types); the target stops at the first ')' or whitespace.
    private static readonly Regex _link = new(@"\[(?<text>(?:[^\[\]]|\[[^\[\]]*\])*)\]\((?<target>[^)\s]+)\)", RegexOptions.Compiled);

    /// <summary>Rewrites every page under the api directory in place; the site base is the path prefix of the site ("/MewUI" or empty for the root).</summary>
    public static StarlightSummary Run(string apiDirectory, string siteBase, string? guideLinksPath, IList<string> warnings)
    {
        var normalizedBase = NormalizeSiteBase(siteBase);
        var guides = guideLinksPath == null ? null : GuideLinkMap.Load(guideLinksPath);
        var versionLine = ApiIndexWriter.ReadVersionStamp(apiDirectory);
        var root = Path.GetFullPath(apiDirectory);
        var counters = new Counters();

        var pages = Directory.EnumerateFiles(root, "*" + PAGE_EXTENSION, SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (var pagePath in pages)
        {
            var relative = Path.GetRelativePath(root, pagePath).Replace('\\', '/');
            ProcessPage(pagePath, relative, normalizedBase, guides, versionLine, counters, warnings);
        }

        return new StarlightSummary(pages.Length, counters.FrontMatterAdded, counters.LinksRewritten, counters.GuideLines, counters.UnresolvedLinks);
    }

    /// <summary>Starlight slug of a page: the relative path lowercased without ".md", a trailing generated "index.md" folded into its directory (a member page "Index.md" becomes "index-member"), under "api".</summary>
    public static string SlugOf(string relativePagePath)
    {
        var segments = relativePagePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 0 && segments[^1].EndsWith(PAGE_EXTENSION, StringComparison.OrdinalIgnoreCase))
        {
            segments[^1] = segments[^1][..^PAGE_EXTENSION.Length];
        }

        // Only the generated "index.md" is a directory index; a member page xmldocmd names "Index.md" would collide with its type page once lowercased.
        if (segments.Count > 0 && segments[^1] == INDEX_STEM)
        {
            segments.RemoveAt(segments.Count - 1);
        }
        else if (segments.Count > 0 && string.Equals(segments[^1], INDEX_STEM, StringComparison.OrdinalIgnoreCase))
        {
            segments[^1] = INDEX_MEMBER_SEGMENT;
        }

        segments.Insert(0, API_SEGMENT);
        return string.Join("/", segments).ToLowerInvariant();
    }

    private static void ProcessPage(string pagePath, string relative, string siteBase, GuideLinkMap? guides, string? versionLine, Counters counters, IList<string> warnings)
    {
        var original = File.ReadAllText(pagePath);
        var lines = original.Split('\n').Select(static text => text.TrimEnd('\r')).ToList();
        TrimTrailingBlankLines(lines);

        var slug = SlugOf(relative);
        if (HasFrontMatter(lines))
        {
            EnsureSlug(lines, slug);
            RemoveLeadingHeading(lines);
        }
        else
        {
            var title = TakeTitle(lines, relative);
            lines.InsertRange(0, [FRONT_MATTER_FENCE, $"{TITLE_KEY} {QuoteYaml(title)}", $"{SLUG_KEY} {slug}", FRONT_MATTER_FENCE, string.Empty]);
            counters.FrontMatterAdded++;
        }

        lines.RemoveAll(static text => text.StartsWith(GENERATED_MARKER, StringComparison.Ordinal));

        var pageDirectory = relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : string.Empty;
        for (var index = FrontMatterEnd(lines) + 1; index < lines.Count; index++)
        {
            if (lines[index].Contains("](", StringComparison.Ordinal))
            {
                lines[index] = _link.Replace(lines[index], match => RewriteLink(match, relative, pageDirectory, siteBase, counters, warnings));
            }
        }

        if (versionLine != null && relative == INDEX_STEM + PAGE_EXTENSION)
        {
            EnsureVersionLine(lines, versionLine);
        }

        var segments = relative.Split('/');
        if (guides != null && segments.Length == TYPE_PAGE_DEPTH && segments[^1] != INDEX_STEM + PAGE_EXTENSION)
        {
            var typeName = $"{segments[1]}.{segments[2][..^PAGE_EXTENSION.Length]}";
            if (guides.TryResolve(typeName, out var url, out var text))
            {
                WriteGuideLine(lines, $"{GUIDE_PREFIX}[{text}]({url})");
                counters.GuideLines++;
            }
        }

        TrimTrailingBlankLines(lines);
        var output = string.Join("\n", lines) + "\n";
        if (output != original)
        {
            File.WriteAllText(pagePath, output, new UTF8Encoding(false));
        }
    }

    private static string RewriteLink(Match match, string relative, string pageDirectory, string siteBase, Counters counters, IList<string> warnings)
    {
        var target = match.Groups["target"].Value;
        if (target.StartsWith('#') || target.StartsWith('/') || target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return match.Value;
        }

        var anchorIndex = target.IndexOf('#');
        var path = anchorIndex < 0 ? target : target[..anchorIndex];
        var anchor = anchorIndex < 0 ? string.Empty : target[anchorIndex..];
        if (!path.EndsWith(PAGE_EXTENSION, StringComparison.OrdinalIgnoreCase))
        {
            return match.Value;
        }

        var resolved = ResolvePath(pageDirectory, path);
        if (resolved == null)
        {
            counters.UnresolvedLinks++;
            warnings.Add($"{relative}: link '{target}' points above the api directory and was left unchanged.");
            return match.Value;
        }

        counters.LinksRewritten++;
        return $"[{match.Groups["text"].Value}]({siteBase}/{SlugOf(resolved)}/{anchor})";
    }

    private static string? ResolvePath(string pageDirectory, string target)
    {
        var segments = new List<string>(pageDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries));
        foreach (var segment in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        return string.Join("/", segments);
    }

    private static bool HasFrontMatter(List<string> lines) => lines.Count > 0 && lines[0] == FRONT_MATTER_FENCE && FrontMatterEnd(lines) > 0;

    private static int FrontMatterEnd(List<string> lines)
    {
        if (lines.Count == 0 || lines[0] != FRONT_MATTER_FENCE)
        {
            return -1;
        }

        for (var index = 1; index < lines.Count; index++)
        {
            if (lines[index] == FRONT_MATTER_FENCE)
            {
                return index;
            }
        }

        return -1;
    }

    private static void EnsureSlug(List<string> lines, string slug)
    {
        var end = FrontMatterEnd(lines);
        var titleIndex = -1;
        for (var index = 1; index < end; index++)
        {
            if (lines[index].StartsWith(SLUG_KEY, StringComparison.Ordinal))
            {
                return;
            }

            if (lines[index].StartsWith(TITLE_KEY, StringComparison.Ordinal))
            {
                titleIndex = index;
            }
        }

        lines.Insert(titleIndex >= 0 ? titleIndex + 1 : end, $"{SLUG_KEY} {slug}");
    }

    private static string TakeTitle(List<string> lines, string relative)
    {
        var headingIndex = lines.FindIndex(static text => text.StartsWith(HEADING_PREFIX, StringComparison.Ordinal));
        if (headingIndex < 0)
        {
            return Path.GetFileNameWithoutExtension(relative);
        }

        var title = WebUtility.HtmlDecode(lines[headingIndex][HEADING_PREFIX.Length..].Trim());
        lines.RemoveAt(headingIndex);
        if (headingIndex < lines.Count && lines[headingIndex].Length == 0)
        {
            lines.RemoveAt(headingIndex);
        }

        return title;
    }

    private static void RemoveLeadingHeading(List<string> lines)
    {
        // Starlight renders the frontmatter title as the page heading; only an H1 that opens the body is its duplicate.
        var index = SkipBlankLines(lines, FrontMatterEnd(lines) + 1);
        if (index >= lines.Count || !lines[index].StartsWith(HEADING_PREFIX, StringComparison.Ordinal))
        {
            return;
        }

        lines.RemoveAt(index);
        while (index < lines.Count && lines[index].Length == 0)
        {
            lines.RemoveAt(index);
        }

        if (lines[index - 1].Length > 0)
        {
            lines.Insert(index, string.Empty);
        }
    }

    private static void EnsureVersionLine(List<string> lines, string versionLine)
    {
        if (lines.Any(static text => text.StartsWith(VERSION_PREFIX, StringComparison.Ordinal)))
        {
            return;
        }

        var index = SkipBlankLines(lines, FrontMatterEnd(lines) + 1);
        if (index < lines.Count && lines[index].StartsWith(HEADING_PREFIX, StringComparison.Ordinal))
        {
            index = SkipBlankLines(lines, index + 1);
        }

        lines.Insert(index, versionLine);
        lines.Insert(index + 1, string.Empty);
    }

    private static void WriteGuideLine(List<string> lines, string guideLine)
    {
        // A previous run inserted the line plus one blank separator; both go so re-runs leave the file unchanged.
        var existing = lines.FindIndex(static text => text.StartsWith(GUIDE_PREFIX, StringComparison.Ordinal));
        if (existing >= 0)
        {
            lines.RemoveAt(existing);
            if (existing < lines.Count && lines[existing].Length == 0)
            {
                lines.RemoveAt(existing);
            }
        }

        var index = SkipBlankLines(lines, FrontMatterEnd(lines) + 1);
        if (index < lines.Count && lines[index].StartsWith(HEADING_PREFIX, StringComparison.Ordinal))
        {
            index = SkipBlankLines(lines, index + 1);
        }

        if (index < lines.Count && lines[index].StartsWith(CODE_FENCE, StringComparison.Ordinal))
        {
            index++;
            while (index < lines.Count && lines[index] != CODE_FENCE)
            {
                index++;
            }

            index = Math.Min(index + 1, lines.Count);
        }
        else
        {
            while (index < lines.Count && lines[index].Length > 0)
            {
                index++;
            }
        }

        lines.Insert(index, string.Empty);
        lines.Insert(index + 1, guideLine);
    }

    private static int SkipBlankLines(List<string> lines, int index)
    {
        while (index < lines.Count && lines[index].Length == 0)
        {
            index++;
        }

        return index;
    }

    private static void TrimTrailingBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
    }

    private static string NormalizeSiteBase(string siteBase)
    {
        var trimmed = siteBase.Trim().TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    private static string QuoteYaml(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private sealed class Counters
    {
        public int FrontMatterAdded;
        public int LinksRewritten;
        public int GuideLines;
        public int UnresolvedLinks;
    }
}

/// <summary>Type-to-guide mapping from a JSON object; a key ending in "." matches every type below that namespace prefix, other keys match a type's page-path name exactly (generic arity as "-N").</summary>
public sealed class GuideLinkMap
{
    private const string REFERENCE_TEXT = "C# Markup reference: ";
    private const string GUIDE_TEXT = "C# Markup guide";

    private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
    private readonly List<KeyValuePair<string, string>> _prefixes = [];

    private GuideLinkMap()
    {
    }

    /// <summary>Reads the mapping file; a value that is not a string or a root that is not an object is an error.</summary>
    public static GuideLinkMap Load(string path)
    {
        var map = new GuideLinkMap();
        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{path}: the guide links file must be a JSON object of type name to URL.");
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"{path}: '{property.Name}' must map to a URL string.");
            }

            var url = property.Value.GetString()!;
            if (property.Name.EndsWith('.'))
            {
                map._prefixes.Add(new KeyValuePair<string, string>(property.Name, url));
            }
            else
            {
                map._exact[property.Name] = url;
            }
        }

        // Longest prefix first so a nested namespace entry wins over its parent.
        map._prefixes.Sort(static (left, right) => right.Key.Length.CompareTo(left.Key.Length));
        return map;
    }

    /// <summary>Resolves a type name to its guide URL and link text; exact entries win over prefix entries.</summary>
    public bool TryResolve(string typeName, out string url, out string text)
    {
        if (_exact.TryGetValue(typeName, out var exactUrl))
        {
            url = exactUrl;
            text = REFERENCE_TEXT + Humanize(exactUrl);
            return true;
        }

        foreach (var (prefix, prefixUrl) in _prefixes)
        {
            if (typeName.StartsWith(prefix, StringComparison.Ordinal))
            {
                url = prefixUrl;
                text = GUIDE_TEXT;
                return true;
            }
        }

        url = string.Empty;
        text = string.Empty;
        return false;
    }

    private static string Humanize(string url)
    {
        var segment = url.TrimEnd('/');
        var slash = segment.LastIndexOf('/');
        if (slash >= 0)
        {
            segment = segment[(slash + 1)..];
        }

        segment = segment.Replace('-', ' ').Replace('_', ' ');
        return segment.Length == 0 ? segment : char.ToUpperInvariant(segment[0]) + segment[1..];
    }
}
