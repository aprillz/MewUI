using Aprillz.MewUI;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Resources;

[TestClass]
public sealed class ImageSourceOrientationTests
{
    private static readonly byte[] OrientedMagic = [0xAB, 0xCD, 0xEF, 0x5A];
    private static readonly byte[] PlainMagic = [0xAB, 0xCD, 0xEF, 0x5B];

    [ClassInitialize]
    public static void Init(TestContext context)
    {
        ImageDecoders.Register(new FakeOrientedDecoder());
        ImageDecoders.Register(new FakePlainDecoder());
    }

    [TestMethod]
    public void TryDecode_ReturnsDecoderOrientation()
    {
        Assert.IsTrue(ImageDecoders.TryDecode(Oriented(ImageOrientation.Rotate90), out var pixels, out var orientation));

        Assert.AreEqual(ImageOrientation.Rotate90, orientation);
        Assert.AreEqual(1, pixels.WidthPx);
        Assert.AreEqual(1, pixels.HeightPx);
    }

    [TestMethod]
    public void TryDecode_DefaultDecoder_IsIdentity()
    {
        Assert.IsTrue(ImageDecoders.TryDecode(PlainMagic, out _, out var orientation));

        Assert.AreEqual(ImageOrientation.Identity, orientation);
    }

    [TestMethod]
    public void ImageSource_ExposesOrientationAfterDecode()
    {
        var source = ImageSource.FromBytes(Oriented(ImageOrientation.Transverse));

        Assert.AreEqual(ImageOrientation.Identity, source.Orientation, "Identity before decode");

        source.EnsureDecode();

        Assert.AreEqual(ImageOrientation.Transverse, source.Orientation);
    }

    [TestMethod]
    public void RawPixelSource_IsIdentity()
    {
        var source = ImageSource.FromBgraPixels(1, 1, new byte[4]);

        source.EnsureDecode();

        Assert.AreEqual(ImageOrientation.Identity, source.Orientation);
    }

    [TestMethod]
    public void EnsureDecode_TransfersLedgerBytesFromEncodedToDecoded()
    {
        byte[] encoded = Oriented(ImageOrientation.Normal);
        var before = RenderResourceMetrics.Snapshot();
        var source = ImageSource.FromBytes(encoded);
        var encodedSnapshot = RenderResourceMetrics.Snapshot();

        Assert.AreEqual(before.EncodedBackingCount + 1, encodedSnapshot.EncodedBackingCount);
        Assert.AreEqual(before.EncodedBackingBytes + encoded.LongLength, encodedSnapshot.EncodedBackingBytes);

        source.EnsureDecode();
        var decodedSnapshot = RenderResourceMetrics.Snapshot();

        Assert.AreEqual(before.EncodedBackingCount, decodedSnapshot.EncodedBackingCount);
        Assert.AreEqual(before.EncodedBackingBytes, decodedSnapshot.EncodedBackingBytes);
        Assert.AreEqual(encodedSnapshot.DecodedPixelCount + 1, decodedSnapshot.DecodedPixelCount);
        Assert.AreEqual(encodedSnapshot.DecodedPixelBytes + 4, decodedSnapshot.DecodedPixelBytes);
        Assert.AreEqual(encodedSnapshot.DecodeAttempts + 1, decodedSnapshot.DecodeAttempts);
        Assert.AreEqual(encodedSnapshot.DecodeSucceeded + 1, decodedSnapshot.DecodeSucceeded);
        Assert.AreEqual(encodedSnapshot.DecodeFailed, decodedSnapshot.DecodeFailed);
        GC.KeepAlive(source);
    }

    private static byte[] Oriented(ImageOrientation orientation) => [.. OrientedMagic, (byte)orientation];

    private static bool StartsWith(ReadOnlySpan<byte> data, byte[] prefix) =>
        data.Length >= prefix.Length && data.Slice(0, prefix.Length).SequenceEqual(prefix);

    private static Bgra32PixelBuffer OnePixel() => new(1, 1, new byte[4], HasAlpha: false);

    private sealed class FakeOrientedDecoder : IImageDecoder
    {
        public string Id => "fake-oriented";
        public bool CanDecode(ReadOnlySpan<byte> encoded) => StartsWith(encoded, OrientedMagic);

        public bool TryDecode(ReadOnlySpan<byte> encoded, out Bgra32PixelBuffer bitmap)
        {
            bitmap = OnePixel();
            return true;
        }

        // The orientation is encoded as the byte right after the magic.
        public ImageOrientation ReadOrientation(ReadOnlySpan<byte> encoded) =>
            (ImageOrientation)encoded[OrientedMagic.Length];
    }

    // Does not override ReadOrientation: exercises the default-interface-method (Identity).
    private sealed class FakePlainDecoder : IImageDecoder
    {
        public string Id => "fake-plain";
        public bool CanDecode(ReadOnlySpan<byte> encoded) => StartsWith(encoded, PlainMagic);

        public bool TryDecode(ReadOnlySpan<byte> encoded, out Bgra32PixelBuffer bitmap)
        {
            bitmap = OnePixel();
            return true;
        }
    }
}
