using System.Globalization;
using System.Text;
using Aprillz.MewUI;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Markdown;

internal sealed class MarkdownHtmlParser
{
    private readonly List<MarkdownHtmlFrame> _frames = [];

    public MarkdownHtmlStyle CurrentStyle { get; private set; } = MarkdownHtmlStyle.Empty;

    public bool TryApplyTag(string tagText, out MarkdownHtmlToken token)
    {
        token = default;
        if (TryReadComment(tagText, 0, out int commentEnd) && commentEnd == tagText.Length)
        {
            token = new MarkdownHtmlToken(MarkdownHtmlTokenKind.Comment, CurrentStyle);
            return true;
        }

        if (tagText.Length > TextMarkupConstants.MAX_TAG_LENGTH ||
            tagText.Length < 3 ||
            tagText[0] != '<' ||
            tagText[^1] != '>' ||
            !TryParseTagContent(tagText.AsSpan(1, tagText.Length - 2), out var tag) ||
            !IsSupportedTag(tag.Name))
        {
            return false;
        }

        if (tag.Name == "br")
        {
            if (tag.IsClosing)
            {
                return false;
            }

            token = new MarkdownHtmlToken(MarkdownHtmlTokenKind.Break, CurrentStyle);
            return true;
        }

        if (tag.IsClosing)
        {
            for (int frameIndex = _frames.Count - 1; frameIndex >= 0; frameIndex--)
            {
                if (_frames[frameIndex].Name == tag.Name)
                {
                    CurrentStyle = _frames[frameIndex].PreviousStyle;
                    _frames.RemoveRange(frameIndex, _frames.Count - frameIndex);
                    token = new MarkdownHtmlToken(MarkdownHtmlTokenKind.Close, CurrentStyle);
                    return true;
                }
            }

            return false;
        }

        if (_frames.Count >= TextMarkupConstants.MAX_NESTING)
        {
            return false;
        }

        MarkdownHtmlStyle previousStyle = CurrentStyle;
        if (!TryApplyOpeningTag(tag, out var style))
        {
            return false;
        }

        CurrentStyle = style;
        if (!tag.IsSelfClosing)
        {
            _frames.Add(new MarkdownHtmlFrame(tag.Name, previousStyle));
        }
        else
        {
            CurrentStyle = previousStyle;
        }

        token = new MarkdownHtmlToken(MarkdownHtmlTokenKind.Open, CurrentStyle);
        return true;
    }

    public static bool TryParseBlock(string source, out MarkdownHtmlBlock block)
    {
        block = MarkdownHtmlBlock.Empty;
        var parser = new MarkdownHtmlParser();
        var builder = new StringBuilder(source.Length);
        var spans = new List<MarkdownHtmlTextSpan>();
        int sourceIndex = 0;

        while (sourceIndex < source.Length)
        {
            if (source[sourceIndex] == '<')
            {
                if (source.AsSpan(sourceIndex).StartsWith("<!--", StringComparison.Ordinal))
                {
                    if (!TryReadComment(source, sourceIndex, out int commentEnd))
                    {
                        return false;
                    }

                    sourceIndex = commentEnd;
                    continue;
                }

                if (!TryReadTag(source, sourceIndex, out int tagEnd, out string tagText) ||
                    !parser.TryApplyTag(tagText, out var token))
                {
                    return false;
                }

                if (token.Kind == MarkdownHtmlTokenKind.Break)
                {
                    Append(builder, spans, "\n".AsSpan(), token.Style);
                }

                sourceIndex = tagEnd;
                continue;
            }

            if (source[sourceIndex] == '&' &&
                TryDecodeEntity(source, sourceIndex, out int entityEnd, out string decoded))
            {
                Append(builder, spans, decoded.AsSpan(), parser.CurrentStyle);
                sourceIndex = entityEnd;
                continue;
            }

            int textEnd = sourceIndex + 1;
            while (textEnd < source.Length && source[textEnd] != '<' && source[textEnd] != '&')
            {
                textEnd++;
            }

            Append(builder, spans, source.AsSpan(sourceIndex, textEnd - sourceIndex), parser.CurrentStyle);
            sourceIndex = textEnd;
        }

        block = new MarkdownHtmlBlock(builder.ToString(), spans.ToArray());
        return true;
    }

    private bool TryApplyOpeningTag(in ParsedHtmlTag tag, out MarkdownHtmlStyle style)
    {
        style = CurrentStyle;
        switch (tag.Name)
        {
            case "sup":
                return TryApplyScript(style, TextMarkupConstants.SUPERSCRIPT_SHIFT, out style);
            case "sub":
                return TryApplyScript(style, TextMarkupConstants.SUBSCRIPT_SHIFT, out style);
            case "b":
            case "strong":
                style = style with { FontWeight = FontWeight.Bold };
                break;
            case "i":
            case "em":
                style = style with { Italic = true };
                break;
            case "u":
                style = style with { Decoration = style.Decoration | TextDecoration.Underline };
                break;
            case "s":
            case "del":
                style = style with { Decoration = style.Decoration | TextDecoration.Strikethrough };
                break;
            case "tt":
            case "code":
                style = style with { FontFamily = null, UsesMonospaceFont = true };
                break;
            case "big":
                style = ScaleFontSize(style, TextMarkupConstants.RELATIVE_SIZE_STEP);
                break;
            case "small":
                style = ScaleFontSize(style, 1.0 / TextMarkupConstants.RELATIVE_SIZE_STEP);
                break;
            case "span":
            case "font":
                ApplyAttributes(tag.Attributes, ref style);
                break;
        }

        return true;
    }

    private static void ApplyAttributes(IReadOnlyDictionary<string, string?> attributes, ref MarkdownHtmlStyle style)
    {
        if (attributes.TryGetValue("font", out string? fontFamily) && !string.IsNullOrWhiteSpace(fontFamily))
        {
            style = style with { FontFamily = fontFamily.Trim(), UsesMonospaceFont = false };
        }

        if (attributes.TryGetValue("size", out string? size) && TryParseFontSize(size, out double value, out bool relative))
        {
            style = relative
                ? ScaleFontSize(style, value)
                : style with { FontSize = value, FontSizeScale = 1.0 };
        }

        if (attributes.TryGetValue("weight", out string? weight) && TryParseFontWeight(weight, out var fontWeight))
        {
            style = style with { FontWeight = fontWeight };
        }

        if (attributes.TryGetValue("color", out string? foreground) &&
            MarkdownHtmlColorParser.TryParse(foreground, out var foregroundColor))
        {
            style = style with { Foreground = foregroundColor };
        }

        if (attributes.TryGetValue("background", out string? background) &&
            MarkdownHtmlColorParser.TryParse(background, out var backgroundColor))
        {
            style = style with { Background = backgroundColor };
        }

        if (attributes.TryGetValue("underline", out string? underline) &&
            TryParseBoolean(underline, out bool isUnderlined))
        {
            TextDecoration decoration = isUnderlined
                ? style.Decoration | TextDecoration.Underline
                : style.Decoration & ~TextDecoration.Underline;
            style = style with { Decoration = decoration };
        }

        if (attributes.TryGetValue("strikethrough", out string? strikethrough) &&
            TryParseBoolean(strikethrough, out bool isStruckThrough))
        {
            TextDecoration decoration = isStruckThrough
                ? style.Decoration | TextDecoration.Strikethrough
                : style.Decoration & ~TextDecoration.Strikethrough;
            style = style with { Decoration = decoration };
        }
    }

    private static MarkdownHtmlStyle ScaleFontSize(in MarkdownHtmlStyle style, double scale)
    {
        if (style.FontSize is double absoluteSize)
        {
            return style with { FontSize = absoluteSize * scale };
        }

        return style with { FontSizeScale = style.FontSizeScale * scale };
    }

    /// <summary>Applies the superscript size and shift to a style; false when the result is not a usable style.</summary>
    internal static bool TryApplySuperscript(in MarkdownHtmlStyle style, out MarkdownHtmlStyle scripted)
        => TryApplyScript(style, TextMarkupConstants.SUPERSCRIPT_SHIFT, out scripted);

    private static bool TryApplyScript(
        in MarkdownHtmlStyle style,
        double shift,
        out MarkdownHtmlStyle scripted)
    {
        if (style.FontSize is double absoluteSize)
        {
            scripted = style with
            {
                FontSize = absoluteSize * TextMarkupConstants.SCRIPT_SIZE_SCALE,
                BaselineOffset = style.BaselineOffset + shift * absoluteSize
            };
        }
        else
        {
            scripted = style with
            {
                FontSizeScale = style.FontSizeScale * TextMarkupConstants.SCRIPT_SIZE_SCALE,
                BaselineOffsetScale = style.BaselineOffsetScale + shift * style.FontSizeScale
            };
        }

        bool usableSize = scripted.FontSize is double size
            ? double.IsFinite(size) && size > 0
            : double.IsFinite(scripted.FontSizeScale) && scripted.FontSizeScale > 0;
        return usableSize &&
            TextLayoutLimits.IsValidBaselineOffset(scripted.BaselineOffset) &&
            TextLayoutLimits.IsValidBaselineOffset(scripted.BaselineOffsetScale);
    }

    private static bool TryParseFontSize(string? text, out double value, out bool relative)
    {
        value = 0;
        relative = false;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> valueText = text.AsSpan().Trim();
        if (valueText.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            valueText = valueText[..^2].TrimEnd();
        }
        else if (valueText.EndsWith("x", StringComparison.OrdinalIgnoreCase))
        {
            valueText = valueText[..^1].TrimEnd();
            relative = true;
        }

        return double.TryParse(valueText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) && value > 0;
    }

    private static bool TryParseFontWeight(string? text, out FontWeight weight)
    {
        weight = FontWeight.Normal;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        weight = normalized switch
        {
            "thin" or "100" => FontWeight.Thin,
            "extralight" or "200" => FontWeight.ExtraLight,
            "light" or "300" => FontWeight.Light,
            "normal" or "regular" or "400" => FontWeight.Normal,
            "medium" or "500" => FontWeight.Medium,
            "semibold" or "600" => FontWeight.SemiBold,
            "bold" or "700" => FontWeight.Bold,
            "extrabold" or "800" => FontWeight.ExtraBold,
            "black" or "900" => FontWeight.Black,
            _ => (FontWeight)0
        };
        return weight != (FontWeight)0;
    }

    private static bool TryParseBoolean(string? text, out bool value)
    {
        if (text is null || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1")
        {
            value = true;
            return true;
        }

        if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0")
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static bool IsSupportedTag(string name)
        => name is "b" or "strong" or "i" or "em" or "u" or "s" or "del" or
            "tt" or "code" or "big" or "small" or "sub" or "sup" or "br" or "span" or "font";

    private static bool TryReadComment(string source, int start, out int end)
    {
        end = start;
        if (!source.AsSpan(start).StartsWith("<!--", StringComparison.Ordinal))
        {
            return false;
        }

        int close = source.AsSpan(start + 4).IndexOf("-->", StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        end = start + 4 + close + 3;
        return true;
    }

    private static bool TryReadTag(string source, int start, out int end, out string tagText)
    {
        end = start + 1;
        tagText = string.Empty;
        char quote = '\0';
        while (end < source.Length)
        {
            if (end - start > TextMarkupConstants.MAX_TAG_LENGTH)
            {
                end = start + 1;
                return false;
            }

            char current = source[end++];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
            }
            else if (current == '<')
            {
                end = start + 1;
                return false;
            }
            else if (current == '>')
            {
                tagText = source.Substring(start, end - start);
                return true;
            }
        }

        end = start + 1;
        return false;
    }

    private static bool TryParseTagContent(ReadOnlySpan<char> content, out ParsedHtmlTag tag)
    {
        tag = default;
        content = content.Trim();
        if (content.Length == 0)
        {
            return false;
        }

        bool isClosing = content[0] == '/';
        if (isClosing)
        {
            content = content[1..].TrimStart();
        }

        bool isSelfClosing = !isClosing && content[^1] == '/';
        if (isSelfClosing)
        {
            content = content[..^1].TrimEnd();
        }

        int nameLength = 0;
        while (nameLength < content.Length && char.IsAsciiLetter(content[nameLength]))
        {
            nameLength++;
        }

        if (nameLength == 0)
        {
            return false;
        }

        string name = content[..nameLength].ToString().ToLowerInvariant();
        ReadOnlySpan<char> attributeText = content[nameLength..];
        if (isClosing)
        {
            if (!attributeText.Trim().IsEmpty)
            {
                return false;
            }

            tag = new ParsedHtmlTag(name, true, false, EmptyAttributes.Instance);
            return true;
        }

        if (!TryParseAttributes(attributeText, out var attributes))
        {
            return false;
        }

        tag = new ParsedHtmlTag(name, false, isSelfClosing, attributes);
        return true;
    }

    private static bool TryParseAttributes(
        ReadOnlySpan<char> source,
        out IReadOnlyDictionary<string, string?> attributes)
    {
        if (source.Trim().IsEmpty)
        {
            attributes = EmptyAttributes.Instance;
            return true;
        }

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        while (index < source.Length)
        {
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (index == source.Length)
            {
                break;
            }

            int nameStart = index;
            while (index < source.Length &&
                (char.IsAsciiLetterOrDigit(source[index]) || source[index] is '-' or '_'))
            {
                index++;
            }

            if (index == nameStart)
            {
                attributes = EmptyAttributes.Instance;
                return false;
            }

            string name = source[nameStart..index].ToString();
            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                attributes = EmptyAttributes.Instance;
                return false;
            }

            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            string? value = null;
            if (index < source.Length && source[index] == '=')
            {
                index++;
                while (index < source.Length && char.IsWhiteSpace(source[index]))
                {
                    index++;
                }

                if (index == source.Length)
                {
                    attributes = EmptyAttributes.Instance;
                    return false;
                }

                if (source[index] is '\'' or '"')
                {
                    char quote = source[index++];
                    int valueStart = index;
                    while (index < source.Length && source[index] != quote)
                    {
                        index++;
                    }

                    if (index == source.Length)
                    {
                        attributes = EmptyAttributes.Instance;
                        return false;
                    }

                    value = DecodeEntities(source[valueStart..index]);
                    index++;
                }
                else
                {
                    int valueStart = index;
                    while (index < source.Length && !char.IsWhiteSpace(source[index]))
                    {
                        index++;
                    }

                    value = DecodeEntities(source[valueStart..index]);
                }
            }

            if (IsStyleAttribute(name))
            {
                result[name] = value;
            }
        }

        attributes = result;
        return true;
    }

    private static bool IsStyleAttribute(string name)
        => name.Equals("font", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("size", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("weight", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("color", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("background", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("underline", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("strikethrough", StringComparison.OrdinalIgnoreCase);

    private static string DecodeEntities(ReadOnlySpan<char> source)
    {
        int ampersand = source.IndexOf('&');
        if (ampersand < 0)
        {
            return source.ToString();
        }

        string value = source.ToString();
        var builder = new StringBuilder(value.Length);
        int index = 0;
        while (index < value.Length)
        {
            if (value[index] == '&' && TryDecodeEntity(value, index, out int end, out string decoded))
            {
                builder.Append(decoded);
                index = end;
            }
            else
            {
                builder.Append(value[index++]);
            }
        }

        return builder.ToString();
    }

    private static bool TryDecodeEntity(string source, int start, out int end, out string decoded)
    {
        end = start + 1;
        decoded = string.Empty;
        int candidateLength = Math.Min(17, source.Length - start - 1);
        int semicolon = source.AsSpan(start + 1, candidateLength).IndexOf(';');
        if (semicolon < 0 || semicolon > 16)
        {
            return false;
        }

        semicolon += start + 1;
        ReadOnlySpan<char> entity = source.AsSpan(start + 1, semicolon - start - 1);
        decoded = entity switch
        {
            "lt" => "<",
            "gt" => ">",
            "amp" => "&",
            "quot" => "\"",
            "apos" => "'",
            _ => string.Empty
        };

        if (decoded.Length == 0 && !TryDecodeNumericEntity(entity, out decoded))
        {
            return false;
        }

        end = semicolon + 1;
        return true;
    }

    private static bool TryDecodeNumericEntity(ReadOnlySpan<char> entity, out string decoded)
    {
        decoded = string.Empty;
        if (entity.Length < 2 || entity[0] != '#')
        {
            return false;
        }

        bool hexadecimal = entity[1] is 'x' or 'X';
        ReadOnlySpan<char> digits = hexadecimal ? entity[2..] : entity[1..];
        NumberStyles style = hexadecimal ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
        if (!int.TryParse(digits, style, CultureInfo.InvariantCulture, out int scalar) || !Rune.IsValid(scalar))
        {
            return false;
        }

        decoded = char.ConvertFromUtf32(scalar);
        return true;
    }

    private static void Append(
        StringBuilder builder,
        List<MarkdownHtmlTextSpan> spans,
        ReadOnlySpan<char> text,
        in MarkdownHtmlStyle style)
    {
        if (text.Length == 0)
        {
            return;
        }

        int start = builder.Length;
        builder.Append(text);
        if (spans.Count > 0 && spans[^1].End == start && spans[^1].Style == style)
        {
            MarkdownHtmlTextSpan previous = spans[^1];
            spans[^1] = previous with { Length = previous.Length + text.Length };
        }
        else
        {
            spans.Add(new MarkdownHtmlTextSpan(start, text.Length, style));
        }
    }

    private readonly record struct ParsedHtmlTag(
        string Name,
        bool IsClosing,
        bool IsSelfClosing,
        IReadOnlyDictionary<string, string?> Attributes);

    private sealed class EmptyAttributes : IReadOnlyDictionary<string, string?>
    {
        public static EmptyAttributes Instance { get; } = new();
        public int Count => 0;
        public IEnumerable<string> Keys => [];
        public IEnumerable<string?> Values => [];
        public string? this[string key] => throw new KeyNotFoundException();
        public bool ContainsKey(string key) => false;
        public IEnumerator<KeyValuePair<string, string?>> GetEnumerator()
            => Enumerable.Empty<KeyValuePair<string, string?>>().GetEnumerator();
        public bool TryGetValue(string key, out string? value)
        {
            value = null;
            return false;
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

internal enum MarkdownHtmlTokenKind
{
    Open,
    Close,
    Break,
    Comment
}

internal readonly record struct MarkdownHtmlToken(MarkdownHtmlTokenKind Kind, MarkdownHtmlStyle Style);

internal sealed record MarkdownHtmlBlock(string Text, MarkdownHtmlTextSpan[] Spans)
{
    public static MarkdownHtmlBlock Empty { get; } = new(string.Empty, []);
}

internal readonly record struct MarkdownHtmlTextSpan(int Start, int Length, MarkdownHtmlStyle Style)
{
    public int End => checked(Start + Length);
}
