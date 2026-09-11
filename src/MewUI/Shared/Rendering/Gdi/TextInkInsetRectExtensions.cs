using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>Applies a text ink inset to the device rectangles the GDI text paths lay out in.</summary>
internal static class TextInkInsetRectExtensions
{
    public static RECT Inflate(this TextInkInsetPx inset, RECT rect)
        => RECT.FromLTRB(rect.left - inset.Left, rect.top - inset.Top, rect.right + inset.Right, rect.bottom + inset.Bottom);

    /// <summary>The run box inside a raster of the given size, in raster-local pixels.</summary>
    public static RECT Inner(this TextInkInsetPx inset, int widthPx, int heightPx)
        => RECT.FromLTRB(inset.Left, inset.Top, widthPx - inset.Right, heightPx - inset.Bottom);
}
