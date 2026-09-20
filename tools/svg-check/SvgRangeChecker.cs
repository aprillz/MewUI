namespace Aprillz.MewUI.SvgCheck;

/// <summary>What a scan found in one document.</summary>
internal sealed class SvgCheckResult
{
    /// <summary>Feature names that put the document out of range, with how often each appears.</summary>
    public SortedDictionary<string, int> Reasons { get; } = new(StringComparer.Ordinal);

    /// <summary>In-range features few documents use, worth a look when picking test fixtures.</summary>
    public SortedSet<string> Notes { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Walks the tags of an SVG document and classifies what it uses. Deliberately shallow: it reports
/// which features appear, not whether they would render correctly.
/// </summary>
internal static class SvgRangeChecker
{
    public static SvgCheckResult Check(string markup)
    {
        var result = new SvgCheckResult();
        int position = 0;
        bool sawRoot = false;

        while (true)
        {
            int start = markup.IndexOf('<', position);
            if (start < 0)
            {
                break;
            }

            position = start + 1;
            if (position >= markup.Length)
            {
                throw new FormatException("markup ends inside a tag");
            }

            char lead = markup[position];
            if (lead == '?' || lead == '!' || lead == '/')
            {
                position = SkipTo(markup, position, '>');
                continue;
            }

            int nameEnd = position;
            while (nameEnd < markup.Length && !char.IsWhiteSpace(markup[nameEnd]) &&
                   markup[nameEnd] != '>' && markup[nameEnd] != '/')
            {
                nameEnd++;
            }

            string name = markup[position..nameEnd].ToLowerInvariant();
            int tagEnd = SkipTo(markup, nameEnd, '>');
            string body = markup[nameEnd..Math.Max(nameEnd, tagEnd - 1)];
            position = tagEnd;

            if (!sawRoot)
            {
                if (name != "svg")
                {
                    throw new FormatException($"root element is '{name}', not 'svg'");
                }
                sawRoot = true;
                CheckRootCoordinates(body, result);
            }

            Classify(name, body, result);
        }

        if (!sawRoot)
        {
            throw new FormatException("no svg element");
        }

        return result;
    }

    private static void Classify(string name, string body, SvgCheckResult result)
    {
        // A prefixed name belongs to an editor's own namespace, which the reader skips without loss.
        if (name.Contains(':', StringComparison.Ordinal))
        {
            return;
        }

        if (SvgRange.Rejected.Contains(name))
        {
            Count(result, name);
        }
        else if (!SvgRange.Accepted.Contains(name))
        {
            Count(result, $"unknown:{name}");
        }

        foreach (string attribute in AttributeNames(body))
        {
            if (attribute.Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            if (SvgRange.RejectedAttributes.Contains(attribute))
            {
                Count(result, attribute);
            }
            else if (SvgRange.Uncommon.Contains(attribute))
            {
                result.Notes.Add(attribute);
            }
        }
    }

    private static void CheckRootCoordinates(string body, SvgCheckResult result)
    {
        string? viewBox = null;
        string? width = null;
        string? height = null;

        foreach (var (name, value) in Attributes(body))
        {
            switch (name)
            {
                case "viewbox": viewBox = value; break;
                case "width": width = value; break;
                case "height": height = value; break;
            }
        }

        if (viewBox == null)
        {
            result.Notes.Add("no-viewbox");
            if (width == null || height == null || width.EndsWith('%') || height.EndsWith('%'))
            {
                Count(result, "no-coordinate-system");
            }
            return;
        }

        var parts = viewBox.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && (parts[0].StartsWith('-') || parts[1].StartsWith('-')))
        {
            result.Notes.Add("negative-viewbox-origin");
        }

        if (width != null && width.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            result.Notes.Add("length-px");
        }
    }

    private static IEnumerable<string> AttributeNames(string body)
    {
        foreach (var (name, _) in Attributes(body))
        {
            yield return name;
        }
    }

    private static IEnumerable<(string Name, string Value)> Attributes(string body)
    {
        int position = 0;
        while (position < body.Length)
        {
            while (position < body.Length && (char.IsWhiteSpace(body[position]) || body[position] == '/'))
            {
                position++;
            }

            int nameStart = position;
            while (position < body.Length && body[position] != '=' && !char.IsWhiteSpace(body[position]))
            {
                position++;
            }

            if (position >= body.Length || nameStart == position)
            {
                yield break;
            }

            string name = body[nameStart..position].ToLowerInvariant();
            while (position < body.Length && char.IsWhiteSpace(body[position]))
            {
                position++;
            }

            if (position >= body.Length || body[position] != '=')
            {
                continue;
            }

            position++;
            while (position < body.Length && char.IsWhiteSpace(body[position]))
            {
                position++;
            }

            if (position >= body.Length)
            {
                yield break;
            }

            char quote = body[position];
            if (quote != '"' && quote != '\'')
            {
                continue;
            }

            int valueStart = position + 1;
            int valueEnd = body.IndexOf(quote, valueStart);
            if (valueEnd < 0)
            {
                yield break;
            }

            yield return (name, body[valueStart..valueEnd]);
            position = valueEnd + 1;
        }
    }

    private static void Count(SvgCheckResult result, string reason)
    {
        result.Reasons.TryGetValue(reason, out int total);
        result.Reasons[reason] = total + 1;
    }

    private static int SkipTo(string markup, int position, char terminator)
    {
        int index = markup.IndexOf(terminator, position);
        if (index < 0)
        {
            throw new FormatException($"missing '{terminator}'");
        }

        return index + 1;
    }
}
