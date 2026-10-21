// Ported from Windows Terminal src/types/utils.cpp and src/types/colorTable.cpp (MIT): the parsing helpers the VT
// engine uses.

using Aprillz.MewUI.Terminal.Buffer;

namespace Aprillz.MewUI.Terminal.Types;

internal static partial class VTUtils
{
    /// <summary>
    /// The first character a run of printable text stops at: C0 controls, DEL and C1 controls.
    /// </summary>
    public static int FindActionableControlCharacter(ReadOnlySpan<char> text)
    {
        int c0 = text.IndexOfAnyInRange('\u0000', '\u001F');
        int searchLength = c0 < 0 ? text.Length : c0;
        int c1 = text[..searchLength].IndexOfAnyInRange('\u007F', '\u009F');
        if (c1 >= 0)
        {
            return c1;
        }

        return c0 < 0 ? text.Length : c0;
    }

    /// <summary>Parses an xterm color: <c>rgb:r/g/b</c>, <c>#rgb</c> forms, or an X11 color name.</summary>
    public static uint? ColorFromXTermColor(ReadOnlySpan<char> text) => ColorFromXParseColorSpec(text) ?? ColorFromXOrgAppColorName(text);

    /// <summary>
    /// Parses the XParseColor forms: <c>rgb:h/h/h</c> with 1 to 4 hex digits per component (scaled), or <c>#</c> with
    /// 3, 6, 9 or 12 hex digits (the high digits taken).
    /// </summary>
    public static uint? ColorFromXParseColorSpec(ReadOnlySpan<char> text)
    {
        bool foundSpec = false;
        bool isSharpSignFormat = false;
        int rgbHexDigitCount = 0;
        Span<uint> colorValues = stackalloc uint[3];
        Span<uint> parameterValues = stackalloc uint[3];
        int stringSize = text.Length;
        int current = 0;

        if (stringSize > 4 && text[..4].Equals("rgb:", StringComparison.OrdinalIgnoreCase))
        {
            // From "rgb:h/h/h" (9) to "rgb:hhhh/hhhh/hhhh" (18).
            if (stringSize < 9 || stringSize > 18)
            {
                return null;
            }

            foundSpec = true;
            current = 4;
        }

        if (!foundSpec && stringSize > 1 && text[0] == '#')
        {
            if (!(stringSize == 4 || stringSize == 7 || stringSize == 10 || stringSize == 13))
            {
                return null;
            }

            isSharpSignFormat = true;
            foundSpec = true;
            rgbHexDigitCount = (stringSize - 1) / 3;
            current = 1;
        }

        if (!foundSpec)
        {
            return null;
        }

        for (int component = 0; component < 3; component++)
        {
            bool foundColor = false;
            int iteration = isSharpSignFormat ? rgbHexDigitCount : 4;
            for (int index = 0; index < iteration && current < stringSize; index++)
            {
                char character = text[current++];
                parameterValues[component] *= 16;
                if (!TryHexToUint(character, out uint digit))
                {
                    return null;
                }

                parameterValues[component] += digit;

                if (isSharpSignFormat)
                {
                    foundColor = true;
                    if (index >= rgbHexDigitCount)
                    {
                        break;
                    }
                }
                else
                {
                    rgbHexDigitCount = index + 1;
                    if (component < 2 && current < stringSize && text[current] == '/')
                    {
                        current++;
                        foundColor = true;
                        break;
                    }

                    if (current >= stringSize)
                    {
                        foundColor = true;
                        break;
                    }
                }
            }

            if (!foundColor)
            {
                return null;
            }

            int scaleMultiplier = isSharpSignFormat ? 0x10 : 0x11;
            int scaleDivisor = scaleMultiplier << 8 >> 4 * (4 - rgbHexDigitCount);
            colorValues[component] = (uint)(parameterValues[component] * scaleMultiplier / scaleDivisor);
        }

        if (current < stringSize)
        {
            return null;
        }

        return TextColor.Rgb((byte)colorValues[0], (byte)colorValues[1], (byte)colorValues[2]);
    }

    /// <summary>Looks up an X11 color name such as <c>yellow3</c> or <c>gray50</c>, ignoring case and spaces.</summary>
    public static uint? ColorFromXOrgAppColorName(ReadOnlySpan<char> text)
    {
        Span<char> stemBuffer = stackalloc char[Math.Min(text.Length, 64)];
        int stemLength = 0;
        int variantIndex = 0;
        bool foundVariant = false;
        foreach (char character in text)
        {
            if (character > 127)
            {
                return null;
            }

            if (character is >= '0' and <= '9')
            {
                foundVariant = true;
                variantIndex = variantIndex * 10 + (character - '0');
                if (variantIndex > 1000)
                {
                    return null;
                }

                continue;
            }

            if (character is ' ' or '\f' or '\n' or '\r' or '\t' or '\v')
            {
                continue;
            }

            // A digit run ends the name, so "yellow3a" is not a name.
            if (foundVariant || stemLength == stemBuffer.Length)
            {
                return null;
            }

            stemBuffer[stemLength++] = char.ToLowerInvariant(character);
        }

        string stem = new(stemBuffer[..stemLength]);
        if (_xorgAppVariantColorTable.TryGetValue(stem, out uint[]? variants) && variantIndex < variants.Length)
        {
            return variants[variantIndex];
        }

        if ((stem == "gray" || stem == "grey") && foundVariant)
        {
            if (variantIndex > 100)
            {
                return null;
            }

            byte component = (byte)Math.Min(255, (variantIndex * 255 + 50) / 100);
            return TextColor.Rgb(component, component, component);
        }

        if (_xorgAppColorTable.TryGetValue(stem, out uint color))
        {
            return color;
        }

        return null;
    }

    public static bool TryHexToUint(char character, out uint value)
    {
        if (character is >= '0' and <= '9')
        {
            value = (uint)(character - '0');
            return true;
        }

        if (character is >= 'a' and <= 'f')
        {
            value = (uint)(character - 'a' + 10);
            return true;
        }

        if (character is >= 'A' and <= 'F')
        {
            value = (uint)(character - 'A' + 10);
            return true;
        }

        value = 0;
        return false;
    }

    /// <summary>Parses decimal digits only; false for an empty string or any other character.</summary>
    public static bool TryStringToUint(ReadOnlySpan<char> text, out uint value)
    {
        value = 0;
        if (text.IsEmpty)
        {
            return false;
        }

        uint result = 0;
        foreach (char character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }

            result = result * 10 + (uint)(character - '0');
        }

        value = result;
        return true;
    }

    /// <summary>Splits on a delimiter; a trailing delimiter yields a final empty part.</summary>
    public static List<string> SplitString(ReadOnlySpan<char> text, char delimiter)
    {
        var result = new List<string>();
        int current = 0;
        while (current < text.Length)
        {
            int next = text[current..].IndexOf(delimiter);
            if (next < 0)
            {
                result.Add(new string(text[current..]));
                break;
            }

            result.Add(new string(text.Slice(current, next)));
            current += next + 1;
            if (current >= text.Length)
            {
                result.Add(string.Empty);
            }
        }

        return result;
    }

    /// <summary>A color from red, green and blue percentages (0 to 100), as DECRSTS gives them.</summary>
    public static uint ColorFromRgb100(int red, int green, int blue)
    {
        static byte Scale(int value) => (byte)((Math.Min((uint)value, 100u) * 255 + 50) / 100);

        return TextColor.Rgb(Scale(red), Scale(green), Scale(blue));
    }

    /// <summary>The red, green and blue percentages (0 to 100) of a color.</summary>
    public static (int Red, int Green, int Blue) ColorToRgb100(uint color)
    {
        static int Scale(uint value) => (int)((value * 100 + 128) / 255);

        return (Scale(color & 0xFF), Scale((color >> 8) & 0xFF), Scale((color >> 16) & 0xFF));
    }

    /// <summary>A color from DEC hue (blue at 0, red at 120, green at 240), lightness and saturation percentages.</summary>
    public static uint ColorFromHls(int hue, int lightness, int saturation)
    {
        int hueAngle = hue % 360;
        float lum = Math.Min(lightness, 100);
        float sat = Math.Min(saturation, 100);

        // Chroma, the second largest component, and the offset that lightness adds to all three.
        float chroma = (50f - Math.Abs(lum - 50f)) * sat / 50f;
        float second = chroma * (60 - Math.Abs(hueAngle % 120 - 60)) / 60f;
        float offset = lum - chroma / 2f;

        const float SCALE = 255f / 100f;
        byte comp1 = (byte)((chroma + offset) * SCALE + 0.5f);
        byte comp2 = (byte)((second + offset) * SCALE + 0.5f);
        byte comp3 = (byte)((0 + offset) * SCALE + 0.5f);

        if (hueAngle < 60)
        {
            return TextColor.Rgb(comp2, comp3, comp1);
        }
        else if (hueAngle < 120)
        {
            return TextColor.Rgb(comp1, comp3, comp2);
        }
        else if (hueAngle < 180)
        {
            return TextColor.Rgb(comp1, comp2, comp3);
        }
        else if (hueAngle < 240)
        {
            return TextColor.Rgb(comp2, comp1, comp3);
        }
        else if (hueAngle < 300)
        {
            return TextColor.Rgb(comp3, comp1, comp2);
        }
        else
        {
            return TextColor.Rgb(comp3, comp2, comp1);
        }
    }

    /// <summary>The DEC hue (blue at 0, red at 120, green at 240), lightness and saturation percentages of a color.</summary>
    public static (int Hue, int Lightness, int Saturation) ColorToHls(uint color)
    {
        float red = (color & 0xFF) / 255f;
        float green = ((color >> 8) & 0xFF) / 255f;
        float blue = ((color >> 16) & 0xFF) / 255f;

        float maxComp = Math.Max(red, Math.Max(green, blue));
        float minComp = Math.Min(red, Math.Min(green, blue));
        float chroma = maxComp - minComp;

        // Twice the luma, which saves a division.
        float luma2 = maxComp + minComp;
        float divisor = Math.Min(luma2, 2f - luma2);
        float sat = divisor > 0 ? chroma / divisor : 0f;

        float hue = 0f;
        if (chroma != 0)
        {
            if (maxComp == red)
            {
                hue = (green - blue) / chroma + 2f;
            }
            else if (maxComp == green)
            {
                hue = (blue - red) / chroma + 4f;
            }
            else
            {
                hue = (red - green) / chroma + 6f;
            }
        }

        return ((int)(hue * 60f + 0.5f) % 360, (int)(luma2 * 50f + 0.5f), (int)(sat * 100f + 0.5f));
    }
}
