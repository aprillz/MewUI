using SkiaSharp;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Event data for <see cref="SKElement.PaintSurface"/>.
/// Mirrors the shape of SkiaSharp's reusable SKElement event args.
/// </summary>
public class SKPaintSurfaceEventArgs : EventArgs
{
    public SKPaintSurfaceEventArgs(SKSurface surface, SKImageInfo info)
        : this(surface, info, info)
    {
    }

    public SKPaintSurfaceEventArgs(SKSurface surface, SKImageInfo info, SKImageInfo rawInfo)
    {
        Surface = surface;
        Info = info;
        RawInfo = rawInfo;
    }

    public SKSurface Surface { get; }

    public SKImageInfo Info { get; }

    public SKImageInfo RawInfo { get; }
}
