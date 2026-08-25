using System.Buffers.Binary;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ImageMeasureTests
{
    [TestMethod]
    public void Measure_Uniform_UsesConstrainedWidth()
    {
        var image = CreateImage(100, 50);

        image.Measure(new Size(200, double.PositiveInfinity));

        Assert.AreEqual(new Size(200, 100), image.DesiredSize);
    }

    [TestMethod]
    public void Measure_Uniform_UsesConstrainedHeight()
    {
        var image = CreateImage(100, 50);

        image.Measure(new Size(double.PositiveInfinity, 25));

        Assert.AreEqual(new Size(50, 25), image.DesiredSize);
    }

    [TestMethod]
    public void Measure_Fill_StretchesConstrainedAxesOnly()
    {
        var image = CreateImage(100, 50);
        image.StretchMode = Stretch.Fill;

        image.Measure(new Size(200, double.PositiveInfinity));

        Assert.AreEqual(new Size(200, 50), image.DesiredSize);
    }

    [TestMethod]
    public void Measure_None_UsesIntrinsicSize()
    {
        var image = CreateImage(100, 50);
        image.StretchMode = Stretch.None;

        image.Measure(new Size(200, 200));

        Assert.AreEqual(new Size(100, 50), image.DesiredSize);
    }

    [TestMethod]
    public void Measure_RasterHeader_DoesNotDecodeOrRealize()
    {
        var source = ImageSource.FromBytes(CreatePngHeader(100, 50));
        var image = new Image
        {
            Source = source,
            StretchMode = Stretch.None,
        };
        var before = RenderResourceMetrics.Snapshot();

        image.Measure(new Size(200, 200));

        var after = RenderResourceMetrics.Snapshot();
        Assert.AreEqual(new Size(100, 50), image.DesiredSize);
        Assert.AreEqual(before.DecodeAttempts, after.DecodeAttempts);
        Assert.AreEqual(before.ImageRealizationRequests, after.ImageRealizationRequests);
        Assert.AreEqual(before.MetadataProbeAttempts + 1, after.MetadataProbeAttempts);
        Assert.AreEqual(before.MetadataProbeSucceeded + 1, after.MetadataProbeSucceeded);
        Assert.AreEqual(100, source.PixelWidth);
        Assert.AreEqual(50, source.PixelHeight);
    }

    private static Image CreateImage(double width, double height) =>
        new()
        {
            Source = new TestVectorImageSource(new Size(width, height))
        };

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

    private sealed class TestVectorImageSource(Size intrinsicSize) : IVectorImageSource
    {
        public Size IntrinsicSize { get; } = intrinsicSize;

        public IImage CreateImage(IGraphicsFactory factory) =>
            throw new NotSupportedException("Vector image measure should not rasterize.");

        public void Render(IGraphicsContext context, Rect destRect)
        {
        }
    }
}
