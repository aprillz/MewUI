using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Layout;

/// <summary>Wrapped text blocks whose words fall on many different line break widths.</summary>
internal static class LayoutTexts
{
    public const double FONT_SIZE = 13;

    public static TextBlock Wrapped(int seed)
        => new() { Text = Words(seed), TextWrapping = TextWrapping.Wrap, FontSize = FONT_SIZE };

    public static TextBlock[] Many(int count, int firstSeed = 0)
        => Enumerable.Range(firstSeed, count).Select(Wrapped).ToArray();

    public static TextBlock Label(string text) => new() { Text = text, FontSize = FONT_SIZE };

    public static Border Cell(int seed)
        => new() { Background = Color.FromArgb(255, (byte)(40 + seed * 37 % 180), 120, 200) };

    private static string Words(int seed)
        => string.Join(' ', Enumerable.Range(0, 6 + seed % 9)
            .Select(word => new string((char)('a' + (word + seed) % 26), 2 + (word * 7 + seed) % 9)));
}
