namespace Aprillz.MewUI.Resources;

internal sealed class StaticPixelBufferSource : IPixelBufferSource
{
    private readonly byte[] _bgra;

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int StrideBytes => PixelWidth * 4;
    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;
    public int Version => 0;

    public StaticPixelBufferSource(int widthPx, int heightPx, byte[] bgra)
    {
        if (widthPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPx));
        }

        if (heightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightPx));
        }

        ArgumentNullException.ThrowIfNull(bgra);
        if (bgra.Length != widthPx * heightPx * 4)
        {
            throw new ArgumentException("Invalid BGRA buffer length.", nameof(bgra));
        }

        PixelWidth = widthPx;
        PixelHeight = heightPx;
        _bgra = bgra;
    }

    public PixelBufferLock Lock() =>
        new(_bgra, PixelWidth, PixelHeight, StrideBytes, PixelFormat, version: 0, release: null);
}

