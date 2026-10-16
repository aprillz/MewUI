namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>The arrow drawn on the DRM cursor plane, with its hot spot at the top-left pixel.</summary>
internal static class FramebufferCursorImage
{
    private const uint OUTLINE = 0xFF000000;
    private const uint FILL = 0xFFFFFFFF;

    // 'X' outline, '.' fill, ' ' transparent.
    private static readonly string[] _arrow =
    [
        "X",
        "XX",
        "X.X",
        "X..X",
        "X...X",
        "X....X",
        "X.....X",
        "X......X",
        "X.......X",
        "X........X",
        "X.........X",
        "X......XXXXX",
        "X...X..X",
        "X..XX..X",
        "X.X  X..X",
        "XX   X..X",
        "X     X..X",
        "      X..X",
        "       XX",
    ];

    /// <summary>Writes the arrow as ARGB8888 into a square <paramref name="size"/> image, clearing the rest.</summary>
    public static void RenderArrow(Span<uint> pixels, int size)
    {
        pixels.Clear();
        for (int row = 0; row < _arrow.Length && row < size; row++)
        {
            string line = _arrow[row];
            for (int column = 0; column < line.Length && column < size; column++)
            {
                pixels[row * size + column] = line[column] switch
                {
                    'X' => OUTLINE,
                    '.' => FILL,
                    _ => 0,
                };
            }
        }
    }
}
