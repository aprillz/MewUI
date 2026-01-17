namespace Aprillz.MewUI.Resources;

// Optional fast-path for decoders that can avoid extra allocations when the caller already has a byte[].
// (ReadOnlySpan<byte> does not guarantee access to the underlying array.)
internal interface IByteArrayImageDecoder
{
    bool TryDecode(byte[] encoded, out DecodedBitmap bitmap);
}
