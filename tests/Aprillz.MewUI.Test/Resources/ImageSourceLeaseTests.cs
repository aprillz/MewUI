using System.Buffers.Binary;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Resources;

[TestClass]
public sealed class ImageSourceLeaseTests
{
    [TestMethod]
    public void SameSourceAndFactory_ShareOneBackendRealization()
    {
        var source = ImageSource.FromBgraPixels(2, 2, new byte[16]);
        var factory = Application.DefaultGraphicsFactory;
        var before = RenderMemoryLedger.Snapshot();

        using var first = source.CreateImage(factory);
        using var second = source.CreateImage(factory);
        var active = RenderMemoryLedger.Snapshot();

        Assert.AreSame(
            ImageResource.ResolveBackendImage(first),
            ImageResource.ResolveBackendImage(second));
        Assert.AreEqual(before.NativeImageRealizationCreated + 1, active.NativeImageRealizationCreated);
        Assert.AreEqual(before.NativeImageRealizationCount + 1, active.NativeImageRealizationCount);

        first.Dispose();
        Assert.AreEqual(
            before.NativeImageRealizationCount + 1,
            RenderMemoryLedger.Snapshot().NativeImageRealizationCount);

        second.Dispose();
        Assert.AreEqual(
            before.NativeImageRealizationCount,
            RenderMemoryLedger.Snapshot().NativeImageRealizationCount);
    }

    [TestMethod]
    public void VariantUpgrade_KeepsOldDecodedPixelsUntilLastLeaseEnds()
    {
        var source = ImageSource.FromBytes(CreateBgraBmp(4, 2));
        var factory = Application.DefaultGraphicsFactory;
        var before = RenderMemoryLedger.Snapshot();

        using var small = source.CreateImage(factory, 2, 1);
        var afterSmall = RenderMemoryLedger.Snapshot();
        Assert.AreEqual(before.DecodedPixelCount + 1, afterSmall.DecodedPixelCount);
        Assert.AreEqual(2, small.PixelWidth);
        Assert.AreEqual(1, small.PixelHeight);

        using var full = source.CreateImage(factory, 4, 2);
        var afterUpgrade = RenderMemoryLedger.Snapshot();
        Assert.AreEqual(before.DecodedPixelCount + 2, afterUpgrade.DecodedPixelCount);
        Assert.AreEqual(before.NativeImageRealizationCount + 2, afterUpgrade.NativeImageRealizationCount);

        small.Dispose();
        var afterOldLease = RenderMemoryLedger.Snapshot();
        Assert.AreEqual(before.DecodedPixelCount + 1, afterOldLease.DecodedPixelCount);
        Assert.AreEqual(before.NativeImageRealizationCount + 1, afterOldLease.NativeImageRealizationCount);
    }

    private static byte[] CreateBgraBmp(int width, int height)
    {
        int pixelBytes = checked(width * height * 4);
        byte[] data = new byte[54 + pixelBytes];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2, 4), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10, 4), 54);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22, 4), height);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28, 2), 32);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(34, 4), pixelBytes);
        return data;
    }
}
