using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Device pixels a text raster grows beyond its run box on each side so glyph ink that overhangs the box
/// is rasterized instead of cut; the run box sits inset inside the grown raster.
/// </summary>
internal readonly record struct TextInkInsetPx(int Left, int Top, int Right, int Bottom)
{
    public bool HasInset => Left > 0 || Top > 0 || Right > 0 || Bottom > 0;

    /// <summary>
    /// Inset for a run carrying an ink overhang: the overhang rounded up to whole device pixels plus one
    /// antialiasing pixel per side. None for layouts without one, which keep their box as the raster.
    /// </summary>
    public static TextInkInsetPx FromOverhang(TextInkOverhang? overhang, double dpiScale)
    {
        if (overhang is not TextInkOverhang ink)
        {
            return default;
        }

        return new TextInkInsetPx(
            (int)Math.Ceiling(ink.Left * dpiScale) + 1,
            (int)Math.Ceiling(ink.Top * dpiScale) + 1,
            (int)Math.Ceiling(ink.Right * dpiScale) + 1,
            (int)Math.Ceiling(ink.Bottom * dpiScale) + 1);
    }
}
