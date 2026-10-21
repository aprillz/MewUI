// Ported from Windows Terminal src/types/colorTable.cpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;

namespace Aprillz.MewUI.Terminal.Types;

internal static class ColorTable
{
    private static readonly uint[] _campbell =
    [
        TextColor.Rgb(0x0C, 0x0C, 0x0C),
        TextColor.Rgb(0xC5, 0x0F, 0x1F),
        TextColor.Rgb(0x13, 0xA1, 0x0E),
        TextColor.Rgb(0xC1, 0x9C, 0x00),
        TextColor.Rgb(0x00, 0x37, 0xDA),
        TextColor.Rgb(0x88, 0x17, 0x98),
        TextColor.Rgb(0x3A, 0x96, 0xDD),
        TextColor.Rgb(0xCC, 0xCC, 0xCC),
        TextColor.Rgb(0x76, 0x76, 0x76),
        TextColor.Rgb(0xE7, 0x48, 0x56),
        TextColor.Rgb(0x16, 0xC6, 0x0C),
        TextColor.Rgb(0xF9, 0xF1, 0xA5),
        TextColor.Rgb(0x3B, 0x78, 0xFF),
        TextColor.Rgb(0xB4, 0x00, 0x9E),
        TextColor.Rgb(0x61, 0xD6, 0xD6),
        TextColor.Rgb(0xF2, 0xF2, 0xF2),
    ];

    private static readonly byte[] _cubeScale = [0, 0x5F, 0x87, 0xAF, 0xD7, 0xFF];

    /// <summary>The 16 ANSI colors of the Campbell scheme, in ANSI order.</summary>
    public static ReadOnlySpan<uint> Campbell => _campbell;

    /// <summary>Fills up to 256 entries: the 16 ANSI colors, the 6x6x6 cube and the 24 grays.</summary>
    public static void Initialize(Span<uint> table)
    {
        InitializeAnsi(table);
        InitializeExtended(table);
    }

    public static void InitializeAnsi(Span<uint> table)
    {
        if (table.Length >= _campbell.Length)
        {
            _campbell.CopyTo(table);
        }
    }

    public static void InitializeExtended(Span<uint> table, bool monochrome = false)
    {
        if (table.Length < 256)
        {
            return;
        }

        for (int index = 0; index < 216; index++)
        {
            byte red = _cubeScale[(index / 36) % 6];
            byte green = _cubeScale[(index / 6) % 6];
            byte blue = _cubeScale[index % 6];
            if (monochrome)
            {
                red = green = blue = (byte)((red + green + blue) / 3);
            }

            table[index + 16] = TextColor.Rgb(red, green, blue);
        }

        for (int index = 0; index < 24; index++)
        {
            byte level = (byte)(index * 10 + 8);
            table[index + 232] = TextColor.Rgb(level, level, level);
        }
    }
}
