using System.Globalization;

namespace Aprillz.MewUI.Text;

internal static class MarkupColorParser
{
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> value = text.AsSpan().Trim();
        if (value[0] == '#')
        {
            return TryParseHex(value[1..], out color);
        }

        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && value[^1] == ')')
        {
            return TryParseRgb(value[4..^1], out color);
        }

        Color? namedColor = value.ToString().ToLowerInvariant() switch
        {
            "black" => Color.FromRgb(0x00, 0x00, 0x00),
            "silver" => Color.FromRgb(0xC0, 0xC0, 0xC0),
            "gray" => Color.FromRgb(0x80, 0x80, 0x80),
            "white" => Color.FromRgb(0xFF, 0xFF, 0xFF),
            "maroon" => Color.FromRgb(0x80, 0x00, 0x00),
            "red" => Color.FromRgb(0xFF, 0x00, 0x00),
            "purple" => Color.FromRgb(0x80, 0x00, 0x80),
            "fuchsia" => Color.FromRgb(0xFF, 0x00, 0xFF),
            "green" => Color.FromRgb(0x00, 0x80, 0x00),
            "lime" => Color.FromRgb(0x00, 0xFF, 0x00),
            "olive" => Color.FromRgb(0x80, 0x80, 0x00),
            "yellow" => Color.FromRgb(0xFF, 0xFF, 0x00),
            "navy" => Color.FromRgb(0x00, 0x00, 0x80),
            "blue" => Color.FromRgb(0x00, 0x00, 0xFF),
            "teal" => Color.FromRgb(0x00, 0x80, 0x80),
            "aqua" => Color.FromRgb(0x00, 0xFF, 0xFF),
            _ => null
        };
        if (namedColor is Color resolvedColor)
        {
            color = resolvedColor;
            return true;
        }

        return false;
    }

    private static bool TryParseHex(ReadOnlySpan<char> value, out Color color)
    {
        color = default;
        if (value.Length == 6 &&
            uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
        {
            color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }

        if (value.Length == 8 &&
            uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint argb))
        {
            color = Color.FromArgb(argb);
            return true;
        }

        return false;
    }

    private static bool TryParseRgb(ReadOnlySpan<char> value, out Color color)
    {
        color = default;
        Span<byte> components = stackalloc byte[3];
        for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
        {
            int comma = value.IndexOf(',');
            ReadOnlySpan<char> component;
            if (componentIndex < components.Length - 1)
            {
                if (comma < 0)
                {
                    return false;
                }
                component = value[..comma].Trim();
                value = value[(comma + 1)..];
            }
            else
            {
                if (comma >= 0)
                {
                    return false;
                }
                component = value.Trim();
            }

            if (!byte.TryParse(
                component,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out components[componentIndex]))
            {
                return false;
            }
        }

        color = Color.FromRgb(components[0], components[1], components[2]);
        return true;
    }
}
