namespace Aprillz.MewUI.Resources;

internal readonly record struct ImageMetadata(
    int PixelWidth,
    int PixelHeight,
    ImageOrientation Orientation,
    bool HasAlpha);

internal interface IImageMetadataDecoder
{
    bool TryReadMetadata(ReadOnlySpan<byte> encoded, out ImageMetadata metadata);
}

internal interface IImageMetadataSource
{
    bool TryGetMetadata(out ImageMetadata metadata);
}

internal interface ITargetSizeImageDecoder
{
    bool TryDecode(byte[] encoded, int targetPixelWidth, int targetPixelHeight, out Bgra32PixelBuffer bitmap);
}

internal static class ImageMetadataValidation
{
    public static bool IsValidSize(int width, int height)
        => width > 0
           && height > 0
           && width <= ImageDecoders.MAX_IMAGE_DIMENSION
           && height <= ImageDecoders.MAX_IMAGE_DIMENSION
           && (long)width * height <= ImageDecoders.MAX_IMAGE_PIXEL_COUNT;
}
