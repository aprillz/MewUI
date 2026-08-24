using Aprillz.MewUI;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Resources;

[TestClass]
public sealed class TargetSizeDecodeTests
{
    [DataRow(4000, 3000, 1920, 1080, 2)]
    [DataRow(4000, 3000, 1000, 750, 4)]
    [DataRow(4000, 3000, 500, 375, 8)]
    [DataRow(4000, 3000, 501, 376, 4)]
    [DataRow(800, 600, 1920, 1080, 1)]
    [TestMethod]
    public void JpegScale_SelectsSmallestNativeOutputThatCoversTarget(
        int width,
        int height,
        int targetWidth,
        int targetHeight,
        int expectedDenominator)
    {
        Assert.AreEqual(
            expectedDenominator,
            JpegDecoder.SelectScaleDenominator(width, height, targetWidth, targetHeight));
    }

    [TestMethod]
    public void FitWithin_PreservesAspectAndCornerPixels()
    {
        byte[] pixels =
        [
            0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255,
            0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255,
        ];
        var source = new Bgra32PixelBuffer(4, 2, pixels);

        var result = Bgra32ImageResampler.FitWithin(source, 2, 2);

        Assert.AreEqual(2, result.WidthPx);
        Assert.AreEqual(1, result.HeightPx);
        Assert.HasCount(8, result.Data);
        Assert.AreEqual(128, result.Data[2], 1);
        Assert.AreEqual(128, result.Data[6], 1);
    }

    [TestMethod]
    public void Resize_InterpolatesTransparentEdgesInPremultipliedSpace()
    {
        var source = new Bgra32PixelBuffer(2, 1,
        [
            0, 0, 255, 0,
            255, 0, 0, 255,
        ]);

        var result = Bgra32ImageResampler.Resize(source, 1, 1);

        Assert.AreEqual(255, result.Data[0], 1);
        Assert.AreEqual(0, result.Data[1]);
        Assert.AreEqual(0, result.Data[2], 1);
        Assert.AreEqual(128, result.Data[3], 1);
    }

    [TestMethod]
    public void ComputeDecodeTarget_UsesDeviceFootprintAndKeepsIntrinsicCap()
    {
        var fhd = Image.ComputeDecodeTarget(
            8000,
            4000,
            ImageOrientation.Normal,
            new Rect(0, 0, 8000, 4000),
            new Rect(0, 0, 1920, 960),
            dpiScale: 1);
        var enlarged = Image.ComputeDecodeTarget(
            100,
            50,
            ImageOrientation.Normal,
            new Rect(0, 0, 100, 50),
            new Rect(0, 0, 500, 250),
            dpiScale: 1);

        Assert.AreEqual(1920, fhd.RawWidth);
        Assert.AreEqual(1024, fhd.RawHeight);
        Assert.AreEqual(100, enlarged.RawWidth);
        Assert.AreEqual(50, enlarged.RawHeight);
    }

    [TestMethod]
    public void DrawImageOrientedScaled_MapsIntrinsicSourceToResidentPixels()
    {
        var context = new RecordingGraphicsContext();
        using var image = new TestImage(400, 200);

        context.DrawImageOrientedScaled(
            image,
            ImageOrientation.Normal,
            intrinsicRawWidth: 4000,
            intrinsicRawHeight: 2000,
            residentRawWidth: 400,
            residentRawHeight: 200,
            intrinsicOrientedSrc: new Rect(1000, 500, 2000, 1000),
            dest: new Rect(10, 20, 300, 150));

        Assert.AreEqual(new Rect(100, 50, 200, 100), context.Source);
        Assert.AreEqual(new Rect(10, 20, 300, 150), context.Destination);
    }

    [TestMethod]
    public void DecodeReservation_IsVisibleOnlyWhileHeld()
    {
        var before = RenderMemoryLedger.Snapshot();

        using (ImageDecodeCoordinator.Acquire(4096))
        {
            var during = RenderMemoryLedger.Snapshot();
            Assert.AreEqual(before.DecodeTemporaryCount + 1, during.DecodeTemporaryCount);
            Assert.AreEqual(before.DecodeTemporaryBytes + 4096, during.DecodeTemporaryBytes);
        }

        var after = RenderMemoryLedger.Snapshot();
        Assert.AreEqual(before.DecodeTemporaryCount, after.DecodeTemporaryCount);
        Assert.AreEqual(before.DecodeTemporaryBytes, after.DecodeTemporaryBytes);
        Assert.IsGreaterThanOrEqualTo(4096, after.DecodeTemporaryPeakBytes);
    }

    private sealed class RecordingGraphicsContext : NoOpGraphicsContext
    {
        public Rect Source { get; private set; }
        public Rect Destination { get; private set; }

        public override void DrawImage(IImage image, Rect destRect, Rect sourceRect)
        {
            Destination = destRect;
            Source = sourceRect;
        }
    }

    private sealed class TestImage(int width, int height) : IImage
    {
        public int PixelWidth { get; } = width;
        public int PixelHeight { get; } = height;
        public void Dispose() { }
    }
}
