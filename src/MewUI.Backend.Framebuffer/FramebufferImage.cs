using Aprillz.MewUI.Resources;
using SkiaSharp;

namespace Aprillz.MewUI.Rendering.Framebuffer;

internal sealed class FramebufferImage : IImage
{
    private SKBitmap? _bitmap;
    private SKImage? _image;

    private FramebufferImage(SKBitmap bitmap)
    {
        _bitmap = bitmap;
    }

    public int PixelWidth => Bitmap.Width;

    public int PixelHeight => Bitmap.Height;

    internal SKBitmap Bitmap => _bitmap ?? throw new ObjectDisposedException(nameof(FramebufferImage));

    internal SKImage Image => _image ??= SKImage.FromBitmap(Bitmap);

    public static FramebufferImage FromFile(string path)
    {
        var bitmap = SKBitmap.Decode(path) ?? throw new InvalidOperationException($"Failed to decode image '{path}'.");
        return new FramebufferImage(ToBgraPremul(bitmap));
    }

    public static FramebufferImage FromBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var bitmap = SKBitmap.Decode(data) ?? throw new InvalidOperationException("Failed to decode image bytes.");
        return new FramebufferImage(ToBgraPremul(bitmap));
    }

    public static FramebufferImage FromPixelSource(IPixelBufferSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var locked = source.Lock();

        var info = new SKImageInfo(locked.PixelWidth, locked.PixelHeight, SKColorType.Bgra8888,
            source.IsPremultiplied ? SKAlphaType.Premul : SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        CopyRows(locked.Buffer, locked.StrideBytes, bitmap.GetPixels(), bitmap.RowBytes, locked.PixelWidth * 4, locked.PixelHeight);
        return new FramebufferImage(bitmap);
    }

    public void Dispose()
    {
        var image = Interlocked.Exchange(ref _image, null);
        image?.Dispose();

        var bitmap = Interlocked.Exchange(ref _bitmap, null);
        bitmap?.Dispose();
    }

    private static SKBitmap ToBgraPremul(SKBitmap source)
    {
        if (source.ColorType == SKColorType.Bgra8888 && source.AlphaType == SKAlphaType.Premul)
        {
            return source;
        }

        var converted = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(converted);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        source.Dispose();
        return converted;
    }

    private static unsafe void CopyRows(byte[] source, int sourceStride, nint destination, int destinationStride, int rowBytes, int height)
    {
        fixed (byte* sourcePtr = source)
        {
            for (int y = 0; y < height; y++)
            {
                new ReadOnlySpan<byte>(sourcePtr + y * sourceStride, rowBytes)
                    .CopyTo(new Span<byte>((void*)(destination + y * destinationStride), rowBytes));
            }
        }
    }
}
