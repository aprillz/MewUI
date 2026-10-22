using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering.Simd;

namespace Aprillz.MewUI;

/// <summary>
/// Converts other pixel layouts to the BGRA32 layout that <see cref="ImageSource.FromBgraPixels(Bgra32PixelBuffer)"/>
/// and backends consume.
/// </summary>
/// <remarks>
/// Output is a freshly allocated <see cref="Bgra32PixelBuffer"/>.
/// </remarks>
public static class PixelFormatConverter
{
    /// <summary>
    /// Swizzles an RGBA32 buffer into BGRA32 by swapping R and B per pixel; throws when the dimensions are not
    /// positive or <paramref name="rgba"/> is not exactly <c>width * height * 4</c> bytes.
    /// </summary>
    /// <param name="width">Pixel width.</param>
    /// <param name="height">Pixel height.</param>
    /// <param name="rgba">Source pixels in tight-packed RGBA byte order.</param>
    /// <param name="hasAlpha">Whether the source carries meaningful alpha.</param>
    public static Bgra32PixelBuffer FromRgba(int width, int height, ReadOnlySpan<byte> rgba, bool hasAlpha = true)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Dimensions must be positive.");
        }
        int expected = checked(width * height * 4);
        if (rgba.Length != expected)
        {
            throw new ArgumentException($"Expected {expected} bytes (tight-packed RGBA32), got {rgba.Length}.", nameof(rgba));
        }

        var bgra = GC.AllocateUninitializedArray<byte>(expected);
        SimdDispatcher.SwapRedBlue32(rgba, bgra);
        return new Bgra32PixelBuffer(width, height, bgra, hasAlpha);
    }
}
