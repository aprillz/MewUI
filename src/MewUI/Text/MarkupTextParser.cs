using System.Globalization;
using System.Text;

namespace Aprillz.MewUI.Text;

internal static class MarkupTextParser
{
    private const int MAX_NESTING = 128;
    private const int MAX_TAG_LENGTH = 4096;
    private const double RELATIVE_SIZE_STEP = 1.2;

    // Synthetic scripts: a script's size relative to its parent, and its baseline shift as a fraction of the parent's size.
    private const double SCRIPT_SIZE_SCALE = 0.75;
    private const double SUPERSCRIPT_SHIFT = 0.35;
    private const double SUBSCRIPT_SHIFT = -0.20;

    public static MarkupTextDocument Parse(string source)
    {
        if (source.Length == 0)
        {
            return MarkupTextDocument.Empty;
        }

        var builder = new StringBuilder(source.Length);
        var spans = new List<MarkupTextSpan>();
        var stack = new List<MarkupTextFrame>();
        var style = MarkupTextStyle.Empty;
        int sourceIndex = 0;

        while (sourceIndex < source.Length)
        {
            if (source[sourceIndex] == '<' &&
                TryReadTag(source, sourceIndex, out int tagEnd, out var tag))
            {
                ReadOnlySpan<char> literalTag = source.AsSpan(sourceIndex, tagEnd - sourceIndex);
                if (TryApplyTag(tag, ref style, stack, builder, spans))
                {
                    sourceIndex = tagEnd;
                    continue;
                }

                Append(builder, spans, literalTag, style);
                sourceIndex = tagEnd;
                continue;
            }

            if (source[sourceIndex] == '&' &&
                TryDecodeEntity(source, sourceIndex, out int entityEnd, out string? decoded))
            {
                Append(builder, spans, decoded.AsSpan(), style);
                sourceIndex = entityEnd;
                continue;
            }

            int textEnd = sourceIndex + 1;
            while (textEnd < source.Length && source[textEnd] != '<' && source[textEnd] != '&')
            {
                textEnd++;
            }

            Append(builder, spans, source.AsSpan(sourceIndex, textEnd - sourceIndex), style);
            sourceIndex = textEnd;
        }

        return new MarkupTextDocument(builder.ToString(), spans.ToArray());
    }

    private static bool TryApplyTag(
        in ParsedMarkupTag tag,
        ref MarkupTextStyle style,
        List<MarkupTextFrame> stack,
        StringBuilder builder,
        List<MarkupTextSpan> spans)
    {
        if (!IsSupportedTag(tag.Name))
        {
            return false;
        }

        if (tag.Name == "br")
        {
            if (tag.IsClosing)
            {
                return false;
            }

            Append(builder, spans, "\n".AsSpan(), style);
            return true;
        }

        if (tag.IsClosing)
        {
            for (int stackIndex = stack.Count - 1; stackIndex >= 0; stackIndex--)
            {
                if (stack[stackIndex].Name == tag.Name)
                {
                    style = stack[stackIndex].PreviousStyle;
                    stack.RemoveRange(stackIndex, stack.Count - stackIndex);
                    return true;
                }
            }

            return false;
        }

        if (stack.Count >= MAX_NESTING)
        {
            return false;
        }

        var previousStyle = style;
        if (!TryApplyOpeningTag(tag, ref style))
        {
            style = previousStyle;
            return false;
        }

        if (!tag.IsSelfClosing)
        {
            stack.Add(new MarkupTextFrame(tag.Name, previousStyle));
        }
        else
        {
            style = previousStyle;
        }

        return true;
    }

    /// <summary>Applies an opening tag to the style; false when the tag would leave no usable style and stays literal.</summary>
    private static bool TryApplyOpeningTag(in ParsedMarkupTag tag, ref MarkupTextStyle style)
    {
        switch (tag.Name)
        {
            case "sup":
                return TryApplyScript(ref style, SUPERSCRIPT_SHIFT);
            case "sub":
                return TryApplyScript(ref style, SUBSCRIPT_SHIFT);
            case "b":
            case "strong":
                style = style with { FontWeight = MewUI.FontWeight.Bold };
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
                style = ScaleFontSize(style, RELATIVE_SIZE_STEP);
                break;
            case "small":
                style = ScaleFontSize(style, 1.0 / RELATIVE_SIZE_STEP);
                break;
            case "span":
            case "font":
                ApplyAttributes(tag.Attributes, ref style);
                break;
        }

        return true;
    }

    private static void ApplyAttributes(IReadOnlyDictionary<string, string?> attributes, ref MarkupTextStyle style)
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

        if (attributes.TryGetValue("color", out string? foreground) && MarkupColorParser.TryParse(foreground, out var foregroundColor))
        {
            style = style with { Foreground = foregroundColor };
        }

        if (attributes.TryGetValue("background", out string? background) && MarkupColorParser.TryParse(background, out var backgroundColor))
        {
            style = style with { Background = backgroundColor };
        }

        if (attributes.TryGetValue("underline", out string? underline) && TryParseBoolean(underline, out bool isUnderlined))
        {
            var decoration = isUnderlined
                ? style.Decoration | TextDecoration.Underline
                : style.Decoration & ~TextDecoration.Underline;
            style = style with { Decoration = decoration };
        }

        if (attributes.TryGetValue("strikethrough", out string? strikethrough) && TryParseBoolean(strikethrough, out bool isStruckThrough))
        {
            var decoration = isStruckThrough
                ? style.Decoration | TextDecoration.Strikethrough
                : style.Decoration & ~TextDecoration.Strikethrough;
            style = style with { Decoration = decoration };
        }
    }

    private static MarkupTextStyle ScaleFontSize(in MarkupTextStyle style, double scale)
    {
        if (style.FontSize is double absoluteSize)
        {
            return style with { FontSize = absoluteSize * scale };
        }

        return style with { FontSizeScale = style.FontSizeScale * scale };
    }

    /// <summary>
    /// Shrinks the size for a script and shifts its baseline by a fraction of the size before the
    /// shrink, so nested scripts accumulate their shifts; false when the result is not a usable style.
    /// </summary>
    private static bool TryApplyScript(ref MarkupTextStyle style, double shift)
    {
        MarkupTextStyle scripted;
        if (style.FontSize is double absoluteSize)
        {
            scripted = style with
            {
                FontSize = absoluteSize * SCRIPT_SIZE_SCALE,
                BaselineOffset = style.BaselineOffset + shift * absoluteSize
            };
        }
        else
        {
            scripted = style with
            {
                FontSizeScale = style.FontSizeScale * SCRIPT_SIZE_SCALE,
                BaselineOffsetScale = style.BaselineOffsetScale + shift * style.FontSizeScale
            };
        }

        bool usableSize = scripted.FontSize is double size
            ? double.IsFinite(size) && size > 0
            : double.IsFinite(scripted.FontSizeScale) && scripted.FontSizeScale > 0;
        if (!usableSize ||
            !TextLayoutRequestSnapshot.IsValidBaselineOffset(scripted.BaselineOffset) ||
            !TextLayoutRequestSnapshot.IsValidBaselineOffset(scripted.BaselineOffsetScale))
        {
            return false;
        }

        style = scripted;
        return true;
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

    private static void Append(
        StringBuilder builder,
        List<MarkupTextSpan> spans,
        ReadOnlySpan<char> text,
        in MarkupTextStyle style)
    {
        if (text.Length == 0)
        {
            return;
        }

        int start = builder.Length;
        builder.Append(text);
        if (spans.Count > 0 && spans[^1].End == start && spans[^1].Style == style)
        {
            var previous = spans[^1];
            spans[^1] = previous with { Length = previous.Length + text.Length };
        }
        else
        {
            spans.Add(new MarkupTextSpan(start, text.Length, style));
        }
    }

    private static bool TryReadTag(string source, int start, out int end, out ParsedMarkupTag tag)
    {
        end = start + 1;
        tag = default;
        char quote = '\0';
        while (end < source.Length)
        {
            if (end - start > MAX_TAG_LENGTH)
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
                return TryParseTagContent(source.AsSpan(start + 1, end - start - 2), out tag);
            }
        }

        end = start + 1;
        return false;
    }

    private static bool TryParseTagContent(ReadOnlySpan<char> content, out ParsedMarkupTag tag)
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

            tag = new ParsedMarkupTag(name, true, false, EmptyAttributes.Instance);
            return true;
        }

        if (!TryParseAttributes(attributeText, out var attributes))
        {
            return false;
        }

        tag = new ParsedMarkupTag(name, false, isSelfClosing, attributes);
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

            result[name] = value;
        }

        attributes = result;
        return true;
    }

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
            if (value[index] == '&' && TryDecodeEntity(value, index, out int end, out string? decoded))
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

    private readonly record struct ParsedMarkupTag(
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

internal sealed record MarkupTextDocument(string Text, MarkupTextSpan[] Spans)
{
    public static MarkupTextDocument Empty { get; } = new(string.Empty, []);

    public void AppendGeometryRuns(
        in TextRunStyle defaultStyle,
        TextMarkupOptions options,
        IList<GeometryStyleRun> output)
    {
        foreach (var span in Spans)
        {
            var markupStyle = span.Style;
            double referenceSize = defaultStyle.FontSize;
            double offset = defaultStyle.BaselineOffset + markupStyle.BaselineOffset +
                markupStyle.BaselineOffsetScale * referenceSize;
            var style = defaultStyle with
            {
                FontFamily = markupStyle.FontFamily ??
                    (markupStyle.UsesMonospaceFont ? options.MonospaceFontFamily : defaultStyle.FontFamily),
                FontSize = markupStyle.FontSize ?? referenceSize * markupStyle.FontSizeScale,
                Weight = markupStyle.FontWeight ?? defaultStyle.Weight,
                Italic = markupStyle.Italic || defaultStyle.Italic,
                Decoration = markupStyle.Decoration | defaultStyle.Decoration,
                // An owner size too large for the resolved shift keeps the owner's offset instead of failing the layout.
                BaselineOffset = TextLayoutRequestSnapshot.IsValidBaselineOffset(offset) ? offset : defaultStyle.BaselineOffset
            };

            if (style != defaultStyle)
            {
                output.Add(new GeometryStyleRun(span.Start, span.Length, style));
            }
        }
    }

    public void AppendPaintSpans(IList<TextPaintSpan> output)
    {
        foreach (var span in Spans)
        {
            if (span.Style.Foreground is not null || span.Style.Background is not null)
            {
                output.Add(new TextPaintSpan(
                    new TextRange(span.Start, span.Length),
                    span.Style.Foreground,
                    span.Style.Background));
            }
        }
    }
}

internal readonly record struct MarkupTextSpan(int Start, int Length, MarkupTextStyle Style)
{
    public int End => checked(Start + Length);
}

internal readonly record struct MarkupTextFrame(string Name, MarkupTextStyle PreviousStyle);

/// <summary>
/// Style of a markup span. A size is absolute DIPs or a scale of the owner's size; a baseline shift keeps
/// both a DIP part and a part scaled by the owner's size, so scripts under a mix of sizes still resolve.
/// </summary>
internal readonly record struct MarkupTextStyle(
    string? FontFamily,
    bool UsesMonospaceFont,
    double? FontSize,
    double FontSizeScale,
    FontWeight? FontWeight,
    bool Italic,
    TextDecoration Decoration,
    Color? Foreground,
    Color? Background,
    double BaselineOffset,
    double BaselineOffsetScale)
{
    public static MarkupTextStyle Empty { get; } =
        new(null, false, null, 1.0, null, false, TextDecoration.None, null, null, 0, 0);
}
