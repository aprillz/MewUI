namespace Aprillz.MewUI.Resources;

/// <summary>One element of scanned SVG markup, with its attributes and children in document order.</summary>
internal sealed class SimpleSvgElement(string name)
{
    /// <summary>Local name, lower-cased. A prefixed name keeps its prefix so the reader can skip it.</summary>
    public string Name { get; } = name;

    public List<KeyValuePair<string, string>> Attributes { get; } = [];

    public List<SimpleSvgElement> Children { get; } = [];

    /// <summary>The attribute's value, or null when absent. Names are compared case-sensitively, as SVG does.</summary>
    public string? Attribute(string name)
    {
        foreach (var pair in Attributes)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return null;
    }
}

/// <summary>
/// Parses SVG markup into an element tree. Hand-written rather than XML-based: the core carries its own
/// decoders and must not pull in an XML reader, which every trimmed and AOT application would then ship.
/// </summary>
internal static class SimpleSvgMarkupParser
{
    /// <summary>Parses markup. Throws <see cref="FormatException"/> when it is malformed.</summary>
    public static SimpleSvgElement Parse(string markup)
    {
        int position = 0;
        SimpleSvgElement? root = null;
        var open = new Stack<SimpleSvgElement>();

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
                throw new FormatException("Markup ends inside a tag.");
            }

            char lead = markup[position];
            if (lead == '?')
            {
                position = SkipTo(markup, position, "?>");
                continue;
            }

            if (lead == '!')
            {
                position = SkipDeclaration(markup, position);
                continue;
            }

            if (lead == '/')
            {
                position = ReadName(markup, position + 1, out _);
                position = SkipTo(markup, position, ">");
                if (open.Count == 0)
                {
                    throw new FormatException("Closing tag without a matching open tag.");
                }
                open.Pop();
                if (open.Count == 0)
                {
                    break;
                }
                continue;
            }

            position = ReadName(markup, position, out string name);
            if (name.Length == 0)
            {
                throw new FormatException($"Empty element name at offset {start}.");
            }

            var element = new SimpleSvgElement(name);
            position = ReadAttributes(markup, position, element, out bool selfClosing);

            if (open.Count > 0)
            {
                open.Peek().Children.Add(element);
            }
            else if (root == null)
            {
                root = element;
            }
            else
            {
                // A second top-level element: the document already ended, so stop reading.
                break;
            }

            if (!selfClosing)
            {
                open.Push(element);
            }
            else if (open.Count == 0)
            {
                break;
            }
        }

        return root ?? throw new FormatException("No element found.");
    }

    // Reads an element or attribute name, which runs until whitespace or one of the tag delimiters.
    private static int ReadName(string markup, int position, out string name)
    {
        int start = position;
        while (position < markup.Length)
        {
            char c = markup[position];
            if (char.IsWhiteSpace(c) || c == '>' || c == '/' || c == '=')
            {
                break;
            }
            position++;
        }

        name = markup[start..position].ToLowerInvariant();
        return position;
    }

    private static int ReadAttributes(string markup, int position, SimpleSvgElement element, out bool selfClosing)
    {
        selfClosing = false;

        while (position < markup.Length)
        {
            while (position < markup.Length && char.IsWhiteSpace(markup[position]))
            {
                position++;
            }

            if (position >= markup.Length)
            {
                throw new FormatException("Markup ends inside a tag.");
            }

            char c = markup[position];
            if (c == '>')
            {
                return position + 1;
            }

            if (c == '/')
            {
                selfClosing = true;
                position++;
                continue;
            }

            position = ReadName(markup, position, out string name);
            if (name.Length == 0)
            {
                throw new FormatException($"Malformed attribute at offset {position}.");
            }

            while (position < markup.Length && char.IsWhiteSpace(markup[position]))
            {
                position++;
            }

            if (position >= markup.Length || markup[position] != '=')
            {
                // A valueless attribute is not SVG, but nothing in the accepted subset needs one either.
                element.Attributes.Add(new KeyValuePair<string, string>(name, string.Empty));
                continue;
            }

            position++;
            while (position < markup.Length && char.IsWhiteSpace(markup[position]))
            {
                position++;
            }

            if (position >= markup.Length)
            {
                throw new FormatException("Markup ends inside an attribute value.");
            }

            char quote = markup[position];
            if (quote != '"' && quote != '\'')
            {
                throw new FormatException($"Unquoted attribute value for '{name}'.");
            }

            int valueStart = position + 1;
            int valueEnd = markup.IndexOf(quote, valueStart);
            if (valueEnd < 0)
            {
                throw new FormatException($"Unterminated attribute value for '{name}'.");
            }

            element.Attributes.Add(new KeyValuePair<string, string>(name, markup[valueStart..valueEnd]));
            position = valueEnd + 1;
        }

        throw new FormatException("Markup ends inside a tag.");
    }

    // Skips a comment, a CDATA section or a DOCTYPE, including a DOCTYPE's internal subset.
    private static int SkipDeclaration(string markup, int position)
    {
        if (markup.AsSpan(position).StartsWith("!--", StringComparison.Ordinal))
        {
            return SkipTo(markup, position, "-->");
        }

        if (markup.AsSpan(position).StartsWith("![CDATA[", StringComparison.Ordinal))
        {
            return SkipTo(markup, position, "]]>");
        }

        int depth = 0;
        while (position < markup.Length)
        {
            char c = markup[position++];
            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
            }
            else if (c == '>' && depth <= 0)
            {
                return position;
            }
        }

        throw new FormatException("Markup ends inside a declaration.");
    }

    private static int SkipTo(string markup, int position, string terminator)
    {
        int index = markup.IndexOf(terminator, position, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new FormatException($"Missing '{terminator}'.");
        }

        return index + terminator.Length;
    }
}
