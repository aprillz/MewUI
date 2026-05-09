using System.Globalization;

namespace Aprillz.MewUI.Svg.Internal;

internal static class SvgColorParser
{
    /// <summary>
    /// Parses an SVG paint value: "none", "currentColor", "#rgb", "#rrggbb",
    /// "rgb(...)", "rgba(...)", named color, or "url(#id)".
    /// Returns null if the value should be treated as "inherit".
    /// </summary>
    public static SvgPaint? Parse(ReadOnlySpan<char> value)
    {
        value = value.Trim();
        if (value.IsEmpty) return null;

        if (value.Equals("inherit", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return SvgPaint.None;
        if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) return SvgPaint.CurrentColor;

        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var inner = value[4..];
            if (inner.Length > 0 && inner[^1] == ')') inner = inner[..^1];
            inner = inner.Trim();
            if (inner.Length > 0 && inner[0] == '#') inner = inner[1..];
            return SvgPaint.FromUrl(new string(inner));
        }

        if (TryParseColor(value, out var color))
            return SvgPaint.FromColor(color);

        return null;
    }

    public static bool TryParseColor(ReadOnlySpan<char> value, out Color color)
    {
        value = value.Trim();

        if (value.Length > 0 && value[0] == '#')
        {
            color = ParseHex(value[1..]);
            return true;
        }

        if (value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
        {
            color = ParseRgba(value[5..^1]);
            return true;
        }

        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
        {
            color = ParseRgb(value[4..^1]);
            return true;
        }

        return TryParseNamed(value, out color);
    }

    private static Color ParseHex(ReadOnlySpan<char> hex)
    {
        if (hex.Length == 3)
        {
            byte r = HexNibble(hex[0]);
            byte g = HexNibble(hex[1]);
            byte b = HexNibble(hex[2]);
            return new Color((byte)(r | (r << 4)), (byte)(g | (g << 4)), (byte)(b | (b << 4)));
        }
        if (hex.Length == 4)
        {
            byte a = HexNibble(hex[0]);
            byte r = HexNibble(hex[1]);
            byte g = HexNibble(hex[2]);
            byte b = HexNibble(hex[3]);
            return new Color((byte)(a | (a << 4)), (byte)(r | (r << 4)), (byte)(g | (g << 4)), (byte)(b | (b << 4)));
        }
        if (hex.Length == 6)
        {
            byte r = (byte)((HexNibble(hex[0]) << 4) | HexNibble(hex[1]));
            byte g = (byte)((HexNibble(hex[2]) << 4) | HexNibble(hex[3]));
            byte b = (byte)((HexNibble(hex[4]) << 4) | HexNibble(hex[5]));
            return new Color(r, g, b);
        }
        if (hex.Length == 8)
        {
            byte r = (byte)((HexNibble(hex[0]) << 4) | HexNibble(hex[1]));
            byte g = (byte)((HexNibble(hex[2]) << 4) | HexNibble(hex[3]));
            byte b = (byte)((HexNibble(hex[4]) << 4) | HexNibble(hex[5]));
            byte a = (byte)((HexNibble(hex[6]) << 4) | HexNibble(hex[7]));
            return new Color(a, r, g, b);
        }
        return new Color(0, 0, 0);
    }

    private static Color ParseRgb(ReadOnlySpan<char> inner)
    {
        Span<double> parts = stackalloc double[3];
        SplitNumbers(inner, parts);
        return new Color(ClampByte(parts[0]), ClampByte(parts[1]), ClampByte(parts[2]));
    }

    private static Color ParseRgba(ReadOnlySpan<char> inner)
    {
        Span<double> parts = stackalloc double[4];
        SplitNumbers(inner, parts);
        byte a = (byte)Math.Clamp((int)(parts[3] * 255), 0, 255);
        return new Color(a, ClampByte(parts[0]), ClampByte(parts[1]), ClampByte(parts[2]));
    }

    private static void SplitNumbers(ReadOnlySpan<char> s, Span<double> dest)
    {
        int idx = 0;
        int start = 0;
        for (int i = 0; i <= s.Length && idx < dest.Length; i++)
        {
            char c = (i < s.Length) ? s[i] : ',';
            if (c == ',' || c == '/')
            {
                var token = s[start..i].Trim();
                double val = 0;
                if (token.Length > 0 && token[^1] == '%')
                    double.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out val);
                else
                    double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
                dest[idx++] = val;
                start = i + 1;
            }
        }
    }

    private static byte ClampByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    private static byte HexNibble(char c) => c switch
    {
        >= '0' and <= '9' => (byte)(c - '0'),
        >= 'a' and <= 'f' => (byte)(c - 'a' + 10),
        >= 'A' and <= 'F' => (byte)(c - 'A' + 10),
        _ => 0
    };

    private static bool TryParseNamed(ReadOnlySpan<char> name, out Color color)
    {
        // Use MewUI's built-in named color dictionary
        if (Color.NamedColors.TryGetValue(new string(name), out color))
            return true;

        color = default;
        return false;
    }
}
