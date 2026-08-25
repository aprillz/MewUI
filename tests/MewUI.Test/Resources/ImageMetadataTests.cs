using System.Buffers.Binary;

using Aprillz.MewUI.Resources;

namespace MewUI.Test.Resources;

[TestClass]
public sealed class ImageMetadataTests
{
    [TestMethod]
    public void BuiltInDecoders_ReadDimensionsWithoutPixelDecode()
    {
        AssertMetadata(CreatePngHeader(320, 180), 320, 180, hasAlpha: true);
        AssertMetadata(CreateJpegHeader(640, 360), 640, 360, hasAlpha: false);
        AssertMetadata(CreateBmpHeader(800, 600, 24), 800, 600, hasAlpha: false);
        AssertMetadata(CreateIconHeader(256, 128), 256, 128, hasAlpha: true);
    }

    [TestMethod]
    public void MetadataProbe_RejectsOversizedDimensions()
    {
        Assert.IsFalse(ImageDecoders.TryReadMetadata(CreatePngHeader(32768, 32768), out _));
        Assert.IsFalse(ImageDecoders.TryReadMetadata(CreateBmpHeader(40000, 1, 32), out _));
    }

    private static void AssertMetadata(byte[] encoded, int width, int height, bool hasAlpha)
    {
        Assert.IsTrue(ImageDecoders.TryReadMetadata(encoded, out var metadata));
        Assert.AreEqual(width, metadata.PixelWidth);
        Assert.AreEqual(height, metadata.PixelHeight);
        Assert.AreEqual(hasAlpha, metadata.HasAlpha);
        Assert.AreEqual(ImageOrientation.Identity, metadata.Orientation);
    }

    private static byte[] CreatePngHeader(int width, int height)
    {
        byte[] data = new byte[29];
        byte[] signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(data.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20, 4), height);
        data[24] = 8;
        data[25] = 6;
        return data;
    }

    private static byte[] CreateJpegHeader(int width, int height)
    {
        byte[] data = new byte[21];
        data[0] = 0xFF;
        data[1] = 0xD8;
        data[2] = 0xFF;
        data[3] = 0xC0;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4, 2), 17);
        data[6] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(7, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(9, 2), (ushort)width);
        data[11] = 3;
        return data;
    }

    private static byte[] CreateBmpHeader(int width, int height, ushort bitsPerPixel)
    {
        byte[] data = new byte[54];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22, 4), height);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28, 2), bitsPerPixel);
        return data;
    }

    private static byte[] CreateIconHeader(int width, int height)
    {
        byte[] data = new byte[22];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4, 2), 1);
        data[6] = width == 256 ? (byte)0 : (byte)width;
        data[7] = height == 256 ? (byte)0 : (byte)height;
        return data;
    }
}
