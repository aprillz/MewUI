using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// Device pixels a text raster grows beyond its layout rectangle on each side so glyph ink that
/// overhangs the run box is rasterized instead of cut; the layout rectangle sits inset inside it.
/// </summary>
internal readonly record struct TextInkInsetPx(int Left, int Top, int Right, int Bottom)
{
    public bool HasInset => Left > 0 || Top > 0 || Right > 0 || Bottom > 0;

    public RECT Inflate(RECT rect)
        => RECT.FromLTRB(rect.left - Left, rect.top - Top, rect.right + Right, rect.bottom + Bottom);

    /// <summary>The layout rectangle inside a raster of the given size, in raster-local pixels.</summary>
    public RECT Inner(int widthPx, int heightPx)
        => RECT.FromLTRB(Left, Top, widthPx - Right, heightPx - Bottom);
}
