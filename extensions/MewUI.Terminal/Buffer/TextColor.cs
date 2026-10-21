// Ported from Windows Terminal src/buffer/out/TextColor.h and TextColor.cpp (MIT).

namespace Aprillz.MewUI.Terminal.Buffer;

internal enum ColorType : byte
{
    IsDefault,
    IsIndex16,
    IsIndex256,
    IsRgb,
}

/// <summary>
/// One color of a cell: the terminal's default, an index into the 16 or 256 color table, or an RGB value.
/// </summary>
/// <remarks>RGB values use the 0x00BBGGRR layout throughout the terminal, made by <see cref="Rgb"/>.</remarks>
internal struct TextColor : IEquatable<TextColor>
{
    public const byte DARK_BLACK = 0;
    public const byte DARK_RED = 1;
    public const byte DARK_GREEN = 2;
    public const byte DARK_YELLOW = 3;
    public const byte DARK_BLUE = 4;
    public const byte DARK_MAGENTA = 5;
    public const byte DARK_CYAN = 6;
    public const byte DARK_WHITE = 7;
    public const byte BRIGHT_BLACK = 8;
    public const byte BRIGHT_RED = 9;
    public const byte BRIGHT_GREEN = 10;
    public const byte BRIGHT_YELLOW = 11;
    public const byte BRIGHT_BLUE = 12;
    public const byte BRIGHT_MAGENTA = 13;
    public const byte BRIGHT_CYAN = 14;
    public const byte BRIGHT_WHITE = 15;

    public const int DEFAULT_FOREGROUND = 261;
    public const int DEFAULT_BACKGROUND = 262;
    public const int FRAME_FOREGROUND = 263;
    public const int FRAME_BACKGROUND = 264;
    public const int CURSOR_COLOR = 265;
    public const int SELECTION_BACKGROUND = 266;
    public const int TABLE_SIZE = 267;

    // The red channel doubles as the table index for indexed colors.
    private byte _red;
    private byte _green;
    private byte _blue;
    private ColorType _meta;

    public TextColor(byte index, bool isIndex256)
    {
        _meta = isIndex256 ? ColorType.IsIndex256 : ColorType.IsIndex16;
        _red = index;
        _green = 0;
        _blue = 0;
    }

    public TextColor(uint rgb)
    {
        _meta = ColorType.IsRgb;
        _red = (byte)rgb;
        _green = (byte)(rgb >> 8);
        _blue = (byte)(rgb >> 16);
    }

    /// <summary>Packs a color in the 0x00BBGGRR layout the color table uses.</summary>
    public static uint Rgb(byte red, byte green, byte blue) => (uint)(red | (green << 8) | (blue << 16));

    public readonly ColorType Type => _meta;

    public readonly byte Index => _red;

    public readonly byte R => _red;

    public readonly byte G => _green;

    public readonly byte B => _blue;

    public readonly uint RgbValue => Rgb(_red, _green, _blue);

    public readonly bool CanBeBrightened() => IsIndex16() || IsDefault();

    public readonly bool IsLegacy() => (IsIndex16() || IsIndex256()) && _red < 16;

    public readonly bool IsIndex16() => _meta == ColorType.IsIndex16;

    public readonly bool IsIndex256() => _meta == ColorType.IsIndex256;

    public readonly bool IsDefault() => _meta == ColorType.IsDefault;

    public readonly bool IsDefaultOrLegacy() => _meta != ColorType.IsRgb && _red < 16;

    public readonly bool IsRgb() => _meta == ColorType.IsRgb;

    public void SetColor(uint rgbColor)
    {
        _meta = ColorType.IsRgb;
        _red = (byte)rgbColor;
        _green = (byte)(rgbColor >> 8);
        _blue = (byte)(rgbColor >> 16);
    }

    public void SetIndex(byte index, bool isIndex256)
    {
        _meta = isIndex256 ? ColorType.IsIndex256 : ColorType.IsIndex16;
        _red = index;
        _green = 0;
        _blue = 0;
    }

    public void SetDefault()
    {
        _meta = ColorType.IsDefault;
        _red = 0;
        _green = 0;
        _blue = 0;
    }

    /// <summary>
    /// Resolves the color against a table of <see cref="TABLE_SIZE"/> entries. A brightened default color that matches
    /// one of the eight dark table colors becomes its bright counterpart.
    /// </summary>
    public readonly uint GetColor(ReadOnlySpan<uint> colorTable, int defaultIndex, bool brighten = false)
    {
        if (IsDefault())
        {
            uint defaultColor = colorTable[defaultIndex];
            if (brighten)
            {
                for (int index = 0; index < 8; index++)
                {
                    if (colorTable[index] == defaultColor)
                    {
                        return colorTable[index + 8];
                    }
                }
            }

            return defaultColor;
        }

        if (IsRgb())
        {
            return RgbValue;
        }

        if (IsIndex16() && brighten)
        {
            return colorTable[_red | 8];
        }

        return colorTable[_red];
    }

    public readonly bool Equals(TextColor other)
        => _meta == other._meta && _red == other._red && _green == other._green && _blue == other._blue;

    public override readonly bool Equals(object? obj) => obj is TextColor other && Equals(other);

    public override readonly int GetHashCode() => _red | (_green << 8) | (_blue << 16) | ((int)_meta << 24);

    public static bool operator ==(TextColor left, TextColor right) => left.Equals(right);

    public static bool operator !=(TextColor left, TextColor right) => !left.Equals(right);

    public override readonly string ToString()
    {
        if (IsDefault())
        {
            return "{default}";
        }

        if (IsRgb())
        {
            return $"{{RGB:0x{RgbValue:x6}}}";
        }

        return $"{{index:0x{_red:x4}}}";
    }
}
