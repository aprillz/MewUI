using System.Buffers.Binary;
using System.IO.Compression;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A cached raster variant must satisfy every later request for the same layout. Recording a scale
/// derived from the produced pixel size instead of the requested one made sizes whose uniform fit
/// rounds down miss forever, disposing the backend image and rebuilding its texture every frame.
/// </summary>
[TestClass]
public sealed class ImageVariantCacheTests
{
    // 480x135 shown at 227x64 is the gallery logo that first showed the rebuild; the rest cover
    // other roundings: half-pixel bounds, extreme aspect ratios, and sizes on the 128 class edge.
    [DataRow(480, 135, 227.0, 64.0)]
    [DataRow(480, 135, 227.5, 64.0)]
    [DataRow(480, 135, 113.0, 32.0)]
    [DataRow(455, 128, 227.0, 64.0)]
    [DataRow(333, 101, 200.0, 61.0)]
    [DataRow(1000, 3, 999.0, 3.0)]
    [DataRow(3, 1000, 3.0, 999.0)]
    [DataRow(129, 129, 128.0, 128.0)]
    [DataRow(257, 129, 256.0, 128.0)]
    [DataRow(100, 50, 33.0, 17.0)]
    [DataRow(64, 64, 63.0, 63.0)]
    [TestMethod]
    public void RepeatedRender_RealizesOnce(int intrinsicWidth, int intrinsicHeight, double width, double height)
    {
        var image = CreateArrangedImage(intrinsicWidth, intrinsicHeight, width, height);
        var context = new NoOpContext();

        image.Render(context);
        long afterFirst = RenderResourceMetrics.Snapshot().ImageRealizationRequests;
        for (int i = 0; i < 5; i++)
        {
            image.Render(context);
        }

        Assert.AreEqual(
            afterFirst,
            RenderResourceMetrics.Snapshot().ImageRealizationRequests,
            $"{intrinsicWidth}x{intrinsicHeight} at {width}x{height} rebuilt its variant while the layout was unchanged");
    }

    [TestMethod]
    public void GrowingLayout_RealizesAgainAtTheLargerScale()
    {
        var image = CreateArrangedImage(480, 135, 60, 17);
        var context = new NoOpContext();
        image.Render(context);
        long afterSmall = RenderResourceMetrics.Snapshot().ImageRealizationRequests;

        image.Arrange(new Rect(0, 0, 480, 135));
        image.Render(context);

        Assert.IsGreaterThan(
            afterSmall,
            RenderResourceMetrics.Snapshot().ImageRealizationRequests,
            "a layout that needs more resolution must request a new variant");
    }

    [TestMethod]
    public void ShrinkingLayout_KeepsTheResidentVariant()
    {
        var image = CreateArrangedImage(480, 135, 480, 135);
        var context = new NoOpContext();
        image.Render(context);
        long afterLarge = RenderResourceMetrics.Snapshot().ImageRealizationRequests;

        image.Arrange(new Rect(0, 0, 60, 17));
        image.Render(context);

        Assert.AreEqual(
            afterLarge,
            RenderResourceMetrics.Snapshot().ImageRealizationRequests,
            "a variant that already covers the request must not be rebuilt for a smaller layout");
    }

    private static Image CreateArrangedImage(int intrinsicWidth, int intrinsicHeight, double width, double height)
    {
        var image = new Image
        {
            Source = ImageSource.FromBytes(CreateOpaquePng(intrinsicWidth, intrinsicHeight)),
            StretchMode = Stretch.Uniform,
        };
        image.Measure(new Size(width, height));
        image.Arrange(new Rect(0, 0, width, height));
        return image;
    }

    /// <summary>
    /// An encoded single-colour PNG. The source must be encoded: a raw-pixel source keeps its full
    /// buffer, so it never produces the smaller-than-requested variant this test is about.
    /// </summary>
    private static byte[] CreateOpaquePng(int width, int height)
    {
        var raw = new byte[height * (width * 4 + 1)];
        for (int y = 0; y < height; y++)
        {
            int row = y * (width * 4 + 1);
            raw[row] = 0; // filter: None
            for (int x = 0; x < width; x++)
            {
                int p = row + 1 + x * 4;
                raw[p] = 0x60;
                raw[p + 1] = 0x40;
                raw[p + 2] = 0x20;
                raw[p + 3] = 0xFF;
            }
        }

        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(raw, 0, raw.Length);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // colour type: RGBA

        using var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR"u8, header);
        WriteChunk(png, "IDAT"u8, compressed.ToArray());
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);

        uint crc = 0xFFFFFFFFu;
        foreach (byte b in type) { crc = StepCrc(crc, b); }
        foreach (byte b in data) { crc = StepCrc(crc, b); }
        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, crc ^ 0xFFFFFFFFu);
        stream.Write(checksum);
    }

    private static uint StepCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }

    private sealed class NoOpContext : NoOpGraphicsContext
    {
    }
}
