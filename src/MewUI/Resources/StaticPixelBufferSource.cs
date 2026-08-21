namespace Aprillz.MewUI.Resources;

internal sealed class StaticPixelBufferSource : IPixelBufferSource
{
    private readonly Memory<byte> _bgra;

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int StrideBytes => PixelWidth * 4;
    public int Version => 0;
    public bool HasAlpha { get; }

    public StaticPixelBufferSource(int widthPx, int heightPx, Memory<byte> bgra, bool hasAlpha = true)
    {
        if (widthPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPx));
        }

        if (heightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightPx));
        }

        if (bgra.IsEmpty || bgra.Length != widthPx * heightPx * 4)
        {
            throw new ArgumentException("Invalid BGRA buffer length.", nameof(bgra));
        }

        PixelWidth = widthPx;
        PixelHeight = heightPx;
        _bgra = bgra;
        HasAlpha = hasAlpha;
    }

    public PixelBufferLock Lock() =>
        new(_bgra, PixelWidth, PixelHeight, StrideBytes, 0, dirtyRegion: null, release: null);
}

