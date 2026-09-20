using System.Globalization;
using System.Numerics;

namespace Aprillz.MewUI.Resources;

/// <summary>Parses the attribute value forms the accepted SVG subset uses.</summary>
internal static class SimpleSvgValueParser
{
    /// <summary>Reads a number, ignoring a trailing unit. Returns false for an empty or non-numeric value.</summary>
    public static bool TryNumber(ReadOnlySpan<char> text, out double value)
    {
        text = text.Trim();
        int length = 0;
        if (length < text.Length && (text[length] == '-' || text[length] == '+'))
        {
            length++;
        }

        while (length < text.Length && char.IsAsciiDigit(text[length]))
        {
            length++;
        }

        if (length < text.Length && text[length] == '.')
        {
            length++;
            while (length < text.Length && char.IsAsciiDigit(text[length]))
            {
                length++;
            }
        }

        if (length < text.Length && (text[length] == 'e' || text[length] == 'E'))
        {
            int exponent = length + 1;
            if (exponent < text.Length && (text[exponent] == '-' || text[exponent] == '+'))
            {
                exponent++;
            }

            if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
            {
                length = exponent;
                while (length < text.Length && char.IsAsciiDigit(text[length]))
                {
                    length++;
                }
            }
        }

        return double.TryParse(text[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Reads a length. Only the unitless and <c>px</c> forms carry a usable value.</summary>
    public static bool TryLength(ReadOnlySpan<char> text, out double value)
    {
        text = text.Trim();
        if (text.EndsWith("%", StringComparison.Ordinal))
        {
            value = 0;
            return false;
        }

        return TryNumber(text, out value);
    }

    /// <summary>Reads a value in the range 0 to 1, clamping what falls outside.</summary>
    public static double Unit(ReadOnlySpan<char> text, double fallback)
    {
        text = text.Trim();
        if (text.EndsWith("%", StringComparison.Ordinal))
        {
            return TryNumber(text[..^1], out double percent) ? Math.Clamp(percent / 100.0, 0, 1) : fallback;
        }

        return TryNumber(text, out double value) ? Math.Clamp(value, 0, 1) : fallback;
    }

    /// <summary>Reads a whitespace or comma separated number list.</summary>
    public static double[] Numbers(ReadOnlySpan<char> text)
    {
        var values = new List<double>();
        int position = 0;
        while (position < text.Length)
        {
            while (position < text.Length && (char.IsWhiteSpace(text[position]) || text[position] == ','))
            {
                position++;
            }

            int start = position;
            if (position < text.Length && (text[position] == '-' || text[position] == '+'))
            {
                position++;
            }

            while (position < text.Length &&
                   (char.IsAsciiDigit(text[position]) || text[position] == '.' ||
                    text[position] == 'e' || text[position] == 'E' ||
                    ((text[position] == '-' || text[position] == '+') &&
                     (text[position - 1] == 'e' || text[position - 1] == 'E'))))
            {
                position++;
            }

            if (position == start)
            {
                position++;
                continue;
            }

            if (double.TryParse(text[start..position], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                values.Add(value);
            }
        }

        return [.. values];
    }

    /// <summary>Reads a color. Returns false for <c>none</c>, a reference, or anything unrecognised.</summary>
    public static bool TryColor(ReadOnlySpan<char> text, out Color color)
    {
        color = default;
        text = text.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (text[0] == '#')
        {
            var digits = text[1..];
            if (digits.Length == 3)
            {
                Span<char> expanded = stackalloc char[6];
                for (int i = 0; i < 3; i++)
                {
                    expanded[i * 2] = digits[i];
                    expanded[i * 2 + 1] = digits[i];
                }
                return TryHex(expanded, out color);
            }

            return TryHex(digits, out color);
        }

        if (Color.NamedColors.TryGetValue(text.ToString(), out var named))
        {
            color = named;
            return true;
        }

        return false;
    }

    private static bool TryHex(ReadOnlySpan<char> digits, out Color color)
    {
        color = default;
        if (digits.Length != 6 && digits.Length != 8)
        {
            return false;
        }

        foreach (char c in digits)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        // Color.FromHex reads #RRGGBBAA, which is the order SVG uses too.
        color = Color.FromHex(digits);
        return true;
    }

    /// <summary>Reads the id out of a <c>url(#id)</c> reference, or null when the value is not one.</summary>
    public static string? Reference(ReadOnlySpan<char> text)
    {
        text = text.Trim();
        if (!text.StartsWith("url(", StringComparison.OrdinalIgnoreCase) || !text.EndsWith(")", StringComparison.Ordinal))
        {
            return null;
        }

        var inner = text[4..^1].Trim().Trim('"').Trim('\'').Trim();
        if (inner.Length < 2 || inner[0] != '#')
        {
            return null;
        }

        return inner[1..].ToString();
    }

    /// <summary>Reads a transform list, composing the functions left to right.</summary>
    public static Matrix3x2 Transform(ReadOnlySpan<char> text)
    {
        var result = Matrix3x2.Identity;
        int position = 0;

        while (position < text.Length)
        {
            while (position < text.Length && (char.IsWhiteSpace(text[position]) || text[position] == ','))
            {
                position++;
            }

            int nameStart = position;
            while (position < text.Length && char.IsAsciiLetter(text[position]))
            {
                position++;
            }

            if (position == nameStart)
            {
                break;
            }

            var name = text[nameStart..position];
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            if (position >= text.Length || text[position] != '(')
            {
                break;
            }

            int close = text[position..].IndexOf(')');
            if (close < 0)
            {
                break;
            }

            var arguments = Numbers(text.Slice(position + 1, close - 1));
            position += close + 1;

            result = Apply(name, arguments) * result;
        }

        return result;
    }

    private static Matrix3x2 Apply(ReadOnlySpan<char> name, double[] arguments)
    {
        if (name.Equals("matrix", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 6)
        {
            return new Matrix3x2(
                (float)arguments[0], (float)arguments[1],
                (float)arguments[2], (float)arguments[3],
                (float)arguments[4], (float)arguments[5]);
        }

        if (name.Equals("translate", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 1)
        {
            return Matrix3x2.CreateTranslation(
                (float)arguments[0],
                (float)(arguments.Length >= 2 ? arguments[1] : 0));
        }

        if (name.Equals("scale", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 1)
        {
            return Matrix3x2.CreateScale(
                (float)arguments[0],
                (float)(arguments.Length >= 2 ? arguments[1] : arguments[0]));
        }

        if (name.Equals("rotate", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 1)
        {
            float radians = (float)(arguments[0] * Math.PI / 180.0);
            if (arguments.Length >= 3)
            {
                return Matrix3x2.CreateRotation(
                    radians,
                    new Vector2((float)arguments[1], (float)arguments[2]));
            }

            return Matrix3x2.CreateRotation(radians);
        }

        if (name.Equals("skewX", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 1)
        {
            return Matrix3x2.CreateSkew((float)(arguments[0] * Math.PI / 180.0), 0);
        }

        if (name.Equals("skewY", StringComparison.OrdinalIgnoreCase) && arguments.Length >= 1)
        {
            return Matrix3x2.CreateSkew(0, (float)(arguments[0] * Math.PI / 180.0));
        }

        return Matrix3x2.Identity;
    }

    /// <summary>Reads one declaration out of an inline <c>style</c> value, in source order.</summary>
    public static IEnumerable<(string Name, string Value)> Declarations(string style)
    {
        foreach (var part in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            yield return (part[..colon].Trim().ToLowerInvariant(), part[(colon + 1)..].Trim());
        }
    }
}
