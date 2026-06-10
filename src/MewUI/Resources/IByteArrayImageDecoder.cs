namespace Aprillz.MewUI.Resources;

// Optional fast-path for decoders that can avoid extra allocations when the caller already has a byte[].
// (ReadOnlySpan<byte> does not guarantee access to the underlying array.)
internal interface IByteArrayImageDecoder
{
    bool TryDecode(byte[] encoded, out Bgra32PixelBuffer bitmap);

    bool TryReadInfo(byte[] encoded, out int width, out int height, out bool hasAlpha);

    bool TryDecodeInto(byte[] encoded, Span<byte> destination, out int width, out int height, out bool hasAlpha);
}
