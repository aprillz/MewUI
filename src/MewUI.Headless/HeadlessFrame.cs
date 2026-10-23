namespace Aprillz.MewUI.Headless;

/// <summary>
/// A copy of the pixels a window was last drawn with, as BGRA32 rows.
/// </summary>
public sealed class HeadlessFrame
{
    private readonly byte[] _pixels;

    internal HeadlessFrame(int width, int height, int stride, double dpiScale, byte[] pixels)
    {
        Width = width;
        Height = height;
        Stride = stride;
        DpiScale = dpiScale;
        _pixels = pixels;
    }

    /// <summary>Gets the width in device pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in device pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the byte distance between the starts of consecutive rows.</summary>
    public int Stride { get; }

    /// <summary>Gets the DPI scale the frame was drawn at (1.0 at 96 DPI).</summary>
    public double DpiScale { get; }

    /// <summary>Gets the BGRA32 pixel rows, <see cref="Stride"/> bytes apart.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>Returns the color of the pixel at device coordinates (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public Color GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(x < 0 || x >= Width ? nameof(x) : nameof(y));
        }

        int offset = (y * Stride) + (x * 4);
        return Color.FromArgb(_pixels[offset + 3], _pixels[offset + 2], _pixels[offset + 1], _pixels[offset]);
    }
}
