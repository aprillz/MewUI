using System.Buffers.Binary;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Resources;

[TestClass]
public sealed class ImageSourceLeaseTests
{
    [TestMethod]
    public void SameSourceAndFactory_ShareOneBackendRealization()
    {
        var source = ImageSource.FromBgraPixels(2, 2, new byte[16]);
        var factory = Application.DefaultGraphicsFactory;
        var before = RenderResourceMetrics.Snapshot();

        using var first = source.CreateImage(factory);
        using var second = source.CreateImage(factory);
        var active = RenderResourceMetrics.Snapshot();

        Assert.AreSame(
            ImageResource.ResolveBackendImage(first),
            ImageResource.ResolveBackendImage(second));
        Assert.AreEqual(before.NativeImageRealizationCreated + 1, active.NativeImageRealizationCreated);
        Assert.AreEqual(before.NativeImageRealizationCount + 1, active.NativeImageRealizationCount);

        first.Dispose();
        Assert.AreEqual(
            before.NativeImageRealizationCount + 1,
            RenderResourceMetrics.Snapshot().NativeImageRealizationCount);

        second.Dispose();
        Assert.AreEqual(
            before.NativeImageRealizationCount,
            RenderResourceMetrics.Snapshot().NativeImageRealizationCount);
    }

    [TestMethod]
    public void VariantUpgrade_KeepsOldDecodedPixelsUntilLastLeaseEnds()
    {
        var source = ImageSource.FromBytes(CreateBgraBmp(4, 2));
        var factory = Application.DefaultGraphicsFactory;
        var before = RenderResourceMetrics.Snapshot();

        using var small = source.CreateImage(factory, 2, 1);
        var afterSmall = RenderResourceMetrics.Snapshot();
        Assert.AreEqual(before.DecodedPixelCount + 1, afterSmall.DecodedPixelCount);
        Assert.AreEqual(2, small.PixelWidth);
        Assert.AreEqual(1, small.PixelHeight);

        using var full = source.CreateImage(factory, 4, 2);
        var afterUpgrade = RenderResourceMetrics.Snapshot();
        Assert.AreEqual(before.DecodedPixelCount + 2, afterUpgrade.DecodedPixelCount);
        Assert.AreEqual(before.NativeImageRealizationCount + 2, afterUpgrade.NativeImageRealizationCount);

        small.Dispose();
        var afterOldLease = RenderResourceMetrics.Snapshot();
        Assert.AreEqual(before.DecodedPixelCount + 1, afterOldLease.DecodedPixelCount);
        Assert.AreEqual(before.NativeImageRealizationCount + 1, afterOldLease.NativeImageRealizationCount);
    }

    [TestMethod]
    public void RoundedTargetVariant_IsReusedForSameRequest()
    {
        using var source = ImageSource.FromBytes(CreateBgraBmp(480, 135));
        var factory = Application.DefaultGraphicsFactory;
        var before = RenderResourceMetrics.Snapshot();

        using var first = source.CreateImage(factory, 480, 128);
        var afterFirst = RenderResourceMetrics.Snapshot();
        using var second = source.CreateImage(factory, 480, 128);
        var afterSecond = RenderResourceMetrics.Snapshot();

        Assert.AreEqual(455, first.PixelWidth);
        Assert.AreEqual(128, first.PixelHeight);
        Assert.AreSame(
            ImageResource.ResolveBackendImage(first),
            ImageResource.ResolveBackendImage(second));
        Assert.AreEqual(before.DecodeAttempts + 1, afterFirst.DecodeAttempts);
        Assert.AreEqual(afterFirst.DecodeAttempts, afterSecond.DecodeAttempts);
        Assert.AreEqual(
            afterFirst.NativeImageRealizationCreated,
            afterSecond.NativeImageRealizationCreated);
    }

    [TestMethod]
    public void DeviceGenerationChange_DoesNotReusePreviousRealization()
    {
        var source = ImageSource.FromBgraPixels(2, 2, new byte[16]);
        using var factory = new GenerationGraphicsFactory(Application.DefaultGraphicsFactory);
        using var first = source.CreateImage(factory);
        var firstBackend = ImageResource.ResolveBackendImage(first);

        factory.Generation++;
        using var second = source.CreateImage(factory);

        Assert.AreNotSame(firstBackend, ImageResource.ResolveBackendImage(second));
    }

    [TestMethod]
    public void FactoryRetirement_RemovesRegistryOwnershipButWaitsForTheActiveView()
    {
        var source = ImageSource.FromBgraPixels(2, 2, new byte[16]);
        using var factory = new GenerationGraphicsFactory(Application.DefaultGraphicsFactory);
        var before = RenderResourceMetrics.Snapshot();
        var image = source.CreateImage(factory);

        Assert.AreEqual(1, source.ActiveRealizationCount);
        Assert.AreEqual(
            before.NativeImageRealizationCount + 1,
            RenderResourceMetrics.Snapshot().NativeImageRealizationCount);

        ImageSource.RetireRealizationsForFactory(factory);

        Assert.AreEqual(0, source.ActiveRealizationCount);
        Assert.AreEqual(
            before.NativeImageRealizationCount + 1,
            RenderResourceMetrics.Snapshot().NativeImageRealizationCount);
        Assert.Throws<ObjectDisposedException>(() => source.CreateImage(factory));

        image.Dispose();
        Assert.AreEqual(
            before.NativeImageRealizationCount,
            RenderResourceMetrics.Snapshot().NativeImageRealizationCount);
    }

    [TestMethod]
    public void GdiFactoryDispose_RetiresRealizationButKeepsActiveLeaseAlive()
    {
        var source = ImageSource.FromBgraPixels(2, 2, new byte[16]);
        var factory = new GdiGraphicsFactory();
        var before = RenderResourceMetrics.Snapshot();
        var image = source.CreateImage(factory);

        factory.Dispose();

        Assert.AreEqual(0, source.ActiveRealizationCount);
        Assert.AreEqual(
            before.NativeImageRealizationCount + 1,
            RenderResourceMetrics.Snapshot().NativeImageRealizationCount);
        Assert.Throws<ObjectDisposedException>(() => source.CreateImage(factory));

        image.Dispose();
        Assert.AreEqual(
            before.NativeImageRealizationCount,
            RenderResourceMetrics.Snapshot().NativeImageRealizationCount);
    }

    [TestMethod]
    public void SourceDispose_ReleasesOwnershipButKeepsActiveImageLeaseAlive()
    {
        var before = RenderResourceMetrics.Snapshot();
        var source = ImageSource.FromBgraPixels(2, 2, new byte[16]);
        var image = source.CreateImage(Application.DefaultGraphicsFactory);

        source.Dispose();

        var retired = RenderResourceMetrics.Snapshot();
        Assert.AreEqual(before.DecodedPixelCount + 1, retired.DecodedPixelCount);
        Assert.AreEqual(before.NativeImageRealizationCount + 1, retired.NativeImageRealizationCount);
        Assert.ThrowsExactly<ObjectDisposedException>(
            () => source.CreateImage(Application.DefaultGraphicsFactory));

        image.Dispose();

        var released = RenderResourceMetrics.Snapshot();
        Assert.AreEqual(before.DecodedPixelCount, released.DecodedPixelCount);
        Assert.AreEqual(before.NativeImageRealizationCount, released.NativeImageRealizationCount);
    }

    [TestMethod]
    public void SourceDispose_WithoutActiveLeaseReleasesDecodedPixelsImmediately()
    {
        var before = RenderResourceMetrics.Snapshot();
        var source = ImageSource.FromBgraPixels(2, 2, new byte[16]);

        source.Dispose();

        Assert.AreEqual(before.DecodedPixelCount, RenderResourceMetrics.Snapshot().DecodedPixelCount);
    }

    [TestMethod]
    public void EncodedFactoryHelper_ReleasesTemporarySourceAfterReturnedImageLeaseEnds()
    {
        var before = RenderResourceMetrics.Snapshot();
        var image = Application.DefaultGraphicsFactory.CreateImageFromBytes(CreateBgraBmp(2, 2));

        Assert.AreEqual(
            before.DecodedPixelCount + 1,
            RenderResourceMetrics.Snapshot().DecodedPixelCount);

        image.Dispose();

        Assert.AreEqual(before.DecodedPixelCount, RenderResourceMetrics.Snapshot().DecodedPixelCount);
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

    private sealed class GenerationGraphicsFactory(IGraphicsFactory inner) : IGraphicsFactory
    {
        public uint Generation { get; set; }
        public string Backend => inner.Backend;
        public RenderDeviceIdentity RenderIdentity => inner.RenderIdentity with { Generation = Generation };
        public IRenderResourceCache? ResourceCache => inner.ResourceCache;
        public IRenderEffectDevice? Effects => inner.Effects;
        public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal, bool italic = false, bool underline = false, bool strikethrough = false) => inner.CreateFont(family, size, weight, italic, underline, strikethrough);
        public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal, bool italic = false, bool underline = false, bool strikethrough = false) => inner.CreateFont(family, size, dpi, weight, italic, underline, strikethrough);
        public IImage CreateImageFromFile(string path) => inner.CreateImageFromFile(path);
        public IImage CreateImageFromBytes(byte[] data) => inner.CreateImageFromBytes(data);
        public IGraphicsContext CreateContext(IRenderTarget target) => inner.CreateContext(target);
        public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor) => inner.CreateSurface(descriptor);
        public IGraphicsContext CreateContext(IRenderSurface surface) => inner.CreateContext(surface);
        public IImage CreateImageView(IRenderSurface surface) => inner.CreateImageView(surface);
        public IImage CreateImageView(IPixelBufferSource source) => inner.CreateImageView(source);
        public IImage CreateImageView(IExternalRasterSource source) => inner.CreateImageView(source);
        public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes) => inner.TryReadPixels(source, destination, destinationStrideBytes);
        public IRenderOperation RequestReadback(IRenderSurface source) => inner.RequestReadback(source);
        public IRenderOperation FlushAsyncWork() => inner.FlushAsyncWork();
        public void Dispose() { }
    }
}
