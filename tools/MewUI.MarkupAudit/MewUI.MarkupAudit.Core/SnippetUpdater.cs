using System.Text;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Copies <c>#region</c> bodies of a C# file into the fenced blocks between <c>&lt;!-- snippet:name --&gt;</c> and <c>&lt;!-- /snippet --&gt;</c> markers of a Markdown file.</summary>
public static class SnippetUpdater
{
    private const string REGION_START = "#region ";
    private const string REGION_END = "#endregion";
    private const string MARKER_START = "<!-- snippet:";
    private const string MARKER_END = "<!-- /snippet -->";
    private const string MARKER_SUFFIX = " -->";
    private const string FENCE = "```csharp";
    private const string FENCE_END = "```";

    /// <summary>Reads every region of the file as a dedented LF-joined body keyed by region name.</summary>
    public static IReadOnlyDictionary<string, string> ReadRegions(string csharpPath)
    {
        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentName = null;
        var body = new List<string>();

        foreach (var rawLine in File.ReadAllLines(csharpPath))
        {
            var trimmed = rawLine.Trim();
            if (trimmed.StartsWith(REGION_START, StringComparison.Ordinal))
            {
                if (currentName != null)
                {
                    throw new InvalidOperationException($"{csharpPath}: region '{currentName}' contains a nested region.");
                }

                currentName = trimmed[REGION_START.Length..].Trim();
                body.Clear();
            }
            else if (trimmed == REGION_END)
            {
                if (currentName == null)
                {
                    throw new InvalidOperationException($"{csharpPath}: #endregion without #region.");
                }

                if (!regions.TryAdd(currentName, Dedent(body)))
                {
                    throw new InvalidOperationException($"{csharpPath}: region '{currentName}' is declared twice.");
                }

                currentName = null;
            }
            else if (currentName != null)
            {
                body.Add(rawLine.TrimEnd('\r'));
            }
        }

        if (currentName != null)
        {
            throw new InvalidOperationException($"{csharpPath}: region '{currentName}' is not closed.");
        }

        return regions;
    }

    /// <summary>Returns the Markdown with every snippet block replaced by its region body; the result uses LF line endings.</summary>
    public static string Apply(string markdown, IReadOnlyDictionary<string, string> regions, string markdownName)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var builder = new StringBuilder();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index];
            builder.Append(line).Append('\n');
            index++;

            var trimmed = line.Trim();
            if (!trimmed.StartsWith(MARKER_START, StringComparison.Ordinal) || !trimmed.EndsWith(MARKER_SUFFIX, StringComparison.Ordinal))
            {
                continue;
            }

            var name = trimmed[MARKER_START.Length..^MARKER_SUFFIX.Length].Trim();
            if (!regions.TryGetValue(name, out var body))
            {
                throw new InvalidOperationException($"{markdownName}: snippet '{name}' has no #region in the snippet source.");
            }

            var end = Array.FindIndex(lines, index, static candidate => candidate.Trim() == MARKER_END);
            if (end < 0)
            {
                throw new InvalidOperationException($"{markdownName}: snippet '{name}' has no closing {MARKER_END} marker.");
            }

            builder.Append(FENCE).Append('\n').Append(body).Append('\n').Append(FENCE_END).Append('\n');
            builder.Append(MARKER_END).Append('\n');
            index = end + 1;
        }

        // Every element got a newline appended, including the last one, so the original ending is restored by dropping it.
        if (builder.Length > 0)
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    /// <summary>Rewrites the file when its snippet blocks differ from the regions; returns true when it was rewritten.</summary>
    public static bool UpdateFile(string markdownPath, IReadOnlyDictionary<string, string> regions)
    {
        var original = File.ReadAllText(markdownPath);
        var updated = Apply(original, regions, markdownPath);
        if (updated == original)
        {
            return false;
        }

        File.WriteAllText(markdownPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return true;
    }

    private static string Dedent(List<string> lines)
    {
        var start = 0;
        var end = lines.Count;
        while (start < end && lines[start].Trim().Length == 0)
        {
            start++;
        }

        while (end > start && lines[end - 1].Trim().Length == 0)
        {
            end--;
        }

        var content = lines.GetRange(start, end - start);
        var indent = content
            .Where(static line => line.Trim().Length > 0)
            .Select(static line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join('\n', content.Select(line => line.Length >= indent ? line[indent..] : line.TrimStart()));
    }
}
